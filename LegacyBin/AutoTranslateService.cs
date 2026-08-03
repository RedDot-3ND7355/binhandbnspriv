using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LegacyBin
{
    /// <summary>
    /// Batch auto-translate for BnsDatTool-style Translation.xml entries.
    /// Primary use: after Merge-by-alias, fill rows whose alias was missing
    /// (still original == replacement) using the language pair learned from
    /// rows the merge already filled (original lang → replacement lang).
    /// </summary>
    public static class AutoTranslateService
    {
        /// <summary>How to lock source/target language for a run.</summary>
        public enum DetectMode
        {
            /// <summary>
            /// Post-merge (default): sample rows where original != replacement,
            /// detect both languages, lock that pair. Only fill untranslated gaps.
            /// Falls back to first untranslated → <see cref="Options.FallbackTargetLang"/>
            /// when no translated samples exist.
            /// </summary>
            FromTranslatedPairs = 0,

            /// <summary>Detect language of first untranslated original → fixed target.</summary>
            FirstUntranslatedToTarget = 1,
        }

        public sealed class Options
        {
            public string InputXmlPath;
            public string OutputXmlPath;
            /// <summary>Forced source lang; null/empty/auto = run detection.</summary>
            public string SourceLang = "auto";
            /// <summary>Forced target lang; null/empty/auto = run detection (pair mode) or fallback.</summary>
            public string TargetLang = "auto";
            /// <summary>Used when pair detection finds no samples (or FirstUntranslated mode).</summary>
            public string FallbackTargetLang = "en";
            public DetectMode Mode = DetectMode.FromTranslatedPairs;
            /// <summary>Only when original == replacement (and original non-empty). Keep true for gap-fill.</summary>
            public bool OnlyUntranslated = true;
            /// <summary>Optional cache path; default is input + ".gtcache.{sl}-{tl}" after detect.</summary>
            public string CachePath;
            /// <summary>
            /// Delay after each unique API call (ms), per worker. 0 = no pause.
            /// With concurrency &gt; 1, keep this low (0–30); raise if you see lots of 429s.
            /// </summary>
            public int DelayMs = 15;
            /// <summary>
            /// Parallel Google requests. 1 = old sequential behavior.
            /// Sweet spot is usually 4–8; 12+ often triggers more throttling.
            /// </summary>
            public int Concurrency = 6;
            /// <summary>How many translated-pair samples to vote on (each side).</summary>
            public int DetectSampleCount = 5;
            /// <summary>Mark entry type when replacement is written by auto-fill.</summary>
            public string TranslatedType = "auto";
        }

        public sealed class Progress
        {
            public int TotalEntries;
            public int UntranslatedEntries;
            public int UniqueToTranslate;
            public int UniqueDone;
            public int Applied;
            public int SkippedAlready;
            public int SkippedEmpty;
            public int CacheHits;
            public int TranslatedPairSamples;
            public string SourceLang;
            public string TargetLang;
            public string Phase;
            public string LastSample;
            public string Message;
            public int Percent;
            public int StepCurrent;
            public int StepTotal;
        }

        public sealed class Report
        {
            public int TotalEntries;
            public int UniqueTranslated;
            public int Applied;
            public int SkippedAlready;
            public int SkippedEmpty;
            public int CacheHits;
            public int TranslatedPairSamples;
            public string SourceLang;
            public string TargetLang;
            public string DetectNote;
            public string OutputPath;
            public string CachePath;
        }

        public static async Task<Report> RunAsync(
            Options options,
            IProgress<Progress> progress = null,
            CancellationToken cancel = default(CancellationToken))
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (string.IsNullOrWhiteSpace(options.InputXmlPath) || !File.Exists(options.InputXmlPath))
            {
                throw new FileNotFoundException("Input Translation XML not found.", options.InputXmlPath);
            }
            if (string.IsNullOrWhiteSpace(options.FallbackTargetLang))
            {
                options.FallbackTargetLang = "en";
            }

            bool autoCachePath = string.IsNullOrWhiteSpace(options.CachePath);
            bool autoOutputPath = string.IsNullOrWhiteSpace(options.OutputXmlPath);

            // Overall bar: load 0–5, scan 5–8, detect 8–12, translate 12–97, save 97–100.
            ReportProgress(progress, new Progress
            {
                Phase = "load",
                Percent = 0,
                StepCurrent = 0,
                StepTotal = 1,
                Message = "Loading " + options.InputXmlPath + " …"
            });

            List<LocalfileTranslation.Entry> entries = null;
            await Task.Run(() =>
            {
                entries = LocalfileTranslation.LoadXml(options.InputXmlPath);
            }, cancel).ConfigureAwait(false);

            cancel.ThrowIfCancellationRequested();

            ReportProgress(progress, new Progress
            {
                Phase = "load",
                Percent = 5,
                StepCurrent = 1,
                StepTotal = 1,
                TotalEntries = entries.Count,
                Message = "Loaded " + entries.Count + " entries — scanning gaps vs already-translated …"
            });

            int skippedAlready = 0;
            int skippedEmpty = 0;
            var needByOriginal = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var translatedPairs = new List<Tuple<string, string>>();

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                string orig = e.Original ?? string.Empty;
                string rep = e.Replacement ?? string.Empty;

                if (string.IsNullOrEmpty(orig))
                {
                    skippedEmpty++;
                    continue;
                }

                // Already filled by merge (or prior work): different non-empty replacement.
                bool hasTranslatedPair = !string.IsNullOrEmpty(rep)
                    && !string.Equals(orig, rep, StringComparison.Ordinal);
                // Gap: still needs a replacement (same text, or empty replacement).
                bool needsFill = string.IsNullOrEmpty(rep)
                    || string.Equals(orig, rep, StringComparison.Ordinal);

                if (hasTranslatedPair)
                {
                    translatedPairs.Add(Tuple.Create(orig, rep));
                }

                if (options.OnlyUntranslated && !needsFill)
                {
                    skippedAlready++;
                    continue;
                }

                // Gap (or force-all): queue for translate.
                List<int> list;
                if (!needByOriginal.TryGetValue(orig, out list))
                {
                    list = new List<int>();
                    needByOriginal[orig] = list;
                }
                list.Add(i);
            }

            int untranslatedEntries = needByOriginal.Sum(k => k.Value.Count);

            // --- Detect language pair ---
            string sourceLang = options.SourceLang;
            string targetLang = options.TargetLang;
            string detectNote;
            int pairSamplesUsed = 0;

            bool needSource = string.IsNullOrWhiteSpace(sourceLang)
                || sourceLang.Equals("auto", StringComparison.OrdinalIgnoreCase);
            bool needTarget = string.IsNullOrWhiteSpace(targetLang)
                || targetLang.Equals("auto", StringComparison.OrdinalIgnoreCase);

            ReportProgress(progress, new Progress
            {
                Phase = "detect",
                Percent = 8,
                StepCurrent = 0,
                StepTotal = 1,
                TotalEntries = entries.Count,
                UntranslatedEntries = untranslatedEntries,
                UniqueToTranslate = needByOriginal.Count,
                TranslatedPairSamples = translatedPairs.Count,
                SkippedAlready = skippedAlready,
                SkippedEmpty = skippedEmpty,
                Message = options.Mode == DetectMode.FromTranslatedPairs
                    ? ("Detecting language pair from " + translatedPairs.Count
                        + " already-translated row(s); gaps to fill: " + untranslatedEntries)
                    : "Detecting source language from first untranslated line …"
            });

            if (options.Mode == DetectMode.FromTranslatedPairs && (needSource || needTarget))
            {
                var result = await DetectPairFromTranslatedAsync(
                    translatedPairs,
                    Math.Max(1, options.DetectSampleCount),
                    cancel,
                    progress,
                    entries.Count,
                    untranslatedEntries,
                    needByOriginal.Count,
                    skippedAlready,
                    skippedEmpty).ConfigureAwait(false);

                pairSamplesUsed = result.SamplesUsed;
                if (needSource)
                {
                    sourceLang = result.SourceLang;
                }
                if (needTarget)
                {
                    targetLang = result.TargetLang;
                }
                detectNote = result.Note;
            }
            else if (needSource || needTarget)
            {
                string sample = needByOriginal.Keys.FirstOrDefault()
                    ?? entries.Select(e => e.Original).FirstOrDefault(s => !string.IsNullOrEmpty(s));
                if (needSource)
                {
                    sourceLang = string.IsNullOrEmpty(sample)
                        ? "auto"
                        : await GoogleFreeTranslate.DetectLanguageAsync(sample, options.FallbackTargetLang, cancel)
                            .ConfigureAwait(false);
                }
                if (needTarget)
                {
                    targetLang = options.FallbackTargetLang;
                }
                detectNote = "First-untranslated mode: " + sourceLang + " → " + targetLang;
            }
            else
            {
                detectNote = "Languages forced: " + sourceLang + " → " + targetLang;
            }

            // Safety fallbacks
            if (string.IsNullOrWhiteSpace(sourceLang) || sourceLang.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                string sample = needByOriginal.Keys.FirstOrDefault();
                if (!string.IsNullOrEmpty(sample))
                {
                    sourceLang = await GoogleFreeTranslate.DetectLanguageAsync(sample, options.FallbackTargetLang, cancel)
                        .ConfigureAwait(false);
                }
                else
                {
                    sourceLang = "auto";
                }
            }
            if (string.IsNullOrWhiteSpace(targetLang) || targetLang.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                targetLang = options.FallbackTargetLang;
            }

            if (string.Equals(sourceLang, targetLang, StringComparison.OrdinalIgnoreCase)
                && untranslatedEntries > 0)
            {
                detectNote += " (warning: source and target lang codes are the same — check merge samples)";
            }

            // Cache / output paths once langs are locked
            if (autoCachePath)
            {
                options.CachePath = options.InputXmlPath + ".gtcache." + sourceLang + "-" + targetLang;
            }
            if (autoOutputPath)
            {
                string dir = Path.GetDirectoryName(options.InputXmlPath) ?? ".";
                string name = Path.GetFileNameWithoutExtension(options.InputXmlPath);
                options.OutputXmlPath = Path.Combine(dir, name + "_filled.xml");
            }

            var cache = LoadCache(options.CachePath);
            int cacheHits = 0;
            var remaining = new List<string>();
            foreach (var kv in needByOriginal)
            {
                if (cache.TryGetValue(kv.Key, out string cached) && cached != null)
                {
                    cacheHits++;
                    ApplyTranslation(entries, kv.Value, cached, options.TranslatedType);
                }
                else
                {
                    remaining.Add(kv.Key);
                }
            }

            int applied = 0;
            foreach (var kv in needByOriginal)
            {
                if (cache.ContainsKey(kv.Key))
                {
                    applied += kv.Value.Count;
                }
            }

            int concurrency = options.Concurrency < 1 ? 1 : Math.Min(options.Concurrency, 16);
            int delayMs = options.DelayMs < 0 ? 0 : options.DelayMs;

            ReportProgress(progress, new Progress
            {
                Phase = "translate",
                Percent = TranslatePercent(0, remaining.Count),
                StepCurrent = 0,
                StepTotal = Math.Max(1, remaining.Count),
                TotalEntries = entries.Count,
                UntranslatedEntries = untranslatedEntries,
                UniqueToTranslate = remaining.Count,
                UniqueDone = 0,
                Applied = applied,
                CacheHits = cacheHits,
                SkippedAlready = skippedAlready,
                SkippedEmpty = skippedEmpty,
                TranslatedPairSamples = translatedPairs.Count,
                SourceLang = sourceLang,
                TargetLang = targetLang,
                Message = "Language pair locked: " + sourceLang + " → " + targetLang
                    + ". Unique gaps: " + remaining.Count
                    + " | workers=" + concurrency + " delayMs=" + delayMs
                    + (pairSamplesUsed > 0 ? " | pair samples=" + pairSamplesUsed : "")
            });

            int uniqueDone = 0;
            int cacheDirty = 0;
            const int cacheFlushEvery = 40;
            int remainingCount = remaining.Count;
            var sync = new object();
            int nextIndex = 0;

            // Worker pool: pull next unique gap, translate, update shared state under lock.
            int workerCount = remainingCount == 0 ? 0 : Math.Min(concurrency, remainingCount);
            var workers = new Task[workerCount];
            for (int w = 0; w < workerCount; w++)
            {
                workers[w] = Task.Run(async () =>
                {
                    while (true)
                    {
                        cancel.ThrowIfCancellationRequested();

                        string original;
                        lock (sync)
                        {
                            if (nextIndex >= remainingCount)
                            {
                                return;
                            }
                            original = remaining[nextIndex++];
                        }

                        string errorMsg = null;
                        string translated;
                        try
                        {
                            // Protect <font> / &quot; / &lt; etc. so MT cannot unescape or mangle UI markup.
                            translated = await TranslationMarkupGuard.ProtectTranslateUnprotectAsync(
                                original,
                                async plain =>
                                {
                                    var tr = await GoogleFreeTranslate.TranslateAsync(
                                        plain, targetLang, sourceLang, cancel).ConfigureAwait(false);
                                    return tr.TranslatedText ?? plain;
                                }).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            errorMsg = ex.Message;
                            translated = original;
                        }

                        if (string.IsNullOrEmpty(translated))
                        {
                            translated = original;
                        }

                        int localDone;
                        int localApplied;
                        lock (sync)
                        {
                            cache[original] = translated;
                            cacheDirty++;
                            ApplyTranslation(entries, needByOriginal[original], translated, options.TranslatedType);
                            applied += needByOriginal[original].Count;
                            uniqueDone++;
                            localDone = uniqueDone;
                            localApplied = applied;

                            if (cacheDirty >= cacheFlushEvery)
                            {
                                try
                                {
                                    SaveCache(options.CachePath, cache);
                                    cacheDirty = 0;
                                }
                                catch (Exception ex)
                                {
                                    // Don't kill the run for a cache flush glitch; try again later.
                                    errorMsg = (errorMsg ?? "") + " cache-flush: " + ex.Message;
                                }
                            }
                        }

                        ReportProgress(progress, new Progress
                        {
                            Phase = "translate",
                            Percent = TranslatePercent(localDone, remainingCount),
                            StepCurrent = localDone,
                            StepTotal = Math.Max(1, remainingCount),
                            TotalEntries = entries.Count,
                            UntranslatedEntries = untranslatedEntries,
                            UniqueToTranslate = remainingCount,
                            UniqueDone = localDone,
                            Applied = localApplied,
                            CacheHits = cacheHits,
                            SkippedAlready = skippedAlready,
                            SkippedEmpty = skippedEmpty,
                            TranslatedPairSamples = translatedPairs.Count,
                            SourceLang = sourceLang,
                            TargetLang = targetLang,
                            LastSample = Clip(original, 60) + " → " + Clip(translated, 60),
                            Message = errorMsg != null
                                ? ("Error (kept original): " + errorMsg)
                                : ("Filled unique gap " + localDone + " / " + remainingCount
                                    + " ×" + concurrency)
                        });

                        if (delayMs > 0)
                        {
                            await Task.Delay(delayMs, cancel).ConfigureAwait(false);
                        }
                    }
                }, cancel);
            }

            if (workerCount > 0)
            {
                try
                {
                    await Task.WhenAll(workers).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Flush whatever we have before rethrowing so cancel still resumes well.
                    lock (sync)
                    {
                        if (cacheDirty > 0)
                        {
                            try { SaveCache(options.CachePath, cache); } catch { /* ignore */ }
                            cacheDirty = 0;
                        }
                    }
                    throw;
                }
            }

            lock (sync)
            {
                if (cacheDirty > 0)
                {
                    SaveCache(options.CachePath, cache);
                    cacheDirty = 0;
                }
            }

            ReportProgress(progress, new Progress
            {
                Phase = "save",
                Percent = 97,
                StepCurrent = uniqueDone,
                StepTotal = Math.Max(1, remainingCount),
                TotalEntries = entries.Count,
                UniqueToTranslate = remainingCount,
                UniqueDone = uniqueDone,
                Applied = applied,
                CacheHits = cacheHits,
                SkippedAlready = skippedAlready,
                SkippedEmpty = skippedEmpty,
                TranslatedPairSamples = translatedPairs.Count,
                SourceLang = sourceLang,
                TargetLang = targetLang,
                Message = "Saving " + options.OutputXmlPath + " …"
            });

            await Task.Run(() =>
            {
                LocalfileTranslation.SaveXml(options.OutputXmlPath, entries);
            }, cancel).ConfigureAwait(false);

            ReportProgress(progress, new Progress
            {
                Phase = "save",
                Percent = 100,
                StepCurrent = uniqueDone,
                StepTotal = Math.Max(1, remainingCount),
                TotalEntries = entries.Count,
                UniqueToTranslate = remainingCount,
                UniqueDone = uniqueDone,
                Applied = applied,
                CacheHits = cacheHits,
                SkippedAlready = skippedAlready,
                SkippedEmpty = skippedEmpty,
                TranslatedPairSamples = translatedPairs.Count,
                SourceLang = sourceLang,
                TargetLang = targetLang,
                Message = "Saved " + options.OutputXmlPath
            });

            return new Report
            {
                TotalEntries = entries.Count,
                UniqueTranslated = uniqueDone,
                Applied = applied,
                SkippedAlready = skippedAlready,
                SkippedEmpty = skippedEmpty,
                CacheHits = cacheHits,
                TranslatedPairSamples = translatedPairs.Count,
                SourceLang = sourceLang,
                TargetLang = targetLang,
                DetectNote = detectNote,
                OutputPath = options.OutputXmlPath,
                CachePath = options.CachePath
            };
        }

        private sealed class PairDetectResult
        {
            public string SourceLang;
            public string TargetLang;
            public int SamplesUsed;
            public string Note;
        }

        /// <summary>
        /// From rows where original != replacement (typical after successful alias merge),
        /// detect original language and replacement language and lock them as the pair.
        /// </summary>
        private static async Task<PairDetectResult> DetectPairFromTranslatedAsync(
            List<Tuple<string, string>> pairs,
            int sampleCount,
            CancellationToken cancel,
            IProgress<Progress> progress,
            int totalEntries,
            int untranslatedEntries,
            int uniqueGaps,
            int skippedAlready,
            int skippedEmpty)
        {
            var result = new PairDetectResult
            {
                SourceLang = "auto",
                TargetLang = "en",
                SamplesUsed = 0,
                Note = ""
            };

            if (pairs == null || pairs.Count == 0)
            {
                result.Note = "No already-translated rows (original != replacement). "
                    + "Falling back to first untranslated → en. Merge a source XML first for best results.";
                return result;
            }

            // Prefer longer, more distinctive samples; skip tiny tokens.
            var ranked = pairs
                .Where(p => !string.IsNullOrWhiteSpace(p.Item1) && !string.IsNullOrWhiteSpace(p.Item2))
                .Where(p => p.Item1.Trim().Length >= 2 && p.Item2.Trim().Length >= 2)
                .OrderByDescending(p => Math.Min(p.Item1.Length, p.Item2.Length))
                .ToList();

            if (ranked.Count == 0)
            {
                result.Note = "Translated rows too short to detect languages; fallback → en.";
                return result;
            }

            var origSamples = new List<string>();
            var repSamples = new List<string>();
            var seenOrig = new HashSet<string>(StringComparer.Ordinal);
            var seenRep = new HashSet<string>(StringComparer.Ordinal);

            foreach (var p in ranked)
            {
                if (origSamples.Count >= sampleCount && repSamples.Count >= sampleCount)
                {
                    break;
                }
                if (origSamples.Count < sampleCount && seenOrig.Add(p.Item1))
                {
                    origSamples.Add(p.Item1);
                }
                if (repSamples.Count < sampleCount && seenRep.Add(p.Item2))
                {
                    repSamples.Add(p.Item2);
                }
            }

            ReportProgress(progress, new Progress
            {
                Phase = "detect",
                Percent = 9,
                TotalEntries = totalEntries,
                UntranslatedEntries = untranslatedEntries,
                UniqueToTranslate = uniqueGaps,
                TranslatedPairSamples = pairs.Count,
                SkippedAlready = skippedAlready,
                SkippedEmpty = skippedEmpty,
                Message = "Detecting original language from " + origSamples.Count + " sample(s) …",
                LastSample = Clip(origSamples.FirstOrDefault(), 80)
            });

            string src = await DetectMajorityLanguageAsync(origSamples, cancel).ConfigureAwait(false);

            ReportProgress(progress, new Progress
            {
                Phase = "detect",
                Percent = 11,
                TotalEntries = totalEntries,
                UntranslatedEntries = untranslatedEntries,
                UniqueToTranslate = uniqueGaps,
                TranslatedPairSamples = pairs.Count,
                SkippedAlready = skippedAlready,
                SkippedEmpty = skippedEmpty,
                SourceLang = src,
                Message = "Detecting replacement language from " + repSamples.Count + " sample(s) …",
                LastSample = Clip(repSamples.FirstOrDefault(), 80)
            });

            string tgt = await DetectMajorityLanguageAsync(repSamples, cancel).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(src) || src.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                src = "auto";
            }
            if (string.IsNullOrWhiteSpace(tgt) || tgt.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                tgt = "en";
            }

            result.SourceLang = src;
            result.TargetLang = tgt;
            result.SamplesUsed = Math.Max(origSamples.Count, repSamples.Count);
            result.Note = "Pair from merged/translated rows: original=" + src
                + ", replacement=" + tgt
                + " (samples " + origSamples.Count + "/" + repSamples.Count
                + " of " + pairs.Count + " translated rows)";
            return result;
        }

        private static async Task<string> DetectMajorityLanguageAsync(
            IList<string> samples,
            CancellationToken cancel)
        {
            if (samples == null || samples.Count == 0)
            {
                return "auto";
            }
            var votes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string sample in samples)
            {
                cancel.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(sample))
                {
                    continue;
                }
                string lang = await GoogleFreeTranslate.DetectLanguageAsync(sample, "en", cancel)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(lang) || lang.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                int c;
                votes.TryGetValue(lang, out c);
                votes[lang] = c + 1;
                await Task.Delay(40, cancel).ConfigureAwait(false);
            }
            if (votes.Count == 0)
            {
                return "auto";
            }
            return votes.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
        }

        private static void ApplyTranslation(
            List<LocalfileTranslation.Entry> entries,
            List<int> indices,
            string translated,
            string type)
        {
            foreach (int i in indices)
            {
                entries[i].Replacement = translated;
                if (!string.IsNullOrEmpty(type))
                {
                    entries[i].Type = type;
                }
            }
        }

        private static void ReportProgress(IProgress<Progress> progress, Progress p)
        {
            progress?.Report(p);
        }

        private static int TranslatePercent(int done, int total)
        {
            if (total <= 0)
            {
                return 97;
            }
            if (done <= 0)
            {
                return 12;
            }
            if (done >= total)
            {
                return 97;
            }
            return 12 + (int)((done * 85L) / total);
        }

        private static string Clip(string s, int max)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        /// <summary>
        /// Cache file: GT1 header, then O/T base64 lines per unique original.
        /// </summary>
        public static Dictionary<string, string> LoadCache(string path)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return map;
            }
            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length == 0 || lines[0] != "GT1")
                {
                    return map;
                }
                string pendingOriginal = null;
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.Length < 3)
                    {
                        continue;
                    }
                    if (line.StartsWith("O ", StringComparison.Ordinal))
                    {
                        pendingOriginal = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(2)));
                    }
                    else if (line.StartsWith("T ", StringComparison.Ordinal) && pendingOriginal != null)
                    {
                        string t = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(2)));
                        map[pendingOriginal] = t;
                        pendingOriginal = null;
                    }
                }
            }
            catch
            {
                map.Clear();
            }
            return map;
        }

        public static void SaveCache(string path, Dictionary<string, string> map)
        {
            if (string.IsNullOrEmpty(path) || map == null)
            {
                return;
            }
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var sb = new StringBuilder(map.Count * 64);
            sb.AppendLine("GT1");
            foreach (var kv in map)
            {
                sb.Append("O ");
                sb.AppendLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(kv.Key ?? string.Empty)));
                sb.Append("T ");
                sb.AppendLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(kv.Value ?? string.Empty)));
            }
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(tmp, path);
        }

        /// <summary>Count rows still needing a replacement (original == replacement, non-empty).</summary>
        public static void CountMergeGaps(IList<LocalfileTranslation.Entry> entries, out int filled, out int gaps, out int empty)
        {
            filled = 0;
            gaps = 0;
            empty = 0;
            if (entries == null)
            {
                return;
            }
            foreach (var e in entries)
            {
                string o = e.Original ?? string.Empty;
                string r = e.Replacement ?? string.Empty;
                if (string.IsNullOrEmpty(o))
                {
                    empty++;
                }
                else if (string.Equals(o, r, StringComparison.Ordinal))
                {
                    gaps++;
                }
                else
                {
                    filled++;
                }
            }
        }
    }
}
