using System;
using System.IO;
using System.Linq;

namespace LegacyBin.Ava
{
    /// <summary>CLI: merge srcXml into tgtXml (either translation or datafile table XML) → outXml.</summary>
    internal static class MergeCli
    {
        public static int Run(string source, string target, string output, bool fuzzyRenames)
        {
            try
            {
                var srcIn = LocalfileTranslation.LoadMergeInput(source);
                var tgtIn = LocalfileTranslation.LoadMergeInput(target);

                var merged = LocalfileTranslation.MergeByAlias(tgtIn.Entries, srcIn.Entries, out var st, fuzzyRenames);
                LocalfileTranslation.SaveMergeOutput(output, tgtIn, merged);

                Console.WriteLine("source: " + (srcIn.Kind == "table" ? "table id=" + srcIn.TableId : "translation XML") + " (" + srcIn.Entries.Count + " entries)");
                Console.WriteLine("target: " + (tgtIn.Kind == "table" ? "table id=" + tgtIn.TableId : "translation XML") + " (" + tgtIn.Entries.Count + " entries)");
                Console.WriteLine("merged: " + merged.Count + " entries ("
                    + st.Merged + " by alias, "
                    + (fuzzyRenames ? st.FuzzyMatched + " via 1-token rename, " : "")
                    + st.StructureMismatched + " structure-mismatched kept, "
                    + st.NoAliasMatch + " no-match)");
                if (fuzzyRenames && !string.IsNullOrEmpty(st.FuzzyRuleSummary))
                {
                    Console.WriteLine("rename rules:" + Environment.NewLine + st.FuzzyRuleSummary);
                }
                Console.WriteLine("out: " + output);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }
    }
}
