using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LegacyBin.Ava
{
    /// <summary>
    /// CLI: translate-apply &lt;merged.xml&gt; &lt;translations.jsonl&gt; &lt;out.xml&gt;
    /// Applies manual translations ({idx, translation} per line, keyed by lookup ordinal)
    /// to a copy of a datafile table XML using the engine's own merge/write path.
    /// </summary>
    internal static class TranslateApplyCli
    {
        public static int Run(string tableXml, string translationsJsonl, string outXml)
        {
            var byIdx = new Dictionary<int, string>();
            foreach (var line in File.ReadAllLines(translationsJsonl))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                using (var doc = JsonDocument.Parse(line))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("idx", out var idxEl)
                        && root.TryGetProperty("translation", out var trEl)
                        && trEl.ValueKind == JsonValueKind.String)
                    {
                        byIdx[idxEl.GetInt32()] = trEl.GetString();
                    }
                }
            }
            Console.WriteLine("translations loaded: " + byIdx.Count);

            var input = LocalfileTranslation.LoadMergeInput(tableXml);
            int applied = 0;
            for (int i = 0; i < input.Entries.Count && byIdx.Count > 0; i++)
            {
                if (byIdx.TryGetValue(i, out string tr))
                {
                    var e = input.Entries[i];
                    if (!string.IsNullOrEmpty(tr))
                    {
                        // only replace when the slot still holds the untranslated text
                        if (string.IsNullOrEmpty(e.Replacement) || e.Replacement == e.Original)
                        {
                            e.Replacement = tr;
                            e.Type = "manual";
                            applied++;
                        }
                        byIdx.Remove(i);
                    }
                }
            }
            Console.WriteLine("applied: " + applied + " (unmatched idx: " + byIdx.Count + ")");
            LocalfileTranslation.SaveMergeOutput(outXml, input, input.Entries);
            Console.WriteLine("out: " + outXml);
            return 0;
        }
    }
}
