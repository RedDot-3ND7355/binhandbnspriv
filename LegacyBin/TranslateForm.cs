using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LegacyBin
{
    /// <summary>
    /// localfile translation UI (export / merge / apply / auto-translate) — BnsDatTool-compatible Translation.xml.
    /// </summary>
    public sealed class TranslateForm : Form
    {
        private readonly BinSession _session;
        private readonly Action _onApplied;

        private TextBox _txtSourceXml;
        private TextBox _txtTargetXml;
        private TextBox _txtLog;
        private NumericUpDown _numTable;
        private CheckBox _chkAutoTable;
        private CheckBox _chkResplit;
        private CheckBox _chkOnlyUntranslated;
        private CheckBox _chkDetectFromPairs;
        private NumericUpDown _numWorkers;
        private Label _lblStatus;
        private Label _lblAutoStatus;
        private ProgressBar _progress;
        private Button _btnAutoTranslate;
        private Button _btnCancelAuto;
        private CancellationTokenSource _autoCts;

        public TranslateForm(BinSession session, Action onApplied = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _onApplied = onApplied;
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "Localfile Translate";
            Width = 820;
            Height = 640;
            MinimumSize = new System.Drawing.Size(720, 520);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 8,
                Padding = new Padding(10)
            };
            // status, source, target, options, buttons (2 rows), progress, auto-status, log
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));  // options + workers can wrap
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));  // two button rows
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));  // progress bar only
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _lblStatus = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Text = _session.IsOpen
                    ? ("Open bin: " + Path.GetFileName(_session.FilePath) + (_session.Is64Bit ? " [64-bit]" : " [32-bit]"))
                    : "No bin open in editor — Apply requires an open localfile."
            };
            root.Controls.Add(_lblStatus, 0, 0);

            // Source XML row
            var rowSrc = MakePathRow("Source / merge-from XML:", out _txtSourceXml, out var btnSrc);
            btnSrc.Click += (s, e) => BrowseXml(_txtSourceXml, save: false);
            root.Controls.Add(rowSrc, 0, 1);

            // Target XML row
            var rowTgt = MakePathRow("Target XML (export/merge into):", out _txtTargetXml, out var btnTgt);
            btnTgt.Click += (s, e) => BrowseXml(_txtTargetXml, save: false);
            root.Controls.Add(rowTgt, 0, 2);

            // Options
            var opts = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            _chkAutoTable = new CheckBox { Text = "Auto-detect text table", Checked = true, AutoSize = true, Margin = new Padding(0, 6, 12, 0) };
            _numTable = new NumericUpDown { Minimum = 0, Maximum = 9999, Width = 70, Enabled = false };
            _chkAutoTable.CheckedChanged += (s, e) => _numTable.Enabled = !_chkAutoTable.Checked;
            _chkResplit = new CheckBox
            {
                Text = "Split oversized blocks after apply",
                Checked = true,
                AutoSize = true,
                Margin = new Padding(12, 6, 12, 0)
            };
            _chkOnlyUntranslated = new CheckBox
            {
                Text = "Fill gaps only (orig = repl)",
                Checked = true,
                AutoSize = true,
                Margin = new Padding(12, 6, 12, 0)
            };
            _chkDetectFromPairs = new CheckBox
            {
                Text = "Detect langs from merged pairs",
                Checked = true,
                AutoSize = true,
                Margin = new Padding(12, 6, 12, 0)
            };
            _numWorkers = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 16,
                Value = 6,
                Width = 50,
                Margin = new Padding(0, 4, 0, 0)
            };
            opts.Controls.Add(_chkAutoTable);
            opts.Controls.Add(new Label { Text = "Table index:", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
            opts.Controls.Add(_numTable);
            opts.Controls.Add(_chkResplit);
            opts.Controls.Add(_chkOnlyUntranslated);
            opts.Controls.Add(_chkDetectFromPairs);
            opts.Controls.Add(new Label { Text = "Workers:", AutoSize = true, Margin = new Padding(12, 8, 4, 0) });
            opts.Controls.Add(_numWorkers);
            root.Controls.Add(opts, 0, 3);

            // Buttons (own row tall enough for wrap — progress bar lives in the next row)
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            var btnExport = new Button { Text = "Export XML from open bin", AutoSize = true, Margin = new Padding(0, 0, 8, 4) };
            var btnMerge = new Button { Text = "Merge XMLs by alias", AutoSize = true, Margin = new Padding(0, 0, 8, 4) };
            _btnAutoTranslate = new Button
            {
                Text = "Fill gaps (auto-translate)",
                AutoSize = true,
                Margin = new Padding(0, 0, 8, 4)
            };
            _btnCancelAuto = new Button
            {
                Text = "Cancel auto",
                AutoSize = true,
                Enabled = false,
                Margin = new Padding(0, 0, 8, 4)
            };
            var btnApply = new Button { Text = "Apply XML → open bin", AutoSize = true, Margin = new Padding(0, 0, 8, 4) };
            var btnClose = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel, Margin = new Padding(0, 0, 8, 4) };
            btnExport.Click += async (s, e) => await ExportAsync();
            btnMerge.Click += async (s, e) => await MergeAsync();
            _btnAutoTranslate.Click += async (s, e) => await AutoTranslateAsync();
            _btnCancelAuto.Click += (s, e) =>
            {
                try { _autoCts?.Cancel(); } catch { /* ignore */ }
            };
            btnApply.Click += async (s, e) => await ApplyAsync();
            buttons.Controls.Add(btnExport);
            buttons.Controls.Add(btnMerge);
            buttons.Controls.Add(_btnAutoTranslate);
            buttons.Controls.Add(_btnCancelAuto);
            buttons.Controls.Add(btnApply);
            buttons.Controls.Add(btnClose);
            root.Controls.Add(buttons, 0, 4);

            _progress = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Height = 18,
                Style = ProgressBarStyle.Continuous,
                Margin = new Padding(0, 2, 0, 2)
            };
            root.Controls.Add(_progress, 0, 5);

            _lblAutoStatus = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Text = "Auto-translate: idle (detects source lang from first untranslated line → English)."
            };
            root.Controls.Add(_lblAutoStatus, 0, 6);

            _txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new System.Drawing.Font("Consolas", 9f)
            };
            root.Controls.Add(_txtLog, 0, 7);

            Controls.Add(root);
            CancelButton = btnClose;
            UiTheme.Apply(this);

            Log("Workflow (BnsDatTool-compatible):");
            Log("1) Open localfile / localfile64 in the editor.");
            Log("2) Export Target Translation XML from this bin (aliases + original text).");
            Log("3) Set Source XML = other language region; Merge XMLs by alias.");
            Log("4) Fill gaps — detects original/replacement langs from rows merge already filled,");
            Log("   then auto-translates only leftover rows (alias missing → orig still == repl).");
            Log("5) Apply the Translation XML into the open bin, then Save.");
            Log("");
            _lblAutoStatus.Text = "Fill gaps: idle — after merge, learns language pair from translated rows, fills the rest.";
            if (_session.IsOpen)
            {
                try
                {
                    int idx = LocalfileTranslation.FindTextTableIndex(_session.Content);
                    if (idx >= 0)
                    {
                        var L = _session.Content.Lists[idx];
                        Log("Detected text table: index=" + idx + " id=" + L.ID + " size=" + L.Size);
                        _numTable.Value = idx;
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

        private static Panel MakePathRow(string label, out TextBox textBox, out Button browse)
        {
            var p = new Panel { Dock = DockStyle.Fill, Height = 32 };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            var l = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            textBox = new TextBox { Dock = DockStyle.Fill };
            browse = new Button { Text = "Browse…", Dock = DockStyle.Fill };
            layout.Controls.Add(l, 0, 0);
            layout.Controls.Add(textBox, 1, 0);
            layout.Controls.Add(browse, 2, 0);
            p.Controls.Add(layout);
            return p;
        }

        private void BrowseXml(TextBox target, bool save)
        {
            if (save)
            {
                using (var dlg = new SaveFileDialog())
                {
                    dlg.Filter = "Translation XML (*.xml)|*.xml|All files (*.*)|*.*";
                    dlg.FileName = _session.Is64Bit ? "Translation64.xml" : "Translation.xml";
                    DialogPaths.Apply(dlg);
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        DialogPaths.RememberFile(dlg.FileName);
                        target.Text = dlg.FileName;
                    }
                }
            }
            else
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Filter = "Translation XML (*.xml)|*.xml|All files (*.*)|*.*";
                    DialogPaths.Apply(dlg);
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        DialogPaths.RememberFile(dlg.FileName);
                        target.Text = dlg.FileName;
                    }
                }
            }
        }

        private void Log(string msg)
        {
            _txtLog.AppendText(msg + Environment.NewLine);
        }

        private int ResolveTableIndex()
        {
            if (_chkAutoTable.Checked)
            {
                return LocalfileTranslation.FindTextTableIndex(_session.Content);
            }
            return (int)_numTable.Value;
        }

        private string ResolveInputXmlPath()
        {
            string path = _txtTargetXml.Text.Trim();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                path = _txtSourceXml.Text.Trim();
            }
            return path;
        }

        /// <summary>
        /// WinForms ProgressBar throws if Maximum is set below Value; set safely for stepped fills.
        /// </summary>
        private void SetProgressSteps(int maximum, int value)
        {
            if (maximum < 1)
            {
                maximum = 1;
            }
            if (value < 0)
            {
                value = 0;
            }
            if (value > maximum)
            {
                value = maximum;
            }
            if (_progress.Maximum != maximum)
            {
                // Drop value first when shrinking max to avoid ArgumentOutOfRangeException.
                if (_progress.Value > maximum)
                {
                    _progress.Value = 0;
                }
                _progress.Maximum = maximum;
            }
            if (_progress.Value != value)
            {
                _progress.Value = value;
            }
        }

        private async Task ExportAsync()
        {
            if (!_session.IsOpen)
            {
                MessageBox.Show(this, "Open a localfile bin in the editor first.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "Translation XML (*.xml)|*.xml";
                dlg.FileName = _session.Is64Bit ? "Translation64.xml" : "Translation.xml";
                DialogPaths.Apply(dlg);
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                DialogPaths.RememberFile(dlg.FileName);
                string path = dlg.FileName;
                UseWaitCursor = true;
                try
                {
                    List<LocalfileTranslation.Entry> entries = null;
                    int tableIndex = ResolveTableIndex();
                    await Task.Run(() =>
                    {
                        entries = LocalfileTranslation.ExportFromContent(_session.Content, tableIndex);
                        LocalfileTranslation.SaveXml(path, entries);
                    });
                    _txtTargetXml.Text = path;
                    Log("Exported " + entries.Count + " entries → " + path + " (table " + tableIndex + ")");
                    MessageBox.Show(this, "Exported " + entries.Count + " text entries.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    Log("Export failed: " + ex.Message);
                    MessageBox.Show(this, ex.ToString(), "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    UseWaitCursor = false;
                }
            }
        }

        private async Task MergeAsync()
        {
            string source = _txtSourceXml.Text.Trim();
            string target = _txtTargetXml.Text.Trim();
            if (!File.Exists(source) || !File.Exists(target))
            {
                MessageBox.Show(this, "Select existing Source and Target Translation XML files.\n"
                    + "Source = language you want to pull from\nTarget = structure to keep (usually current client export).",
                    "Merge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "Translation XML (*.xml)|*.xml";
                dlg.FileName = Path.GetFileNameWithoutExtension(target) + "_merged.xml";
                DialogPaths.Apply(dlg);
                string targetDir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir))
                {
                    dlg.InitialDirectory = targetDir;
                }
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                DialogPaths.RememberFile(dlg.FileName);
                string outPath = dlg.FileName;
                UseWaitCursor = true;
                try
                {
                    int count = 0;
                    int filled = 0;
                    int gaps = 0;
                    int empty = 0;
                    int mergedHits = 0;
                    int structMismatch = 0;
                    int noAlias = 0;
                    await Task.Run(() =>
                    {
                        var src = LocalfileTranslation.LoadXml(source);
                        var tgt = LocalfileTranslation.LoadXml(target);
                        var merged = LocalfileTranslation.MergeByAlias(tgt, src, out var st);
                        LocalfileTranslation.SaveXml(outPath, merged);
                        count = merged.Count;
                        mergedHits = st.Merged;
                        structMismatch = st.StructureMismatched;
                        noAlias = st.NoAliasMatch;
                        AutoTranslateService.CountMergeGaps(merged, out filled, out gaps, out empty);
                    });
                    _txtTargetXml.Text = outPath;
                    Log("Merged " + count + " entries → " + outPath);
                    Log("  merged by alias (target markup kept): " + mergedHits);
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
                        + "\n\nMerged by alias: " + mergedHits
                        + (structMismatch > 0
                            ? "\nStructure mismatched (target kept, icons preserved): " + structMismatch
                            : "")
                        + "\nNo alias match: " + noAlias
                        + "\n\nTranslated by alias: " + filled
                        + "\nGaps (alias missing / still orig=repl): " + gaps
                        + (gaps > 0
                            ? "\n\nNext: click “Fill gaps (auto-translate)” — it will detect the language pair from the translated rows and only translate the gaps."
                            : "\n\nNo gaps left to fill.");
                    MessageBox.Show(this, msg, "Merge", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    Log("Merge failed: " + ex.Message);
                    MessageBox.Show(this, ex.ToString(), "Merge failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    UseWaitCursor = false;
                }
            }
        }

        private async Task AutoTranslateAsync()
        {
            string input = ResolveInputXmlPath();
            if (!File.Exists(input))
            {
                MessageBox.Show(this,
                    "Select a Translation XML in Target (preferred) or Source.\n"
                    + "Export from the open bin first if you do not have one yet.",
                    "Auto-translate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string dir = Path.GetDirectoryName(input) ?? ".";
            string name = Path.GetFileNameWithoutExtension(input);
            string defaultOut = Path.Combine(dir, name + "_filled.xml");

            string outPath;
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "Translation XML (*.xml)|*.xml";
                dlg.FileName = Path.GetFileName(defaultOut);
                dlg.InitialDirectory = dir;
                DialogPaths.Apply(dlg);
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                DialogPaths.RememberFile(dlg.FileName);
                outPath = dlg.FileName;
            }

            if (string.Equals(Path.GetFullPath(input), Path.GetFullPath(outPath), StringComparison.OrdinalIgnoreCase))
            {
                if (MessageBox.Show(this,
                        "Output path is the same as input — the XML will be overwritten after fill.\nContinue?",
                        "Overwrite input?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }
            }

            _autoCts = new CancellationTokenSource();
            _btnAutoTranslate.Enabled = false;
            _btnCancelAuto.Enabled = true;
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Minimum = 0;
            _progress.Maximum = 100;
            _progress.Value = 0;

            var opts = new AutoTranslateService.Options
            {
                InputXmlPath = input,
                OutputXmlPath = outPath,
                SourceLang = "auto",
                TargetLang = "auto",
                FallbackTargetLang = "en",
                Mode = _chkDetectFromPairs.Checked
                    ? AutoTranslateService.DetectMode.FromTranslatedPairs
                    : AutoTranslateService.DetectMode.FirstUntranslatedToTarget,
                OnlyUntranslated = _chkOnlyUntranslated.Checked,
                // Cache path chosen after language pair is detected (…gtcache.{sl}-{tl})
                CachePath = null,
                Concurrency = (int)_numWorkers.Value,
                // Per-worker pause; lower with more workers. Raise if you see many 429s.
                DelayMs = (int)_numWorkers.Value >= 8 ? 25 : 15,
                TranslatedType = "auto"
            };

            Log("Fill gaps start: " + input);
            Log("  → " + outPath);
            Log("  mode=" + opts.Mode + " onlyGaps=" + opts.OnlyUntranslated
                + " workers=" + opts.Concurrency + " delayMs=" + opts.DelayMs);

            var progress = new Progress<AutoTranslateService.Progress>(p =>
            {
                if (IsDisposed)
                {
                    return;
                }

                // Always continuous / stepped — never marquee (spinning).
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Minimum = 0;

                if (p.Phase == "translate" && p.StepTotal > 0)
                {
                    // One tick per unique string for a steady fill.
                    int max = Math.Max(1, p.StepTotal);
                    int step = Math.Max(0, Math.Min(p.StepCurrent, max));
                    SetProgressSteps(max, step);
                }
                else
                {
                    // Load / detect / save use overall percent on a 0–100 scale.
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
                _lblAutoStatus.Text = status;

                // Log less often than the bar steps (bar updates every unique string).
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
                _txtTargetXml.Text = report.OutputPath;
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Maximum = 100;
                _progress.Value = 100;
                _lblAutoStatus.Text = "Done: " + report.SourceLang + "→" + report.TargetLang
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
                MessageBox.Show(this, sb.ToString() + "\nYou can Apply this XML to the open bin next.",
                    "Fill gaps complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                _lblAutoStatus.Text = "Fill gaps cancelled (cache kept — re-run to resume).";
                Log("Fill gaps cancelled. Re-run to resume from language-pair cache.");
                MessageBox.Show(this,
                    "Cancelled. Progress is stored in the .gtcache.{lang}-{lang} file next to the input XML.\nRe-run Fill gaps to resume.",
                    "Cancelled", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log("Fill gaps failed: " + ex.Message);
                _lblAutoStatus.Text = "Failed: " + ex.Message;
                MessageBox.Show(this, ex.ToString(), "Fill gaps failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnAutoTranslate.Enabled = true;
                _btnCancelAuto.Enabled = false;
                if (_autoCts != null)
                {
                    _autoCts.Dispose();
                    _autoCts = null;
                }
                _progress.Style = ProgressBarStyle.Continuous;
            }
        }

        private async Task ApplyAsync()
        {
            if (!_session.IsOpen)
            {
                MessageBox.Show(this, "Open a localfile bin in the editor first.", "Apply", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string xmlPath = _txtTargetXml.Text.Trim();
            if (string.IsNullOrEmpty(xmlPath) || !File.Exists(xmlPath))
            {
                xmlPath = _txtSourceXml.Text.Trim();
            }
            if (!File.Exists(xmlPath))
            {
                MessageBox.Show(this, "Select a Translation XML to apply (Target preferred, else Source).",
                    "Apply", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show(this,
                    "Apply translations from:\n" + xmlPath + "\n\nto the open bin in memory?\n(You still need to Save the bin afterward.)",
                    "Apply translation", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            UseWaitCursor = true;
            try
            {
                LocalfileTranslation.ApplyResult result = null;
                int tableIndex = ResolveTableIndex();
                bool resplit = _chkResplit.Checked;
                await Task.Run(() =>
                {
                    var entries = LocalfileTranslation.LoadXml(xmlPath);
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
                MessageBox.Show(this, sb.ToString() + "\nRemember to File → Save the bin.", "Apply complete",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log("Apply failed: " + ex.Message);
                MessageBox.Show(this, ex.ToString(), "Apply failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { _autoCts?.Cancel(); } catch { /* ignore */ }
            base.OnFormClosing(e);
        }
    }
}
