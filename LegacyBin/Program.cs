using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;

namespace LegacyBin
{
    static class Program
    {
        /// <summary>
        /// GUI by default. CLI:
        ///   LegacyBin.exe unpack &lt;datafile64.bin&gt; [outDir]
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            EmbeddedAssembly.Load("LegacyBin.Resources.DotNetZip.dll", "DotNetZip.dll");
            EmbeddedAssembly.Load("LegacyBin.Resources.MaterialSkin.dll", "MaterialSkin.dll");
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

            if (args != null && args.Length >= 2 &&
                string.Equals(args[0], "unpack", StringComparison.OrdinalIgnoreCase))
            {
                return CliUnpack(args[1], args.Length >= 3 ? args[2] : null);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Main shell: Unpack/Repack tool; open Bin Editor from the form button
            Application.Run(new BinEditorForm());
            return 0;
        }

        static int CliUnpack(string binPath, string outDir)
        {
            try
            {
                if (!File.Exists(binPath))
                {
                    Console.Error.WriteLine("File not found: " + binPath);
                    return 1;
                }
                binPath = Path.GetFullPath(binPath);
                outDir = string.IsNullOrWhiteSpace(outDir) ? binPath + ".files" : Path.GetFullPath(outDir);
                Directory.CreateDirectory(outDir);

                Console.WriteLine("Reading " + binPath);
                var content = new BDAT_CONTENT();
                using (var fs = File.OpenRead(binPath))
                using (var br = new BinaryReader(fs))
                {
                    bool is64 = Form1.ResolveIs64Bit(binPath, br);
                    Console.WriteLine(is64 ? "Mode: 64-bit" : "Mode: 32-bit");
                    if (is64)
                    {
                        content.Read64(br);
                    }
                    else
                    {
                        content.Read(br);
                    }
                }

                Console.WriteLine("Tables: " + content.ListCount);
                var ser = new XmlSerializer(typeof(BXML_LIST));
                var settings = new XmlWriterSettings
                {
                    Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    Indent = true,
                    NewLineHandling = NewLineHandling.Entitize
                };

                int ok = 0;
                int fail = 0;
                for (int i = 0; i < content.ListCount; i++)
                {
                    var list = content.Lists[i];
                    try
                    {
                        if (i % 25 == 0 || i == content.ListCount - 1)
                        {
                            Console.WriteLine($"  XML {i + 1}/{content.ListCount} id={list.ID}");
                        }
                        var bxml = new BXML_LIST();
                        bxml.Convert(list);
                        using (var ms = new MemoryStream())
                        using (var xw = XmlWriter.Create(ms, settings))
                        {
                            ser.Serialize(xw, bxml);
                            string xml = Encoding.UTF8.GetString(ms.ToArray());
                            string name = $"datafile_{list.ID:000}.xml";
                            File.WriteAllText(Path.Combine(outDir, name), xml, Encoding.UTF8);
                        }
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        fail++;
                        Console.Error.WriteLine($"FAIL table idx={i} id={list.ID}: {ex.Message}");
                    }
                }

                Console.WriteLine($"Done ok={ok} fail={fail} out={outDir}");
                return fail == 0 ? 0 : 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            return EmbeddedAssembly.Get(args.Name);
        }
    }
}

