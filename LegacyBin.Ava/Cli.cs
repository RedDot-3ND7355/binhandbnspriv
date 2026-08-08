using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace LegacyBin.Ava
{
    /// <summary>Headless CLI: unpack &lt;bin&gt; [outDir] / repack &lt;bin&gt; &lt;xmlDir&gt;. Mirrors the GUI flows.</summary>
    internal static class Cli
    {
        public static int RunRepack(string binPath, string xmlDir)
        {
            try
            {
                if (!File.Exists(binPath))
                {
                    Console.Error.WriteLine("File not found: " + binPath);
                    return 1;
                }
                binPath = Path.GetFullPath(binPath);
                if (!Directory.Exists(xmlDir))
                {
                    Console.Error.WriteLine("Folder not found: " + xmlDir);
                    return 1;
                }
                Console.WriteLine("Repacking " + binPath + " <- " + xmlDir);
                RepackService.Repack(binPath, Path.GetFullPath(xmlDir), m => Console.WriteLine("  " + m));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        public static int RunUnpack(string binPath, string outDir)
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
                    bool is64 = BinSession.ResolveIs64Bit(binPath, br);
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
                    Encoding = new UTF8Encoding(false),
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
                            Console.WriteLine("  XML " + (i + 1) + "/" + content.ListCount + " id=" + list.ID);
                        }
                        var bxml = new BXML_LIST();
                        bxml.Convert(list);
                        using (var ms = new MemoryStream())
                        using (var xw = XmlWriter.Create(ms, settings))
                        {
                            ser.Serialize(xw, bxml);
                            string xml = Encoding.UTF8.GetString(ms.ToArray());
                            File.WriteAllText(Path.Combine(outDir, string.Format("datafile_{0:000}.xml", list.ID)), xml, Encoding.UTF8);
                        }
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        fail++;
                        Console.Error.WriteLine("FAIL table idx=" + i + " id=" + list.ID + ": " + ex.Message);
                    }
                }

                Console.WriteLine("Done ok=" + ok + " fail=" + fail + " out=" + outDir);
                return fail == 0 ? 0 : 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }
    }
}
