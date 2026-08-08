using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace LegacyBin.Ava
{
    /// <summary>
    /// localfile translation UI (export / merge / apply / auto-translate) — BnsDatTool-compatible
    /// Translation.xml. Port of the WinForms TranslateForm to Avalonia.
    /// </summary>
    public partial class TranslateWindow : Window
    {
        private readonly BinSession _session;
        private readonly Action _onApplied;
        private CancellationTokenSource _autoCts;

        public TranslateWindow(BinSession session, Action onApplied = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _onApplied = onApplied;
            InitializeComponent();
            ChkAutoTable.PropertyChanged += (s, e) =>
            {
                if (e.Property.Name == nameof(CheckBox.IsChecked))
                {
                    NumTable.IsEnabled = ChkAutoTable.IsChecked != true;
                }
            };

            LblStatus.Text = _session.IsOpen
                ? "Open bin: " + Path.GetFileName(_session.FilePath) + (_session.Is64Bit ? " [64-bit]" : " [32-bit]")
                : "No bin open in editor — Apply requires an open localfile.";

            Log("Workflow (BnsDatTool-compatible):");
            Log("1) Open localfile / localfile64 in the editor.");
            Log("2) Export Target Translation XML from this bin (aliases + original text).");
            Log("3) Set Source XML = other language region; Merge XMLs by alias.");
            Log("4) Fill gaps — detects original/replacement langs from rows merge already filled,");
            Log("   then auto-translates only leftover rows (alias missing → orig still == repl).");
            Log("5) Apply the Translation XML into the open bin, then Save.");
            Log("");
            LblAutoStatus.Text = "Fill gaps: idle — after merge, learns language pair from translated rows, fills the rest.";

            if (_session.IsOpen)
            {
                try
                {
                    int idx = LocalfileTranslation.FindTextTableIndex(_session.Content);
                    if (idx >= 0)
                    {
                        var L = _session.Content.Lists[idx];
                        Log("Detected text table: index=" + idx + " id=" + L.ID + " size=" + L.Size);
                        NumTable.Value = idx;
                    }
                    else
                    {
                        Log("Warning: could not auto-detect text table.");
                    }
                }
                catch (Exception ex)
                {
                    Log("Detect error: " + ex.Message);
                }
            }
        }

        private void Log(string msg)
        {
            Dispatcher.UIThread.Post(() =>
            {
                TxtLog.Text += msg + Environment.NewLine;
                TxtLog.CaretIndex = TxtLog.Text.Length;
            });
        }

        private int ResolveTableIndex()
        {
            if (ChkAutoTable.IsChecked == true)
            {
                return LocalfileTranslation.FindTextTableIndex(_session.Content);
            }
            return (int)(NumTable.Value ?? 0);
        }

        private string ResolveInputXmlPath()
        {
            string path = TxtTargetXml.Text.Trim();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                path = TxtSourceXml.Text.Trim();
            }
            return path;
        }

        private void SetProgressSteps(int maximum, int value)
        {
            if (maximum < 1) maximum = 1;
            if (value < 0) value = 0;
            if (value > maximum) value = maximum;
            if (Progress.Maximum != maximum)
            {
                if (Progress.Value > maximum)
                {
                    Progress.Value = 0;
                }
                Progress.Maximum = maximum;
            }
            if (Progress.Value != value)
            {
                Progress.Value = value;
            }
        }

        private async void OnBrowseSourceClicked(object sender, RoutedEventArgs e)
        {
            string path = await AvaDialogs.PickOpenFileAsync(this, "Select translation XML", "Translation XML", new[] { "*.xml" });
            if (!string.IsNullOrEmpty(path))
            {
                TxtSourceXml.Text = path;
            }
        }

        private async void OnBrowseTargetClicked(object sender, RoutedEventArgs e)
        {
            string path = await AvaDialogs.PickOpenFileAsync(this, "Select translation XML", "Translation XML", new[] { "*.xml" });
            if (!string.IsNullOrEmpty(path))
            {
                TxtTargetXml.Text = path;
            }
        }

        private async void OnExportClicked(object sender, RoutedEventArgs e)
        {
            if (!_session.IsOpen)
            {
                await AvaMsg.Warn(this, "Open a localfile bin in the editor first.", "Export");
                return;
            }
            string path = await AvaDialogs.PickSaveFileAsync(this, "Export translation XML", "Translation XML",
                new[] { "*.xml" }, _session.Is64Bit ? "Translation64.xml" : "Translation.xml");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            try
            {
                List<LocalfileTranslation.Entry> entries = null;
                int tableIndex = ResolveTableIndex();
                await Task.Run(() =>
                {
                    entries = LocalfileTranslation.ExportFromContent(_session.Content, tableIndex);
                    LocalfileTranslation.SaveXml(path, entries);
                });
                TxtTargetXml.Text = path;
                Log("Exported " + entries.Count + " entries → " + path + " (table " + tableIndex + ")");
                await AvaMsg.Show(this, "Exported " + entries.Count + " text entries.", "Export");
            }
            catch (Exception ex)
            {
                Log("Export failed: " + ex.Message);
                await AvaMsg.Error(this, ex.ToString(), "Export failed");
            }
        }

        private async void OnMergeClicked(object sender, RoutedEventArgs e)
        {
            string source = TxtSourceXml.Text.Trim();
            string target = TxtTargetXml.Text.Trim();
            if (!File.Exists(source) || !File.Exists(target))
            {
                await AvaMsg.Warn(this,
                    "Select existing Source and Target Translation XML files.\n"
                    + "Source = language you want to pull from\nTarget = structure to keep (usually current client export).",
                    "Merge");
                return;
            }
            string outPath = await AvaDialogs.PickSaveFileAsync(this, "Save merged XML", "Translation XML",
                new[] { "*.xml" }, Path.GetFileNameWithoutExtension(target) + "_merged.xml");
            if (string.IsNullOrEmpty(outPath))
            {
                return;
            }
            try
            {
                int count = 0, mergedHits = 0, structMismatch = 0, noAlias = 0, filled = 0, gaps = 0, empty = 0;
                int fuzzyMatched = 0;
                string fuzzyRules = "";
                string srcFmt = "", tgtFmt = "";
                int tgtTableId = -1;
                bool fuzzy = ChkFuzzyRenames.IsChecked == true;
                await Task.Run(() =>
                {
                    var srcIn = LocalfileTranslation.LoadMergeInput(source);
                    var tgtIn = LocalfileTranslation.LoadMergeInput(target);
                    var merged = LocalfileTranslation.MergeByAlias(tgtIn.Entries, srcIn.Entries, out var st, fuzzy);
                    LocalfileTranslation.SaveMergeOutput(outPath, tgtIn, merged);
                    count = merged.Count;
                    mergedHits = st.Merged;
                    structMismatch = st.StructureMismatched;
                    noAlias = st.NoAliasMatch;
                    fuzzyMatched = st.FuzzyMatched;
                    fuzzyRules = st.FuzzyRuleSummary ?? "";
                    srcFmt = srcIn.Kind == "table" ? "table id=" + srcIn.TableId : "translation XML";
                    tgtFmt = tgtIn.Kind == "table" ? "table id=" + tgtIn.TableId : "translation XML";
                    tgtTableId = tgtIn.TableId;
                    AutoTranslateService.CountMergeGaps(merged, out filled, out gaps, out empty);
                });
                TxtTargetXml.Text = outPath;
                Log("Merged " + count + " entries → " + outPath);
                Log("  source: " + srcFmt + "   target: " + tgtFmt
                    + (tgtTableId >= 0 ? "   (output keeps target table id=" + tgtTableId + ")" : ""));
                Log("  merged by alias (target markup kept): " + mergedHits);
                if (fuzzy)
                {
                    Log("  merged via 1-token alias rename: " + fuzzyMatched);
                    if (!string.IsNullOrEmpty(fuzzyRules))
                    {
                        Log("  rename rules encountered:");
                        foreach (var line in fuzzyRules.Split('\n'))
                        {
                            Log("    " + line.Trim());
                        }
                    }
                }
                if (structMismatch > 0)
                {
                    Log("  structure mismatched (kept target original, icon IDs preserved): " + structMismatch);
                }
                if (noAlias > 0)
                {
                    Log("  no alias match (target unchanged): " + noAlias);
                }
                Log("  already translated (orig ≠ repl): " + filled);
                Log("  gaps still untranslated (orig = repl): " + gaps);
                if (empty > 0)
                {
                    Log("  empty original: " + empty);
                }
                string msg = "Merge complete.\n" + outPath
                    + "\n\nSource: " + srcFmt
                    + "\nTarget: " + tgtFmt
                    + (tgtTableId >= 0
                        ? "\nOutput keeps the target table structure and ids (datafile_"
                            + tgtTableId.ToString("000") + ")."
                        : "")
                    + "\n\nMerged by alias: " + mergedHits
                    + (fuzzy ? "\nMerged via 1-token alias rename: " + fuzzyMatched : "")
                    + (structMismatch > 0 ? "\nStructure mismatched (target kept, icons preserved): " + structMismatch : "")
                    + "\nNo alias match: " + noAlias
                    + "\n\nTranslated by alias: " + filled
                    + "\nGaps (alias missing / still orig=repl): " + gaps
                    + (gaps > 0
                        ? (tgtTableId >= 0
                            ? "\n\nNext: replace datafile_" + tgtTableId.ToString("000") + ".xml in the unpack folder and Repack,"
                                + " or apply a Translation XML into the open bin."
                            : "\n\nNext: click “Fill gaps (auto-translate)” — it will detect the language pair from the translated rows and only translate the gaps.")
                        : "\n\nNo gaps left to fill.");
                await AvaMsg.Show(this, msg, "Merge");
            }
            catch (Exception ex)
            {
                Log("Merge failed: " + ex.Message);
                await AvaMsg.Error(this, ex.ToString(), "Merge failed");
            }
        }

        private async void OnAutoTranslateClicked(object sender, RoutedEventArgs e)
        {
            string input = ResolveInputXmlPath();
            if (!File.Exists(input))
            {
                await AvaMsg.Warn(this,
                    "Select a Translation XML in Target (preferred) or Source.\n"
                    + "Export from the open bin first if you do not have one yet.",
                    "Auto-translate");
                return;
            }

            string dir = Path.GetDirectoryName(input) ?? ".";
            string name = Path.GetFileNameWithoutExtension(input);
            string outPath = await AvaDialogs.PickSaveFileAsync(this, "Save filled XML", "Translation XML",
                new[] { "*.xml" }, name + "_filled.xml");
            if (string.IsNullOrEmpty(outPath))
            {
                return;
            }

            if (string.Equals(Path.GetFullPath(input), Path.GetFullPath(outPath), StringComparison.OrdinalIgnoreCase))
            {
                if (!await AvaMsg.Ask(this,
                        "Output path is the same as input — the XML will be overwritten after fill.\nContinue?",
                        "Overwrite input?"))
                {
                    return;
                }
            }

            _autoCts = new CancellationTokenSource();
            BtnAutoTranslate.IsEnabled = false;
            BtnCancelAuto.IsEnabled = true;
            Progress.Minimum = 0;
            Progress.Maximum = 100;
            Progress.Value = 0;

            var opts = new AutoTranslateService.Options
            {
                InputXmlPath = input,
                OutputXmlPath = outPath,
                SourceLang = "auto",
                TargetLang = "auto",
                FallbackTargetLang = "en",
                Mode = ChkDetectFromPairs.IsChecked == true
                    ? AutoTranslateService.DetectMode.FromTranslatedPairs
                    : AutoTranslateService.DetectMode.FirstUntranslatedToTarget,
                OnlyUntranslated = ChkOnlyUntranslated.IsChecked == true,
                // Cache path chosen after language pair is detected (…gtcache.{sl}-{tl})
                CachePath = null,
                Concurrency = (int)(NumWorkers.Value ?? 6),
                // Per-worker pause; lower with more workers. Raise if you see many 429s.
                DelayMs = (int)(NumWorkers.Value ?? 6) >= 8 ? 25 : 15,
                TranslatedType = "auto"
            };

            Log("Fill gaps start: " + input);
            Log("  → " + outPath);
            Log("  mode=" + opts.Mode + " onlyGaps=" + opts.OnlyUntranslated
                + " workers=" + opts.Concurrency + " delayMs=" + opts.DelayMs);

            var progress = new Progress<AutoTranslateService.Progress>(p =>
            {
                if (p.Phase == "translate" && p.StepTotal > 0)
                {
                    int max = Math.Max(1, p.StepTotal);
                    int step = Math.Max(0, Math.Min(p.StepCurrent, max));
                    SetProgressSteps(max, step);
                }
                else
                {
                    int pct = Math.Max(0, Math.Min(100, p.Percent));
                    SetProgressSteps(100, pct);
                }

                string status = p.Phase + ": " + (p.Message ?? "");
                if (p.Phase == "translate" && p.StepTotal > 0)
                {
                    status = p.StepCurrent + " / " + p.StepTotal + "  (" + p.Percent + "%)  " + status;
                }
                else if (p.Percent > 0)
                {
                    status = p.Percent + "%  " + status;
                }
                if (!string.IsNullOrEmpty(p.SourceLang))
                {
                    status = "[" + p.SourceLang + "→" + p.TargetLang + "] " + status;
                }
                LblAutoStatus.Text = status;

                if (!string.IsNullOrEmpty(p.Message)
                    && (p.Phase == "load" || p.Phase == "detect" || p.Phase == "save"
                        || (p.Phase == "translate" && (p.UniqueDone == 1 || p.UniqueDone % 50 == 0
                            || p.UniqueDone == p.UniqueToTranslate))))
                {
                    Log(p.Message + (string.IsNullOrEmpty(p.LastSample) ? "" : " | " + p.LastSample));
                }
            });

            try
            {
                var report = await AutoTranslateService.RunAsync(opts, progress, _autoCts.Token);
                TxtTargetXml.Text = report.OutputPath;
                Progress.Maximum = 100;
                Progress.Value = 100;
                LblAutoStatus.Text = "Done: " + report.SourceLang + "→" + report.TargetLang
                    + " unique=" + report.UniqueTranslated
                    + " applied=" + report.Applied
                    + " cacheHits=" + report.CacheHits;

                var sb = new StringBuilder();
                sb.AppendLine("Fill gaps complete.");
                sb.AppendLine("Language pair (locked): " + report.SourceLang + " → " + report.TargetLang);
                if (!string.IsNullOrEmpty(report.DetectNote))
                {
                    sb.AppendLine(report.DetectNote);
                }
                sb.AppendLine("Already-translated rows used as samples: " + report.TranslatedPairSamples);
                sb.AppendLine("Total entries: " + report.TotalEntries);
                sb.AppendLine("Unique gaps fetched: " + report.UniqueTranslated);
                sb.AppendLine("Entries updated: " + report.Applied);
                sb.AppendLine("Skipped (already translated): " + report.SkippedAlready);
                sb.AppendLine("Skipped (empty): " + report.SkippedEmpty);
                sb.AppendLine("Cache hits: " + report.CacheHits);
                sb.AppendLine("Output: " + report.OutputPath);
                sb.AppendLine("Cache: " + report.CachePath);
                Log(sb.ToString());
                await AvaMsg.Show(this, sb.ToString() + "\nYou can Apply this XML to the open bin next.",
                    "Fill gaps complete");
            }
            catch (OperationCanceledException)
            {
                LblAutoStatus.Text = "Fill gaps cancelled (cache kept — re-run to resume).";
                Log("Fill gaps cancelled. Re-run to resume from language-pair cache.");
                await AvaMsg.Show(this,
                    "Cancelled. Progress is stored in the .gtcache.{lang}-{lang} file next to the input XML.\nRe-run Fill gaps to resume.",
                    "Cancelled");
            }
            catch (Exception ex)
            {
                Log("Fill gaps failed: " + ex.Message);
                LblAutoStatus.Text = "Failed: " + ex.Message;
                await AvaMsg.Error(this, ex.ToString(), "Fill gaps failed");
            }
            finally
            {
                BtnAutoTranslate.IsEnabled = true;
                BtnCancelAuto.IsEnabled = false;
                if (_autoCts != null)
                {
                    _autoCts.Dispose();
                    _autoCts = null;
                }
            }
        }

        private void OnCancelAutoClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                _autoCts?.Cancel();
            }
            catch
            {
                // ignore
            }
        }

        private async void OnApplyClicked(object sender, RoutedEventArgs e)
        {
            if (!_session.IsOpen)
            {
                await AvaMsg.Warn(this, "Open a localfile bin in the editor first.", "Apply");
                return;
            }
            string xmlPath = TxtTargetXml.Text.Trim();
            if (string.IsNullOrEmpty(xmlPath) || !File.Exists(xmlPath))
            {
                xmlPath = TxtSourceXml.Text.Trim();
            }
            if (!File.Exists(xmlPath))
            {
                await AvaMsg.Warn(this, "Select a Translation XML to apply (Target preferred, else Source).", "Apply");
                return;
            }
            if (!await AvaMsg.Ask(this,
                    "Apply translations from:\n" + xmlPath + "\n\nto the open bin in memory?\n(You still need to Save the bin afterward.)",
                    "Apply translation"))
            {
                return;
            }

            try
            {
                LocalfileTranslation.ApplyResult result = null;
                int tableIndex = ResolveTableIndex();
                bool resplit = ChkResplit.IsChecked == true;
                await Task.Run(() =>
                {
                    // Auto-detect: BnsDatTool Translation.xml (<table>) or unpacked datafile_XXX.xml (<list>).
                    var entries = LocalfileTranslation.LoadEntriesAnyFormat(xmlPath);
                    result = LocalfileTranslation.Apply(_session.Content, entries, tableIndex, resplit);
                });
                _session.MarkDirty();
                _onApplied?.Invoke();
                var sb = new StringBuilder();
                sb.AppendLine(result.TextTableSummary);
                sb.AppendLine("Records scanned: " + result.RecordsScanned);
                sb.AppendLine("Applied by alias: " + result.AppliedByAlias);
                sb.AppendLine("Applied by original text: " + result.AppliedByOriginal);
                sb.AppendLine("Unchanged: " + result.Unchanged);
                sb.AppendLine("Skipped: " + result.Skipped);
                sb.AppendLine("Blocks split: " + result.BlocksSplit);
                Log(sb.ToString());
                await AvaMsg.Show(this, sb.ToString() + "\nRemember to Save the bin (re-pack or write).", "Apply complete");
            }
            catch (Exception ex)
            {
                Log("Apply failed: " + ex.Message);
                await AvaMsg.Error(this, ex.ToString(), "Apply failed");
            }
        }

        private void OnCloseClicked(object sender, RoutedEventArgs e)
        {
            try
            {
                _autoCts?.Cancel();
            }
            catch
            {
                // ignore
            }
            Close();
        }
    }
}
