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

        // Entity-encoded tags like &lt;arg id="…" p="…"/&gt; — the bin stores arg/image
        // references this way. Protect the WHOLE construct as one atomic token so MT can
        // never mangle the icon/reference text inside it.
        private static readonly Regex EntityTagRegex = new Regex(
            @"&lt;/?[A-Za-z][^<>]*?&gt;",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        // Named/numeric HTML entities as they appear literally in BNS strings.
        private static readonly Regex EntityRegex = new Regex(
            @"&(?:[A-Za-z][A-Za-z0-9]*|#\d+|#[xX][0-9A-Fa-f]+);",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Combined: find tags, entity-encoded tags or entities left-to-right.
        private static readonly Regex MarkupRegex = new Regex(
            @"</?[A-Za-z][^<>]*>|&lt;/?[A-Za-z][^<>]*?&gt;|&(?:[A-Za-z][A-Za-z0-9]*|#\d+|#[xX][0-9A-Fa-f]+);",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

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
        /// <paramref name="original"/> (the pre-translate text) helps re-position tags whose
        /// placeholders MT dropped entirely.
        /// </summary>
        public static string Unprotect(string translated, IList<string> tokens, string original = null)
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

            // MT sometimes eats a placeholder entirely (no leftover §n§ at all) — usually for
            // leading tags like <image …/>. Re-insert such tags so UI icons/references survive.
            return RestoreMissingTags(s, tokens, original);
        }

        /// <summary>
        /// Re-insert tag tokens whose placeholders were dropped by MT. Placement is anchored on
        /// whatever sibling tags survived: each missing tag goes right after the previous
        /// restored tag, else just before the next restored tag, else proportional to where the
        /// tag sat in the original string (leading → start, otherwise end). Entity-encoded tags
        /// (&lt;arg …/&gt;) are treated like literal tags. Plain entities (&quot; etc.) are NOT
        /// re-inserted — NormalizeEntities re-escapes any bare chars MT produced instead.
        /// </summary>
        private static string RestoreMissingTags(string s, IList<string> tokens, string originalText = null)
        {
            if (tokens == null || tokens.Count == 0 || string.IsNullOrWhiteSpace(s))
            {
                return s;
            }

            // Find which tag tokens are missing, and where each restored tag landed in the output.
            var restored = new List<KeyValuePair<int, int>>(); // token index → output position
            var missing = new List<int>();
            for (int i = 0; i < tokens.Count; i++)
            {
                string tok = tokens[i];
                if (string.IsNullOrEmpty(tok)
                    || (tok[0] != '<' && !LooksLikeEntityTag(tok)))
                {
                    continue; // plain entities handled by NormalizeEntities
                }
                int pos = s.IndexOf(tok, StringComparison.Ordinal);
                if (pos >= 0)
                {
                    restored.Add(new KeyValuePair<int, int>(i, pos));
                }
                else
                {
                    missing.Add(i);
                }
            }
            if (missing.Count == 0)
            {
                return s;
            }

            // For each missing tag pick an anchor: right after the previous restored tag
            // (preserves open/close pairing), else just before the next restored tag,
            // else proportional to where the tag sat in the original text.
            var inserts = new List<KeyValuePair<int, string>>(); // position → tag
            foreach (int idx in missing)
            {
                int nextPos = -1;
                int prevEnd = -1;
                foreach (var kv in restored)
                {
                    if (kv.Key < idx)
                    {
                        // keep the nearest previous: ends after its tag text
                        prevEnd = kv.Value + tokens[kv.Key].Length;
                    }
                    else if (kv.Key > idx)
                    {
                        nextPos = kv.Value;
                        break;
                    }
                }
                int at;
                if (prevEnd >= 0)
                {
                    at = prevEnd;
                }
                else if (nextPos >= 0)
                {
                    at = nextPos;
                }
                else if (originalText != null)
                {
                    int srcIdx = originalText.IndexOf(tokens[idx], StringComparison.Ordinal);
                    double rel = srcIdx < 0
                        ? 1.0
                        : (double)srcIdx / Math.Max(1, originalText.Length);
                    at = rel < 0.25 ? 0 : s.Length;
                }
                else
                {
                    at = s.Length;
                }
                inserts.Add(new KeyValuePair<int, string>(at, tokens[idx]));
            }

            // Insert from the end so earlier positions stay valid.
            inserts.Sort((a, b) => b.Key.CompareTo(a.Key));
            var sb = new StringBuilder(s);
            foreach (var ins in inserts)
            {
                int pos = Math.Max(0, Math.Min(ins.Key, sb.Length));
                sb.Insert(pos, ins.Value);
            }
            return sb.ToString();
        }

        private static bool LooksLikeEntityTag(string tok)
        {
            return tok != null && tok.Length >= 4
                && string.Equals(tok.Substring(0, 4), "&lt;", StringComparison.OrdinalIgnoreCase);
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
            return NormalizeEntities(Unprotect(mid, p.Tokens, original));
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
                return NormalizeEntities(await translatePlainAsync(original).ConfigureAwait(false) ?? original);
            }

            if (IsOnlyPlaceholdersAndWhitespace(p.Text))
            {
                return original;
            }

            string mid = await translatePlainAsync(p.Text).ConfigureAwait(false) ?? p.Text;
            return NormalizeEntities(Unprotect(mid, p.Tokens, original));
        }

        public static string MakePlaceholder(int id)
        {
            return "⟦§" + id + "§⟧";
        }

        /// <summary>
        /// Re-escape bare markup characters that appear OUTSIDE tags/entities so the string
        /// survives round-trip through MT / merge. BNS stores quotes as &quot; and apostrophes
        /// as &apos; literally; Google often turns &quot; into " and &apos; into ' and drops
        /// the entity. Original bins never contain a bare " or ' outside a tag attribute, so
        /// re-escaping is always safe.
        ///
        /// Tag regions (matched by TagRegex) are left untouched so attribute quotes like
        /// <font name="..."> stay as-is. Inside text segments we:
        ///   • keep existing valid entities (&quot;, &apos;, &amp;, &#…;)
        ///   • "  → &quot;
        ///   • '  → &apos;
        ///   • &  → &amp;   (only when not already part of an entity)
        ///   • <  → &lt;    (bare < that didn't start a tag)
        ///   • >  → &gt;    (bare > outside a tag — e.g. from MT-mangled tag fragments)
        /// </summary>
        public static string NormalizeEntities(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return s ?? string.Empty;
            }
            // Fast path: nothing to fix.
            if (s.IndexOfAny(new[] { '"', '\'', '&', '<', '>' }) < 0)
            {
                return s;
            }

            var sb = new StringBuilder(s.Length + 16);
            int last = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<')
                {
                    var m = TagRegex.Match(s, i);
                    if (m.Success && m.Index == i)
                    {
                        // Tag region — copy verbatim, skip ahead.
                        if (i > last)
                        {
                            AppendNormalizedSegment(sb, s, last, i);
                        }
                        sb.Append(m.Value);
                        i += m.Value.Length - 1; // loop's i++ moves past
                        last = i + 1;
                        continue;
                    }
                }
                if (c == '&')
                {
                    var me = EntityTagRegex.Match(s, i);
                    if (me.Success && me.Index == i)
                    {
                        // Entity-encoded tag (&lt;arg …/&gt;) — verbatim, incl. attribute quotes.
                        if (i > last)
                        {
                            AppendNormalizedSegment(sb, s, last, i);
                        }
                        sb.Append(me.Value);
                        i += me.Value.Length - 1;
                        last = i + 1;
                        continue;
                    }
                }
            }
            if (last < s.Length)
            {
                AppendNormalizedSegment(sb, s, last, s.Length);
            }
            return sb.ToString();
        }

        private static void AppendNormalizedSegment(StringBuilder sb, string s, int start, int end)
        {
            int i = start;
            while (i < end)
            {
                char c = s[i];
                if (c == '&')
                {
                    var m = EntityRegex.Match(s, i);
                    if (m.Success && m.Index == i && i + m.Length <= end)
                    {
                        sb.Append(m.Value);
                        i += m.Length;
                        continue;
                    }
                    sb.Append("&amp;");
                    i++;
                }
                else if (c == '"')
                {
                    sb.Append("&quot;");
                    i++;
                }
                else if (c == '\'')
                {
                    sb.Append("&apos;");
                    i++;
                }
                else if (c == '<')
                {
                    // Bare < that TagRegex didn't claim as a tag start.
                    sb.Append("&lt;");
                    i++;
                }
                else if (c == '>')
                {
                    // Bare > outside a tag (e.g. from MT-mangled tag fragments).
                    sb.Append("&gt;");
                    i++;
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
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
