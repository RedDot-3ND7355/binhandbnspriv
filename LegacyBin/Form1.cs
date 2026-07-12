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
            materialLabel1.Text = text;
            materialLabel1.Refresh();
        }

        string FilePath = "";
        string OutPath = "";
        private void materialButton1_Click(object sender, EventArgs e)
        {
            if (!isBusy)
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Title = "Search for a bin file";
                openFileDialog.Filter = "bin files(*.bin)| *.bin";
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    if (File.Exists(openFileDialog.FileName))
                    {
                        FilePath = openFileDialog.FileName;
                        materialButton2.Enabled = true;
                        materialButton3.Enabled = true;
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
                        using (BDat LegacyBinActor = new BDat())
                        {
                            isBusy = true;
                            Control.CheckForIllegalCrossThreadCalls = false;

                            UpdateText("Reading bin...");
                            FileStream fileStream = new FileStream(FilePath, FileMode.Open);
                            BinaryReader binaryReader = new BinaryReader(fileStream);
                            OutPath = FilePath + ".files";
                            读取BIN(binaryReader, FilePath.Contains("64.bin"));
                            Directory.CreateDirectory(OutPath);
                            输出保存XML(OutPath);
                            fileStream.Close();
                            binaryReader.Close();
                            UpdateText("GC Cleanup...");
                        }
                        GC.Collect();
                        UpdateText("Done!");
                        isBusy = false;
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
            for (int i = 0; i < _content.ListCount; i++)
            {
                UpdateText("Reading: " + i + "/" + _content.ListCount);
                BDAT_LIST bDAT_LIST = _content.Lists[i];
                string text = $"datafile_{bDAT_LIST.ID:000}.xml";
                string s = File.ReadAllText(dir + "/" + text, Encoding.UTF8);
                BXML_LIST item;
                using (MemoryStream input = new MemoryStream(Encoding.UTF8.GetBytes(s)))
                {
                    using (XmlTextReader xmlTextReader = new XmlTextReader(input))
                    {
                        xmlTextReader.Normalization = false;
                        item = (BXML_LIST)new XmlSerializer(typeof(BXML_LIST)).Deserialize(xmlTextReader);
                    }
                }
                xml_list.Add(item);
                Application.DoEvents();
            }
        }

        public void 检查Xml()
        {
            checkresult = true;
            for (int i = 0; i < _content.ListCount; i++)
            {
                BDAT_LIST bDAT_LIST = _content.Lists[i];
                for (int j = 0; j < xml_list.Count; j++)
                {
                    if (xml_list[j].id == bDAT_LIST.ID)
                    {
                        if (bDAT_LIST.Collection.Compressed >= 1)
                        {
                            string text = $"datafile_{bDAT_LIST.ID:000}.xml";
                            BXML_ARCHIVE archive = xml_list[j].collection.archive;
                            checkresult = bDAT_LIST.Collection.Archive.Compare(archive);
                        }
                        else
                        {
                            string text2 = $"datafile_{bDAT_LIST.ID:000}.xml";
                            BXML_LOOSE loose = xml_list[j].collection.loose;
                            checkresult = bDAT_LIST.Collection.Loose.Compare(loose);
                        }
                    }
                }
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
                if (Directory.Exists(OutPath))
                {
                    Task.Delay(50).ContinueWith(delegate
                    {
                        using (BDat LegacyBinActor = new BDat())
                        {
                            isBusy = true;
                            Control.CheckForIllegalCrossThreadCalls = false;

                            UpdateText("Reading bin...");
                            FileStream fileStream = new FileStream(FilePath, FileMode.Open);
                            BinaryReader br = new BinaryReader(fileStream);
                            读取BIN(br, FilePath.Contains("64.bin"));
                            UpdateText("Reading xml...");
                            加载XML(OutPath);
                            UpdateText("Verifying xml...");
                            检查Xml();
                            UpdateText("Updating bin...");
                            Xml转BIN();
                            UpdateText("Saving bin...");
                            fileStream.Close();
                            FileStream fileStream2 = new FileStream(FilePath, FileMode.Create);
                            BinaryWriter binaryWriter = new BinaryWriter(fileStream2);
                            编写BIN(binaryWriter, FilePath.Contains("64.bin"));
                            binaryWriter.Close();
                            fileStream2.Close();
                            fileStream.Close();
                            Directory.Delete(OutPath, true);
                        }
                        GC.Collect();
                        UpdateText("Done!");
                        isBusy = false;
                    });
                }
            }
            else
            {
                materialCard1.Visible = true;
            }
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            Process.GetCurrentProcess().Kill();
        }
    }
}
