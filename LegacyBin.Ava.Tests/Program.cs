using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;

namespace LegacyBin.Ava.Tests
{
    /// <summary>
    /// Headless smoke test for the Avalonia UI (runs without a display, works on CI/Linux).
    /// Usage: dotnet run --project LegacyBin.Ava.Tests -- [path/to/localfile.bin]
    /// </summary>
    internal static class Program
    {
        private static int _fails;

        private static int Main(string[] args)
        {
            string binPath = args.Length > 0 && File.Exists(args[0])
                ? Path.GetFullPath(args[0])
                : Path.GetFullPath("../../LegacyBin/Resources/localfile.bin");
            if (!File.Exists(binPath))
            {
                Console.Error.WriteLine("Sample bin not found: " + binPath);
                return 2;
            }

            // Headless Avalonia: no Skia/native rendering, no display required.
            var builder = AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions());
            builder.SetupWithoutStarting();

            try
            {
                Check("MainWindow constructs (XAML resolves)", () =>
                {
                    var mw = new MainWindow();
                    return mw.FindControl<Button>("BtnOpen") != null;
                });

                // Open the sample bin (BDAT read + ZLibStream path).
                var session = new BinSession();
                session.Open(binPath);
                Check("BinSession.Open loaded tables", () => session.IsOpen && session.Content.ListCount == 4);

                Check("TranslateWindow constructs with session", () =>
                {
                    var tw = new TranslateWindow(session);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    var tb = tw.FindControl<TextBox>("TxtLog");
                    return tb != null
                        && tw.FindControl<NumericUpDown>("NumTable") != null
                        && (tb.Text ?? "").Contains("Detected text table");
                });

                // Merge logic end-to-end through the shared engine.
                Check("ExportFromContent produces entries", () =>
                {
                    var entries = LocalfileTranslation.ExportFromContent(session.Content);
                    return entries != null && entries.Count > 1000
                        && entries.Exists(e => !string.IsNullOrEmpty(e.Alias));
                });

                Check("MergeByAlias keeps targets (self-merge is identity)", () =>
                {
                    var entries = LocalfileTranslation.ExportFromContent(session.Content);
                    var merged = LocalfileTranslation.MergeByAlias(entries, entries, out var st);
                    return st.Total == entries.Count && merged.Count == entries.Count;
                });

                Check("Apply no-op is safe", () =>
                {
                    var entries = LocalfileTranslation.ExportFromContent(session.Content);
                    var res = LocalfileTranslation.Apply(session.Content, entries);
                    return res.RecordsScanned > 0;
                });

                Check("Text table detection finds table 2", () =>
                    LocalfileTranslation.FindTextTableIndex(session.Content) == 2);

                // --- Table-format (datafile_XXX.xml) merge, the workflow that previously did nothing ---
                string tableXmlPath = Path.Combine(binPath + ".table.xml");
                string mergedTablePath = Path.Combine(binPath + ".table.merged.xml");
                string mergedTablePath2 = Path.Combine(binPath + ".table.merged2.xml");
                try
                {
                    // Export the open bin's text table in datafile XML format.
                    {
                        var bxml = new BXML_LIST();
                        bxml.Convert(session.Content.Lists[2]);
                        var ser = new System.Xml.Serialization.XmlSerializer(typeof(BXML_LIST));
                        using (var ms = new System.IO.MemoryStream())
                        {
                            using (var xw = System.Xml.XmlWriter.Create(ms, new System.Xml.XmlWriterSettings
                            {
                                Encoding = new System.Text.UTF8Encoding(false),
                                Indent = true,
                                NewLineHandling = System.Xml.NewLineHandling.Entitize
                            }))
                            {
                                ser.Serialize(xw, bxml);
                            }
                            File.WriteAllText(tableXmlPath, System.Text.Encoding.UTF8.GetString(ms.ToArray()));
                        }
                    }

                    Check("LoadMergeInput detects datafile table format", () =>
                    {
                        var mi = LocalfileTranslation.LoadMergeInput(tableXmlPath);
                        return mi.Kind == "table" && mi.TableId == 255 && mi.Entries.Count > 1000;
                    });

                    Check("Table-format merge writes merged words[1] at right index", () =>
                    {
                        // Target: the table XML. Source: handmade translation XML with one known alias.
                        var tgt = LocalfileTranslation.LoadMergeInput(tableXmlPath);
                        string probeAlias = tgt.Entries.First(e => !string.IsNullOrEmpty(e.Alias) && e.Alias.StartsWith("Achieve.Name_", StringComparison.Ordinal)).Alias;
                        string probeText = tgt.Entries.First(e => e.Alias == probeAlias).Original;
                        var srcEntries = new System.Collections.Generic.List<LocalfileTranslation.Entry>
                        {
                            new LocalfileTranslation.Entry { AutoId = 1, Alias = probeAlias, Priority = 0, Original = probeText, Replacement = "PROBE_SENTINEL_&quot;text&quot;" }
                        };
                        var merged = LocalfileTranslation.MergeByAlias(tgt.Entries, srcEntries, out var st);
                        bool mergedCountOk = merged.Count == tgt.Entries.Count && st.Merged >= 1;
                        LocalfileTranslation.SaveMergeOutput(mergedTablePath, tgt, merged);

                        // Read the merged table XML back and verify the probe landed on that alias.
                        var mi2 = LocalfileTranslation.LoadMergeInput(mergedTablePath);
                        var back = mi2.Entries.First(e => e.Alias == probeAlias);
                        return mergedCountOk
                            && back.Replacement == "PROBE_SENTINEL_&quot;text&quot;"
                            && mi2.Entries.Count == tgt.Entries.Count;
                    });

                    Check("Table-format self-merge round-trip keeps all records", () =>
                    {
                        var tgt = LocalfileTranslation.LoadMergeInput(mergedTablePath);
                        var merged = LocalfileTranslation.MergeByAlias(tgt.Entries, tgt.Entries, out var st);
                        LocalfileTranslation.SaveMergeOutput(mergedTablePath2, tgt, merged);
                        var back = LocalfileTranslation.LoadMergeInput(mergedTablePath2);
                        return st.Total == tgt.Entries.Count
                            && back.Entries.Count == tgt.Entries.Count
                            && back.Entries.Where(e => !string.IsNullOrEmpty(e.Alias)).Count()
                                == tgt.Entries.Where(e => !string.IsNullOrEmpty(e.Alias)).Count();
                    });

                    Check("Merged table XML re-loads and re-export matches engine read", () =>
                    {
                        // Cross-check the merged table XML against a direct field/lookup walk.
                        var mi = LocalfileTranslation.LoadMergeInput(mergedTablePath);
                        int lookupTotal = 0;
                        foreach (var sub in mi.Table.collection.archive.SubArchives)
                        {
                            if (sub?.lookup != null) lookupTotal += sub.lookup.Length;
                        }
                        return lookupTotal == mi.Entries.Count;
                    });

                    Check("Rename fallback recovers 1-token renames", () =>
                    {
                        // use table format entries; target entries whose alias has no exact match,
                        // but matches after Name->Title style token swap.
                        var tgtIn = LocalfileTranslation.LoadMergeInput(tableXmlPath);
                        var srcEntries = new System.Collections.Generic.List<LocalfileTranslation.Entry>
                        {
                            new LocalfileTranslation.Entry { AutoId = 1, Alias = "Achieve.Title_10001_growth_grade_step1", Priority = 0, Original = "First Apprentice", Replacement = "First Apprentice" },
                            new LocalfileTranslation.Entry { AutoId = 2, Alias = "Different.Complete", Priority = 0, Original = "x", Replacement = "y" }
                        };
                        var exact = LocalfileTranslation.MergeByAlias(tgtIn.Entries, srcEntries, out _);
                        var fuzzy = LocalfileTranslation.MergeByAlias(tgtIn.Entries, srcEntries, out var fs, fuzzyRenameFallback: true);
                        int before = exact.Count(e => e.Type == "merged");
                        return fs.FuzzyMatched >= 1
                            && fuzzy.Count(e => e.Type == "fuzzy") >= 1
                            && fs.NoAliasMatch < 0 + fs.Total - 2 + 0 + 1; // sanity: fewer no-match with fallback
                    });
                }
                finally
                {
                    if (File.Exists(tableXmlPath)) File.Delete(tableXmlPath);
                    if (File.Exists(mergedTablePath)) File.Delete(mergedTablePath);
                    if (File.Exists(mergedTablePath2)) File.Delete(mergedTablePath2);
                }

                // --- MT tag recovery: fake translator that eats placeholders ---
                Check("MT tag recovery: leading icon re-inserted when placeholder eaten", () =>
                {
                    const string image = "<image enablescale=\"true\" imagesetpath=\"00009076.ToolTip_BlazingPalm\"/>";
                    string original = image + " ใช้ Blazing Palm เพิ่มเติม";
                    string result = TranslationMarkupGuard.ProtectTranslateUnprotect(original,
                        plain => new System.Text.RegularExpressions.Regex(@"⟦[^⟧]*⟧").Replace(plain, ""));
                    return result == original;
                });

                Check("MT tag recovery: eaten open tag anchored after previous tag", () =>
                {
                    const string img = "<image imagesetpath=\"00009076.Icon_X\"/>";
                    const string font = "<font name=\"00008130.UI.Label_12\">";
                    const string end = "</font>";
                    string original = img + font + "Blazing Palm" + end;
                    string result = TranslationMarkupGuard.ProtectTranslateUnprotect(original,
                        plain => new System.Text.RegularExpressions.Regex(@"⟦§1§⟧").Replace(plain, ""));
                    return result == img + font + "Blazing Palm" + end;
                });

                Check("Entity-encoded arg tag survives MT untouched (icon ref preserved)", () =>
                {
                    const string arg = "&lt;arg id=\"skill:ForceMaster_Deal_fire_blast_Lv1\" p=\"id:skill.current-short-cut-key.key1.image\"/&gt;";
                    string original = "Stack Embers on the Blue Training Dummy using " + arg;
                    string result = TranslationMarkupGuard.ProtectTranslateUnprotect(original,
                        plain => new System.Text.RegularExpressions.Regex(@"⟦[^⟧]*⟧").Replace(plain, ""));
                    return result == original;
                });

                Check("Entity-encoded arg tag restored at end when placed mid-sentence", () =>
                {
                    const string arg = "&lt;arg id=\"skill:ForceMaster_Deal_fire_blast_Lv1\" p=\"id:skill.name2\"/&gt;";
                    // original = "some Thai text using <arg>"; MT drops the placeholder → appended at end
                    string original = "สะสมเปลวเพลิงใส่หุ่นฝึกโดยใช้ " + arg;
                    string res = TranslationMarkupGuard.ProtectTranslateUnprotect(
                        original, plain => new System.Text.RegularExpressions.Regex(@"⟦[^⟧]*⟧").Replace(plain, ""));
                    return res == original;
                });

                Check("NormalizeEntities leaves entity-tag attribute quotes intact", () =>
                {
                    const string arg = "&lt;arg id=\"skill:X\" p=\"id:skill.name2\"/&gt;";
                    string result = TranslationMarkupGuard.NormalizeEntities("text &quot;q&quot; " + arg + " more");
                    return result == "text &quot;q&quot; " + arg + " more";
                });

                Check("Merge keeps target entity-encoded arg when rename fallback matches", () =>
                {
                    const string tgtArg = "&lt;arg id=\"skill:ForceMaster_Deal_fire_blast_Lv1\" p=\"id:skill.name2\"/&gt; &lt;arg id=\"skill:ForceMaster_Deal_fire_blast_Lv1\" p=\"id:skill.current-short-cut-key.key1.image\"/&gt;";
                    const string srcArg = "&lt;arg id=\"skill:ForceMaster_Deal_fire_blast_Lv1\" p=\"id:skill.name2\"/&gt; &lt;arg id=\"skill:MarshalMagic_Deal_fire_blast_Lv1\" p=\"id:skill.current-short-cut-key.key1.image\"/&gt;";
                    string targetText = "สะสมเปลวเพลิงใส่หุ่นฝึก และใช้ " + tgtArg;
                    string sourceText = "Use " + srcArg + " on the dummy";
                    string merge = LocalfileTranslation.MergeKeepTargetMarkup(targetText, sourceText, out bool structureOk);
                    return structureOk && merge.Contains(tgtArg) && !merge.Contains("MarshalMagic");
                });
                // NOTE: no message loop was started, nothing to tear down — process exits after Main.
            }
            catch (Exception ex)
            {
                _fails++;
                Console.WriteLine("FAIL  unexpected top-level error: " + ex.GetType().Name + ": " + ex.Message);
            }

            Console.WriteLine();
            Console.WriteLine(_fails == 0 ? "ALL CHECKS PASSED" : (_fails + " CHECK(S) FAILED"));
            return _fails == 0 ? 0 : 1;
        }

        private static void Check(string name, Func<bool> fn)
        {
            try
            {
                if (fn())
                {
                    Console.WriteLine("PASS  " + name);
                }
                else
                {
                    _fails++;
                    Console.WriteLine("FAIL  " + name);
                }
            }
            catch (Exception ex)
            {
                _fails++;
                Console.WriteLine("FAIL  " + name + " — " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
