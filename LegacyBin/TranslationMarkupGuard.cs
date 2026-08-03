using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace LegacyBin
{
    /// <summary>
    /// Protects BNS UI markup (font tags, HTML entities, etc.) while text is sent to MT.
    /// Google free translate often turns &quot; into " and can mangle &lt;/tags — that can break in-game UI.
    /// </summary>
    public static class TranslationMarkupGuard
    {
        // Tags like <font name="...">, </font>, <image .../>, etc.
        private static readonly Regex TagRegex = new Regex(
            @"</?[A-Za-z][^<>]*>",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Named/numeric HTML entities as they appear literally in BNS strings.
        private static readonly Regex EntityRegex = new Regex(
            @"&(?:[A-Za-z][A-Za-z0-9]*|#\d+|#[xX][0-9A-Fa-f]+);",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Combined: find tags OR entities left-to-right.
        private static readonly Regex MarkupRegex = new Regex(
            @"</?[A-Za-z][^<>]*>|&(?:[A-Za-z][A-Za-z0-9]*|#\d+|#[xX][0-9A-Fa-f]+);",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Placeholders Google usually leaves alone (fullwidth + section sign).
        // Example: ⟦§0§⟧
        private static readonly Regex PlaceholderRegex = new Regex(
            @"⟦\s*§\s*(\d+)\s*§\s*⟧",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Fallback if MT strips the brackets but leaves the core token.
        private static readonly Regex PlaceholderLooseRegex = new Regex(
            @"§\s*(\d+)\s*§",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public sealed class ProtectedText
        {
            public string Text;
            public List<string> Tokens;
            public bool HadMarkup;
        }

        /// <summary>
        /// Replace markup/entities with placeholders. Returns plain-ish text safe to send to MT.
        /// </summary>
        public static ProtectedText Protect(string input)
        {
            var result = new ProtectedText
            {
                Text = input ?? string.Empty,
                Tokens = new List<string>(),
                HadMarkup = false
            };
            if (string.IsNullOrEmpty(input))
            {
                return result;
            }

            // Fast path: nothing to protect.
            if (!MightContainMarkup(input))
            {
                return result;
            }

            var tokens = new List<string>();
            string protectedText = MarkupRegex.Replace(input, m =>
            {
                int id = tokens.Count;
                tokens.Add(m.Value);
                return MakePlaceholder(id);
            });

            result.Text = protectedText;
            result.Tokens = tokens;
            result.HadMarkup = tokens.Count > 0;
            return result;
        }

        /// <summary>
        /// Put original tags/entities back. Tolerates light spacing changes around placeholders.
        /// </summary>
        public static string Unprotect(string translated, IList<string> tokens)
        {
            if (string.IsNullOrEmpty(translated) || tokens == null || tokens.Count == 0)
            {
                return translated ?? string.Empty;
            }

            string s = translated;

            // Primary restore (full placeholder form).
            s = PlaceholderRegex.Replace(s, m =>
            {
                int id;
                if (!int.TryParse(m.Groups[1].Value, out id) || id < 0 || id >= tokens.Count)
                {
                    return m.Value;
                }
                return tokens[id];
            });

            // If any tokens remain unreferenced, try loose §n§ form (MT stripped brackets).
            if (StillHasPlaceholders(s) || CountRestoredRough(s, tokens) < tokens.Count)
            {
                s = PlaceholderLooseRegex.Replace(s, m =>
                {
                    int id;
                    if (!int.TryParse(m.Groups[1].Value, out id) || id < 0 || id >= tokens.Count)
                    {
                        return m.Value;
                    }
                    // Avoid double-replacing if already restored as real markup containing §
                    return tokens[id];
                });
            }

            // Last resort: if placeholder tokens still present as MakePlaceholder text exactly.
            for (int i = 0; i < tokens.Count; i++)
            {
                string ph = MakePlaceholder(i);
                if (s.IndexOf(ph, StringComparison.Ordinal) >= 0)
                {
                    s = s.Replace(ph, tokens[i]);
                }
            }

            return s;
        }

        /// <summary>
        /// Protect → call translate → unprotect. If no markup, just returns the translated string.
        /// </summary>
        public static string ProtectTranslateUnprotect(string original, Func<string, string> translatePlain)
        {
            if (translatePlain == null)
            {
                throw new ArgumentNullException(nameof(translatePlain));
            }
            if (string.IsNullOrEmpty(original))
            {
                return original ?? string.Empty;
            }

            var p = Protect(original);
            if (!p.HadMarkup)
            {
                return translatePlain(original) ?? original;
            }

            // Nothing but markup? Don't call MT.
            if (IsOnlyPlaceholdersAndWhitespace(p.Text))
            {
                return original;
            }

            string mid = translatePlain(p.Text) ?? p.Text;
            return Unprotect(mid, p.Tokens);
        }

        public static async System.Threading.Tasks.Task<string> ProtectTranslateUnprotectAsync(
            string original,
            Func<string, System.Threading.Tasks.Task<string>> translatePlainAsync)
        {
            if (translatePlainAsync == null)
            {
                throw new ArgumentNullException(nameof(translatePlainAsync));
            }
            if (string.IsNullOrEmpty(original))
            {
                return original ?? string.Empty;
            }

            var p = Protect(original);
            if (!p.HadMarkup)
            {
                return await translatePlainAsync(original).ConfigureAwait(false) ?? original;
            }

            if (IsOnlyPlaceholdersAndWhitespace(p.Text))
            {
                return original;
            }

            string mid = await translatePlainAsync(p.Text).ConfigureAwait(false) ?? p.Text;
            return Unprotect(mid, p.Tokens);
        }

        public static string MakePlaceholder(int id)
        {
            return "⟦§" + id + "§⟧";
        }

        public static bool MightContainMarkup(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }
            // Cheap pre-check before regex.
            return s.IndexOf('<') >= 0 || s.IndexOf('&') >= 0;
        }

        private static bool StillHasPlaceholders(string s)
        {
            return s.IndexOf('⟦') >= 0 || (s.IndexOf('§') >= 0 && PlaceholderLooseRegex.IsMatch(s));
        }

        private static int CountRestoredRough(string s, IList<string> tokens)
        {
            int n = 0;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (!string.IsNullOrEmpty(tokens[i]) && s.IndexOf(tokens[i], StringComparison.Ordinal) >= 0)
                {
                    n++;
                }
            }
            return n;
        }

        private static bool IsOnlyPlaceholdersAndWhitespace(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return true;
            }
            string stripped = PlaceholderRegex.Replace(s, "");
            stripped = PlaceholderLooseRegex.Replace(stripped, "");
            return string.IsNullOrWhiteSpace(stripped);
        }

#if DEBUG
        // Helps manual checks; not used in release path.
        internal static bool LooksLikeTag(string s) => TagRegex.IsMatch(s ?? "");
        internal static bool LooksLikeEntity(string s) => EntityRegex.IsMatch(s ?? "");
#endif
    }
}
