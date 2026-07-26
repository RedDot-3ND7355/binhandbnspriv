using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MaterialSkin.Controls;
using MaterialSkin;
using System.IO;
using System.Diagnostics;
using System.Xml;
using System.Xml.Serialization;

namespace LegacyBin
{
    public partial class Form1 : MaterialForm
    {
        private readonly MaterialSkinManager materialSkinManager;

        public static Form1 CurrentForm;

        private static readonly XmlSerializer BxmlListSerializer = new XmlSerializer(typeof(BXML_LIST));

        public readonly BDAT_CONTENT _content = new BDAT_CONTENT();
        public BNSDat m_bnsDat;
        public List<BXML_LIST> xml_list = new List<BXML_LIST>();
        public bool checkresult;

        /// <summary>Last resolved mode for the open bin (32 vs 64). Used for UI and repack.</summary>
        private bool _is64BitFile;

        public Form1()
        {
            CurrentForm = this;
            InitializeComponent();

            // Initialize MaterialSkinManager
            materialSkinManager = MaterialSkinManager.Instance;

            // Set this to false to disable backcolor enforcing on non-materialSkin components
            // This HAS to be set before the AddFormToManage()
            materialSkinManager.EnforceBackcolorOnAllComponents = true;

            // MaterialSkinManager properties
            materialSkinManager.AddFormToManage(this);
            materialSkinManager.Theme = MaterialSkinManager.Themes.DARK;
            materialSkinManager.ColorScheme = new ColorScheme(Primary.Indigo500, Primary.Indigo700, Primary.Indigo100, Accent.Pink200, TextShade.BLACK);
        }

        public void UpdateText(string text)
        {
            if (materialLabel1.InvokeRequired)
            {
                materialLabel1.BeginInvoke(new Action(() =>
                {
                    materialLabel1.Text = text;
                    materialLabel1.Refresh();
                }));
            }
            else
            {
                materialLabel1.Text = text;
                materialLabel1.Refresh();
            }
        }

        /// <summary>
        /// Resolve 32/64-bit mode: prefer filename (datafile64.bin / localfile64.bin),
        /// fall back to header layout detection for renamed files.
        /// </summary>
        public static bool ResolveIs64Bit(string filePath, BinaryReader br)
        {
            string name = Path.GetFileName(filePath) ?? string.Empty;
            if (name.IndexOf("64.bin", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            // Common x64 names without the "64.bin" substring pattern
            if (name.IndexOf("datafile64", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("localfile64", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            return BDAT_CONTENT.DetectIs64Bit(br);
        }

        string FilePath = "";
        string OutPath = "";
        private void materialButton1_Click(object sender, EventArgs e)
        {
            if (!isBusy)
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Title = "Search for a bin file";
                openFileDialog.Filter = "bin files (*.bin)|*.bin|All files (*.*)|*.*";
                DialogPaths.Apply(openFileDialog);
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    if (File.Exists(openFileDialog.FileName))
                    {
                        DialogPaths.RememberFile(openFileDialog.FileName);
                        FilePath = openFileDialog.FileName;
                        materialButton2.Enabled = true;
                        materialButton3.Enabled = true;
                        string name = Path.GetFileName(FilePath);
                        bool nameLooks64 = name.IndexOf("64.bin", StringComparison.OrdinalIgnoreCase) >= 0
                            || name.IndexOf("datafile64", StringComparison.OrdinalIgnoreCase) >= 0
                            || name.IndexOf("localfile64", StringComparison.OrdinalIgnoreCase) >= 0;
                        UpdateText(nameLooks64
                            ? "Selected (likely 64-bit): " + name
                            : "Selected (likely 32-bit): " + name + " — header checked on unpack");
                    }
                }
            }
            else
            {
                materialCard1.Visible = true;
            }
        }

        public void 输出保存XML(string dir)
        {
            UpdateText("Saving to XML...");
            for (int i = 0; i < _content.ListCount; i++)
            {
                try
                {
                    UpdateText("Saving: " + i + "/" + _content.ListCount);
                    BDAT_LIST bDAT_LIST = _content.Lists[i];
                    BXML_LIST bXML_LIST = new BXML_LIST();
                    if (bDAT_LIST.ID == 9)
                    {
                        bDAT_LIST.ID = 9;
                    }
                    bXML_LIST.Convert(bDAT_LIST);
                    XmlWriterSettings settings = new XmlWriterSettings
                    {
                        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                        Indent = true,
                        NewLineHandling = NewLineHandling.Entitize
                    };
                    MemoryStream memoryStream = new MemoryStream();
                    XmlWriter xmlWriter = XmlWriter.Create(memoryStream, settings);
                    BxmlListSerializer.Serialize(xmlWriter, bXML_LIST);
                    string @string = Encoding.UTF8.GetString(memoryStream.ToArray());
                    string text = $"datafile_{bDAT_LIST.ID:000}.xml";
                    File.WriteAllText(dir + "/" + text, @string, Encoding.UTF8);
                    Application.DoEvents();
                }
                catch (Exception ex)
                {
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine(ex.InnerException.Message);
                    }
                    Console.WriteLine(ex.Message);
                }
            }
        }

        public void 读取BIN(BinaryReader br, bool is64 = false)
        {
            _is64BitFile = is64;
            if (is64)
            {
                _content.Read64(br);
            }
            else
            {
                _content.Read(br);
            }
        }

        LegacyBin.BDat LegacyBinActor = new LegacyBin.BDat();
        bool isBusy = false;
        private void materialButton2_Click(object sender, EventArgs e)
        {
            if (!isBusy)
            {
                if (File.Exists(FilePath))
                {
                    Task.Delay(50).ContinueWith(delegate
                    {
                        try
                        {
                            using (BDat LegacyBinActor = new BDat())
                            {
                                isBusy = true;
                                Control.CheckForIllegalCrossThreadCalls = false;

                                UpdateText("Reading bin...");
                                using (FileStream fileStream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                                using (BinaryReader binaryReader = new BinaryReader(fileStream))
                                {
                                    OutPath = FilePath + ".files";
                                    bool is64 = ResolveIs64Bit(FilePath, binaryReader);
                                    UpdateText(is64 ? "Reading 64-bit bin..." : "Reading 32-bit bin...");
                                    读取BIN(binaryReader, is64);
                                    Directory.CreateDirectory(OutPath);
                                    输出保存XML(OutPath);
                                }
                                UpdateText("GC Cleanup...");
                            }
                            GC.Collect();
                            UpdateText("Done! (" + (_is64BitFile ? "64-bit" : "32-bit") + ", " + _content.ListCount + " tables)");
                        }
                        catch (Exception ex)
                        {
                            UpdateText("Error: " + ex.Message);
                            MessageBox.Show(ex.ToString(), "Unpack failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        finally
                        {
                            isBusy = false;
                        }
                    });
                }
            }
            else
            {
                materialCard1.Visible = true;
            }
        }

        private void materialButton4_Click(object sender, EventArgs e)
        {
            materialCard1.Visible = false;
        }

        public void 加载XML(string dir)
        {
            xml_list.Clear();
            for (int i = 0; i < _content.ListCount; i++)
            {
                UpdateText("Reading XML: " + (i + 1) + "/" + _content.ListCount);
                BDAT_LIST bDAT_LIST = _content.Lists[i];
                string text = $"datafile_{bDAT_LIST.ID:000}.xml";
                string path = Path.Combine(dir, text);
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("Missing exported table XML: " + text, path);
                }
                string s = File.ReadAllText(path, Encoding.UTF8);
                BXML_LIST item;
                using (MemoryStream input = new MemoryStream(Encoding.UTF8.GetBytes(s)))
                {
                    using (XmlTextReader xmlTextReader = new XmlTextReader(input))
                    {
                        xmlTextReader.Normalization = false;
                        item = (BXML_LIST)BxmlListSerializer.Deserialize(xmlTextReader);
                    }
                }
                xml_list.Add(item);
                Application.DoEvents();
            }
        }

        public void 检查Xml()
        {
            checkresult = true;
            int mismatches = 0;
            for (int i = 0; i < _content.ListCount; i++)
            {
                BDAT_LIST bDAT_LIST = _content.Lists[i];
                for (int j = 0; j < xml_list.Count; j++)
                {
                    if (xml_list[j].id != bDAT_LIST.ID)
                    {
                        continue;
                    }
                    try
                    {
                        bool ok;
                        if (bDAT_LIST.Collection.Compressed >= 1)
                        {
                            if (bDAT_LIST.Collection.Archive == null || xml_list[j].collection?.archive == null)
                            {
                                ok = false;
                            }
                            else
                            {
                                ok = bDAT_LIST.Collection.Archive.Compare(xml_list[j].collection.archive);
                            }
                        }
                        else
                        {
                            if (bDAT_LIST.Collection.Loose == null || xml_list[j].collection?.loose == null)
                            {
                                ok = false;
                            }
                            else
                            {
                                ok = bDAT_LIST.Collection.Loose.Compare(xml_list[j].collection.loose);
                            }
                        }
                        if (!ok)
                        {
                            mismatches++;
                            checkresult = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Never abort the whole repack on a single table compare glitch
                        mismatches++;
                        checkresult = false;
                        Console.WriteLine("Compare failed id=" + bDAT_LIST.ID + ": " + ex.Message);
                    }
                }
            }
            if (mismatches > 0)
            {
                UpdateText("XML verify: " + mismatches + " table(s) differ (continuing repack)...");
            }
        }

        public void Xml转BIN()
        {
            for (int i = 0; i < _content.ListCount; i++)
            {
                BDAT_LIST bDAT_LIST = _content.Lists[i];
                for (int j = 0; j < xml_list.Count; j++)
                {
                    if (xml_list[j].id == bDAT_LIST.ID)
                    {
                        if (bDAT_LIST.ID == 38)
                        {
                            bDAT_LIST.ID = 38;
                        }
                        string text = $"datafile_{bDAT_LIST.ID:000}.xml";
                        if (bDAT_LIST.Collection.Compressed >= 1)
                        {
                            BXML_ARCHIVE archive = xml_list[j].collection.archive;
                            bDAT_LIST.Collection.Archive.UseChange(archive);
                        }
                        else
                        {
                            BXML_LOOSE loose = xml_list[j].collection.loose;
                            bDAT_LIST.Collection.Loose.UseChange(loose);
                        }
                    }
                }
            }
        }

        private void 编写BIN(BinaryWriter bw, bool is64)
        {
            _is64BitFile = is64;
            if (is64)
            {
                _content.Write64(bw);
            }
            else
            {
                _content.Write(bw);
            }
            bw.Flush();
        }

        private void materialButton3_Click(object sender, EventArgs e)
        {
            if (!isBusy)
            {
                if (string.IsNullOrEmpty(OutPath))
                {
                    OutPath = FilePath + ".files";
                }
                if (Directory.Exists(OutPath))
                {
                    Task.Delay(50).ContinueWith(delegate
                    {
                        try
                        {
                            using (BDat LegacyBinActor = new BDat())
                            {
                                isBusy = true;
                                Control.CheckForIllegalCrossThreadCalls = false;

                                UpdateText("Reading bin...");
                                bool is64;
                                using (FileStream fileStream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                                using (BinaryReader br = new BinaryReader(fileStream))
                                {
                                    is64 = ResolveIs64Bit(FilePath, br);
                                    UpdateText(is64 ? "Reading 64-bit bin..." : "Reading 32-bit bin...");
                                    读取BIN(br, is64);
                                }

                                UpdateText("Reading xml...");
                                加载XML(OutPath);
                                UpdateText("Verifying xml...");
                                检查Xml();
                                UpdateText("Updating bin...");
                                Xml转BIN();
                                UpdateText(is64 ? "Saving 64-bit bin..." : "Saving 32-bit bin...");

                                // Write to temp then replace so a failed write does not wipe the original
                                string tempPath = FilePath + ".tmp";
                                using (FileStream fileStream2 = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                                using (BinaryWriter binaryWriter = new BinaryWriter(fileStream2))
                                {
                                    编写BIN(binaryWriter, is64);
                                }
                                File.Copy(tempPath, FilePath, true);
                                File.Delete(tempPath);
                                Directory.Delete(OutPath, true);
                            }
                            GC.Collect();
                            UpdateText("Done! Repacked (" + (_is64BitFile ? "64-bit" : "32-bit") + ")");
                        }
                        catch (Exception ex)
                        {
                            UpdateText("Error: " + ex.Message);
                            MessageBox.Show(ex.ToString(), "Repack failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        finally
                        {
                            isBusy = false;
                        }
                    });
                }
                else
                {
                    MessageBox.Show("Unpack folder not found:\n" + OutPath + "\n\nUnpack the bin first, edit the XML, then repack.",
                        "Repack", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            else
            {
                materialCard1.Visible = true;
            }
        }

        private void materialButtonEditor_Click(object sender, EventArgs e)
        {
            // Reuse an existing editor window if already open
            foreach (Form f in Application.OpenForms)
            {
                if (f is BinEditorForm existing)
                {
                    existing.Show();
                    existing.BringToFront();
                    if (existing.WindowState == FormWindowState.Minimized)
                    {
                        existing.WindowState = FormWindowState.Normal;
                    }
                    return;
                }
            }
            var editor = new BinEditorForm();
            editor.Show(this);
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            // Only exit the process when nothing else is open (editor may still be running).
            if (Application.OpenForms.Count == 0)
            {
                Application.Exit();
            }
        }
    }
}
