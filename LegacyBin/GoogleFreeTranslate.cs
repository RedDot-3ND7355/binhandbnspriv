using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace LegacyBin
{
    /// <summary>
    /// Unofficial Google Translate free endpoint (client=gtx). No API key.
    /// Suitable for personal offline tooling; rate-limit aware with retries.
    /// </summary>
    public static class GoogleFreeTranslate
    {
        private const string Endpoint = "https://translate.googleapis.com/translate_a/single";
        private const int MaxGetChars = 1800;
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        static GoogleFreeTranslate()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                // DefaultConnectionLimit is 2 — that alone serializes "parallel" translates.
                if (ServicePointManager.DefaultConnectionLimit < 32)
                {
                    ServicePointManager.DefaultConnectionLimit = 32;
                }
                try
                {
                    var sp = ServicePointManager.FindServicePoint(new Uri("https://translate.googleapis.com"));
                    if (sp != null && sp.ConnectionLimit < 32)
                    {
                        sp.ConnectionLimit = 32;
                    }
                }
                catch
                {
                    // ignore
                }
            }
            catch
            {
                // ignore on locked-down hosts
            }
        }

        public sealed class Result
        {
            public string TranslatedText;
            public string DetectedLanguage;
        }

        /// <summary>
        /// Translate text. When <paramref name="sourceLang"/> is null/empty/"auto",
        /// Google auto-detects and <see cref="Result.DetectedLanguage"/> is filled.
        /// </summary>
        public static async Task<Result> TranslateAsync(
            string text,
            string targetLang = "en",
            string sourceLang = "auto",
            CancellationToken cancel = default(CancellationToken))
        {
            if (string.IsNullOrEmpty(text))
            {
                return new Result { TranslatedText = text ?? string.Empty, DetectedLanguage = sourceLang };
            }
            if (string.IsNullOrWhiteSpace(targetLang))
            {
                targetLang = "en";
            }
            if (string.IsNullOrWhiteSpace(sourceLang))
            {
                sourceLang = "auto";
            }

            cancel.ThrowIfCancellationRequested();

            string json = await RequestWithRetryAsync(text, sourceLang, targetLang, cancel).ConfigureAwait(false);
            return ParseResponse(json, sourceLang);
        }

        /// <summary>Detect language of a sample string (uses translate with sl=auto).</summary>
        public static async Task<string> DetectLanguageAsync(
            string sample,
            string targetLang = "en",
            CancellationToken cancel = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(sample))
            {
                return "auto";
            }
            // Short sample is enough for detection
            string clip = sample.Length > 200 ? sample.Substring(0, 200) : sample;
            var r = await TranslateAsync(clip, targetLang, "auto", cancel).ConfigureAwait(false);
            return string.IsNullOrEmpty(r.DetectedLanguage) ? "auto" : r.DetectedLanguage;
        }

        private static async Task<string> RequestWithRetryAsync(
            string text, string sourceLang, string targetLang, CancellationToken cancel)
        {
            const int maxAttempts = 6;
            Exception last = null;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                cancel.ThrowIfCancellationRequested();
                try
                {
                    return await RequestOnceAsync(text, sourceLang, targetLang, cancel).ConfigureAwait(false);
                }
                catch (WebException ex)
                {
                    last = ex;
                    int delayMs = 400 * (1 << Math.Min(attempt, 5));
                    var resp = ex.Response as HttpWebResponse;
                    if (resp != null && ((int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500))
                    {
                        delayMs = Math.Max(delayMs, 1500 * (attempt + 1));
                    }
                    await Task.Delay(delayMs, cancel).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    last = ex;
                    await Task.Delay(400 * (1 << Math.Min(attempt, 4)), cancel).ConfigureAwait(false);
                }
            }
            throw new InvalidOperationException("Google Translate request failed after retries.", last);
        }

        private static Task<string> RequestOnceAsync(
            string text, string sourceLang, string targetLang, CancellationToken cancel)
        {
            // Prefer GET for short strings; POST form body for longer ones.
            bool usePost = text.Length > MaxGetChars;
            string url;
            if (usePost)
            {
                url = Endpoint + "?client=gtx&sl=" + Uri.EscapeDataString(sourceLang)
                    + "&tl=" + Uri.EscapeDataString(targetLang) + "&dt=t";
            }
            else
            {
                url = Endpoint + "?client=gtx&sl=" + Uri.EscapeDataString(sourceLang)
                    + "&tl=" + Uri.EscapeDataString(targetLang) + "&dt=t&q=" + Uri.EscapeDataString(text);
            }

            var tcs = new TaskCompletionSource<string>();
            var reg = cancel.Register(() => tcs.TrySetCanceled(cancel));

            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = usePost ? "POST" : "GET";
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) LegacyBin/1.0";
            req.Accept = "*/*";
            req.Timeout = 60000;
            req.ReadWriteTimeout = 60000;
            if (usePost)
            {
                req.ContentType = "application/x-www-form-urlencoded;charset=UTF-8";
                byte[] body = Encoding.UTF8.GetBytes("q=" + Uri.EscapeDataString(text));
                req.ContentLength = body.Length;
                req.BeginGetRequestStream(arWrite =>
                {
                    try
                    {
                        cancel.ThrowIfCancellationRequested();
                        using (var stream = req.EndGetRequestStream(arWrite))
                        {
                            stream.Write(body, 0, body.Length);
                        }
                        BeginGetResponse(req, tcs, reg, cancel);
                    }
                    catch (Exception ex)
                    {
                        reg.Dispose();
                        tcs.TrySetException(ex);
                    }
                }, null);
            }
            else
            {
                BeginGetResponse(req, tcs, reg, cancel);
            }

            return tcs.Task;
        }

        private static void BeginGetResponse(
            HttpWebRequest req,
            TaskCompletionSource<string> tcs,
            CancellationTokenRegistration reg,
            CancellationToken cancel)
        {
            req.BeginGetResponse(ar =>
            {
                try
                {
                    cancel.ThrowIfCancellationRequested();
                    using (var resp = (HttpWebResponse)req.EndGetResponse(ar))
                    using (var stream = resp.GetResponseStream())
                    using (var reader = new System.IO.StreamReader(stream, Encoding.UTF8))
                    {
                        string json = reader.ReadToEnd();
                        reg.Dispose();
                        tcs.TrySetResult(json);
                    }
                }
                catch (Exception ex)
                {
                    reg.Dispose();
                    tcs.TrySetException(ex);
                }
            }, null);
        }

        private static Result ParseResponse(string json, string fallbackLang)
        {
            var result = new Result
            {
                TranslatedText = string.Empty,
                DetectedLanguage = fallbackLang == "auto" ? null : fallbackLang
            };
            if (string.IsNullOrEmpty(json))
            {
                return result;
            }

            // Response shape: [ [ ["translated","original",...], ... ], null, "detectedLang", ... ]
            object rootObj = Json.DeserializeObject(json);
            var root = rootObj as object[];
            if (root == null || root.Length == 0)
            {
                return result;
            }

            var sb = new StringBuilder();
            var sentences = root[0] as object[];
            if (sentences != null)
            {
                foreach (var item in sentences)
                {
                    var row = item as object[];
                    if (row != null && row.Length > 0 && row[0] != null)
                    {
                        sb.Append(Convert.ToString(row[0]));
                    }
                }
            }
            result.TranslatedText = sb.ToString();

            if (root.Length > 2 && root[2] != null)
            {
                string detected = Convert.ToString(root[2]);
                if (!string.IsNullOrEmpty(detected))
                {
                    result.DetectedLanguage = detected;
                }
            }

            return result;
        }
    }
}
