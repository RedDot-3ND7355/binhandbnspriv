using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace LegacyBin.Ava
{
    /// <summary>
    /// Repack flow (port of Form1): load the bin fresh from disk, read every exported
    /// datafile_{id:000}.xml, verify against the tables, apply changes, write to temp
    /// then replace the original.
    /// </summary>
    internal static class RepackService
    {
        private static readonly XmlSerializer BxmlListSerializer = new XmlSerializer(typeof(BXML_LIST));

        public static void Repack(string binPath, string xmlDir, Action<string> progress)
        {
            if (string.IsNullOrEmpty(binPath) || !File.Exists(binPath))
            {
                throw new FileNotFoundException("Bin file not found.", binPath);
            }
            if (string.IsNullOrEmpty(xmlDir) || !Directory.Exists(xmlDir))
            {
                throw new DirectoryNotFoundException("Unpack folder not found:\n" + xmlDir);
            }

            // 1) Read the bin fresh from disk (mirrors the editor's repack path).
            var content = new BDAT_CONTENT();
            bool is64;
            using (var fs = File.OpenRead(binPath))
            using (var br = new BinaryReader(fs))
            {
                is64 = BinSession.ResolveIs64Bit(binPath, br);
                progress(is64 ? "Reading 64-bit bin..." : "Reading 32-bit bin...");
                if (is64)
                {
                    content.Read64(br);
                }
                else
                {
                    content.Read(br);
                }
            }

            // 2) Load all exported table XMLs.
            var settings = new XmlWriterSettings
            {
                Encoding = new System.Text.UTF8Encoding(false),
                Indent = true
            };
            _ = settings;
            var xmlList = new List<BXML_LIST>();
            for (int i = 0; i < content.ListCount; i++)
            {
                var list = content.Lists[i];
                string fileName = string.Format("datafile_{0:000}.xml", list.ID);
                string path = Path.Combine(xmlDir, fileName);
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("Missing exported table XML: " + fileName, path);
                }
                progress("Reading XML " + (i + 1) + "/" + content.ListCount);
                string s = File.ReadAllText(path, System.Text.Encoding.UTF8);
                BXML_LIST item;
                using (var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(s)))
                {
                    using (var xr = new XmlTextReader(input))
                    {
                        xr.Normalization = false;
                        item = (BXML_LIST)BxmlListSerializer.Deserialize(xr);
                    }
                }
                xmlList.Add(item);
            }

            // 3) Verify (BnsDatTool-style compare; mismatches do not abort).
            int mismatches = 0;
            for (int i = 0; i < content.ListCount; i++)
            {
                var list = content.Lists[i];
                for (int j = 0; j < xmlList.Count; j++)
                {
                    if (xmlList[j].id != list.ID)
                    {
                        continue;
                    }
                    try
                    {
                        bool ok = IsCompareOk(list, xmlList[j]);
                        if (!ok)
                        {
                            mismatches++;
                        }
                    }
                    catch (Exception ex)
                    {
                        mismatches++;
                        Console.WriteLine("Compare failed id=" + list.ID + ": " + ex.Message);
                    }
                }
            }
            if (mismatches > 0)
            {
                progress("XML verify: " + mismatches + " table(s) differ (continuing repack)...");
            }

            // 4) Apply XML data into the in-memory tables.
            for (int i = 0; i < content.ListCount; i++)
            {
                var list = content.Lists[i];
                for (int j = 0; j < xmlList.Count; j++)
                {
                    if (xmlList[j].id != list.ID)
                    {
                        continue;
                    }
                    if (list.Collection.Compressed >= 1)
                    {
                        list.Collection.Archive.UseChange(xmlList[j].collection.archive);
                    }
                    else
                    {
                        list.Collection.Loose.UseChange(xmlList[j].collection.loose);
                    }
                }
            }

            // 5) Write to temp, then replace the original.
            progress(is64 ? "Saving 64-bit bin..." : "Saving 32-bit bin...");
            string tempPath = binPath + ".tmp";
            try
            {
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var bw = new BinaryWriter(fs))
                {
                    if (is64)
                    {
                        content.Write64(bw);
                    }
                    else
                    {
                        content.Write(bw);
                    }
                    bw.Flush();
                }
                File.Copy(tempPath, binPath, true);
                File.Delete(tempPath);
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // ignore cleanup failures
                }
                throw;
            }
            progress("Done! Repacked (" + (is64 ? "64-bit" : "32-bit") + ")");
        }

        private static bool IsCompareOk(BDAT_LIST list, BXML_LIST xml)
        {
            bool ok;
            if (list.Collection.Compressed >= 1)
            {
                if (list.Collection.Archive == null || xml.collection?.archive == null)
                {
                    ok = false;
                }
                else
                {
                    ok = list.Collection.Archive.Compare(xml.collection.archive);
                }
            }
            else
            {
                if (list.Collection.Loose == null || xml.collection?.loose == null)
                {
                    ok = false;
                }
                else
                {
                    ok = list.Collection.Loose.Compare(xml.collection.loose);
                }
            }
            return ok;
        }
    }
}
