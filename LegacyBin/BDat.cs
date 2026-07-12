using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;


namespace LegacyBin
{
    public class BDat : IDisposable
    {
        private bool _disposed = false;
        private SafeHandle _safeHandle = new SafeFileHandle(IntPtr.Zero, true);
        public void Dispose() => Dispose(true);
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            if (disposing)
            {
                _content = null;
                xml_list = null;
                compresssizeMap = null;
                sizeMap = null;
                compresssizeMap2 = null;
                sizeMap2 = null;
                _safeHandle?.Dispose();
            }
            _disposed = true;
        }

        public BDAT_CONTENT _content;
        private int _indexFaqs;
        private int _indexCommons;
        private int _indexCommands;

        public List<BXML_LIST> xml_list = new List<BXML_LIST>();
        private Dictionary<uint, int> compresssizeMap = new Dictionary<uint, int>();
        private Dictionary<uint, int> sizeMap = new Dictionary<uint, int>();
        private static int index;
        private static int index2;
        public static int index3;
        private Dictionary<uint, int> compresssizeMap2 = new Dictionary<uint, int>();
        private Dictionary<uint, int> sizeMap2 = new Dictionary<uint, int>();
        private bool checkresult = true;
        public static int compress_lv = 6;
        public bool bIntData = false;
        public static int CurrentFile = 1;

        private void WriteString(BinaryWriter writer, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                writer.Write((byte)0); // Empty string
                return;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length > 255) throw new Exception("String too long for 1-byte length");
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
        }
    }

    public class BDAT_ARCHIVE
    {
        public int SubArchiveCount;

        public int Unknown;

        public BDAT_SUBARCHIVE[] SubArchives;

        public void Read(BinaryReader br)
        {
            SubArchiveCount = br.ReadInt32();
            Unknown = Ultily.ReadIntFrom2Bytes(br.ReadBytes(2));
            SubArchives = new BDAT_SUBARCHIVE[SubArchiveCount];
            for (int i = 0; i < SubArchiveCount; i++)
            {
                SubArchives[i] = new BDAT_SUBARCHIVE();
                SubArchives[i].Read(br);
            }
        }

        public void Write(BinaryWriter bw)
        {
            bw.Write(SubArchiveCount);
            bw.Write(Ultily.WriteIntTo2Bytes(Unknown));
            for (int i = 0; i < SubArchiveCount; i++)
            {
                SubArchives[i].Write(bw);
            }
        }

        public void UseChange(BXML_ARCHIVE newData)
        {
            for (int i = 0; i < SubArchives.Length; i++)
            {
                SubArchives[i].UseChange(newData.SubArchives[i]);
            }
        }

        public bool Compare(BXML_ARCHIVE newData)
        {
            for (int i = 0; i < SubArchives.Length; i++)
            {
                if (!SubArchives[i].Compare(newData.SubArchives[i]))
                {
                    return false;
                }
            }
            return true;
        }
    }

    public class BDAT_COLLECTION
    {
        public byte Compressed;

        public byte Deprecated;

        public BDAT_ARCHIVE Archive;

        public BDAT_LOOSE Loose;

        public void Read(BinaryReader br)
        {
            Compressed = br.ReadByte();
            if (Convert.ToBoolean(Compressed))
            {
                if (Compressed > 1)
                {
                    br.BaseStream.Seek(br.BaseStream.Position - 1, SeekOrigin.Begin);
                }
                Archive = new BDAT_ARCHIVE();
                Archive.Read(br);
                Loose = null;
                if (Compressed > 1)
                {
                    Deprecated = br.ReadByte();
                }
            }
            else
            {
                Loose = new BDAT_LOOSE();
                Loose.Read(br);
                Archive = null;
            }
        }

        // New
        public void Read64(BinaryReader br)
        {
            Compressed = br.ReadByte();
            if (Convert.ToBoolean(Compressed))
            {
                if (Compressed > 1)
                {
                    br.BaseStream.Seek(br.BaseStream.Position - 1, SeekOrigin.Begin);
                }
                Archive = new BDAT_ARCHIVE();
                Archive.Read(br);
                Loose = null;
                if (Compressed > 1)
                {
                    Deprecated = br.ReadByte();
                }
            }
            else
            {
                Loose = new BDAT_LOOSE();
                Loose.Read64(br);
                Archive = null;
            }
        }

        public void Write(BinaryWriter bw)
        {
            bw.Write(Compressed);
            if (Convert.ToBoolean(Compressed))
            {
                if (Compressed > 1)
                {
                    bw.BaseStream.Seek(bw.BaseStream.Position - 1, SeekOrigin.Begin);
                }
                Archive.Write(bw);
                if (Compressed > 1)
                {
                    bw.Write(Deprecated);
                }
            }
            else
            {
                Loose.Write(bw);
            }
        }

        // new 
        public void Write64(BinaryWriter bw)
        {
            bw.Write(Compressed);
            if (Convert.ToBoolean(Compressed))
            {
                if (Compressed > 1)
                {
                    bw.BaseStream.Seek(bw.BaseStream.Position - 1, SeekOrigin.Begin);
                }
                Archive.Write(bw);
                if (Compressed > 1)
                {
                    bw.Write(Deprecated);
                }
            }
            else
            {
                Loose.Write64(bw);
            }
        }
    }

    public class BDAT_CONTENT
    {
        public byte[] Signature;

        public int Version;

        public byte[] Unknown;

        public int ListCount;

        public BDAT_HEAD HeadList;

        public BDAT_LIST[] Lists;

        public void Read(BinaryReader br)
        {
            Signature = br.ReadBytes(8);
            Version = br.ReadInt32();
            Unknown = br.ReadBytes(9);
            ListCount = br.ReadInt32();
            HeadList = new BDAT_HEAD();
            HeadList.Complement = false;
            if (ListCount < 20)
            {
                HeadList.Complement = true;
            }
            HeadList.Read(br);
            Lists = new BDAT_LIST[ListCount];
            for (int i = 0; i < ListCount; i++)
            {
                Lists[i] = new BDAT_LIST();
                Lists[i].Read(br);
            }
        }

        public void Read64(BinaryReader br)
        {
            Form1.CurrentForm.UpdateText("BDAT_CONTENT..."); // status report
            Signature = br.ReadBytes(8);
            Version = br.ReadInt32();
            Unknown = br.ReadBytes(13);
            ListCount = (int)br.ReadInt64();
            HeadList = new BDAT_HEAD();
            HeadList.Complement = false;
            if (ListCount < 20)
            {
                HeadList.Complement = true;
            }
            Form1.CurrentForm.UpdateText("BDAT_HEAD..."); // status report
            HeadList.Read64(br);
            Lists = new BDAT_LIST[ListCount];
            Form1.CurrentForm.UpdateText("BDAT_LIST..."); // status report
            for (int i = 0; i < ListCount; i++)
            {
                Lists[i] = new BDAT_LIST();
                Lists[i].Read64(br);
            }
        }

        public void Write(BinaryWriter bw)
        {
            bw.Write(Signature);
            bw.Write(Version);
            bw.Write(Unknown);
            bw.Write(ListCount);
            HeadList.Write(bw);
            for (int i = 0; i < ListCount; i++)
            {
                Lists[i].Write(bw);
            }
        }

        // new
        public void Write64(BinaryWriter bw)
        {
            bw.Write(Signature);
            bw.Write(Version);
            bw.Write(Unknown);
            bw.Write((long)ListCount);
            HeadList.Write64(bw);
            for (int i = 0; i < ListCount; i++)
            {
                Lists[i].Write64(bw);
            }
        }
    }

    public class BDAT_FIELDTABLE
    {
        public int Unknown1;

        public int Unknown2;

        public int Size;

        public byte[] Data;

        public int ID;

        public void Read(BinaryReader br)
        {
            Unknown1 = Ultily.ReadIntFrom2Bytes(br.ReadBytes(2));
            Unknown2 = Ultily.ReadIntFrom2Bytes(br.ReadBytes(2));
            if (Unknown1 == 255)
            {
                Size = Ultily.ReadIntFrom2Bytes(br.ReadBytes(2));
            }
            else
            {
                Size = br.ReadInt32();
            }
            if (Size >= 12)
            {
                ID = br.ReadInt32();
                Data = br.ReadBytes(Size - 12);
            }
            else
            {
                Data = new byte[0];
            }
        }

        public void Write(BinaryWriter bw)
        {
            bw.Write(Ultily.WriteIntTo2Bytes(Unknown1));
            bw.Write(Ultily.WriteIntTo2Bytes(Unknown2));
            if (Size > 12)
            {
                bw.Write(Size);
                bw.Write(ID);
                bw.Write(Data);
            }
            else
            {
                bw.Write(Ultily.WriteIntTo2Bytes(Size));
            }
        }

        public void UseChange(BXML_FIELDTABLE newData)
        {
            if (newData == null)
            {
                Data = null;
                return;
            }
            ID = (int)newData.id;
            Unknown1 = newData.unk1;
            Unknown2 = newData.unk2;
            Size = (int)newData.size;
            if (Size > 12)
            {
                if (Form1.CurrentForm.materialCheckbox1.Checked)
                {
                    Data = bcrypt.IntToBytes(newData.data, newData.size - 8);
                }
                if (!Form1.CurrentForm.materialCheckbox1.Checked)
                {
                    Data = bcrypt.HexToBytes(newData.data, newData.size - 8);
                }
            }
            else
            {
                Data = null;
            }
        }

        public bool IsEmpty()
        {
            return Data == null;
        }
    }

    public class BDAT_HEAD
    {
        public bool Complement;

        public int Size_1;

        public int Size_2;

        public int Size_3;

        public byte[] Padding;

        public byte[] Data;

        public void Read(BinaryReader br)
        {
            Size_1 = br.ReadInt32();
            Size_2 = br.ReadInt32();
            Size_3 = br.ReadInt32();
            Padding = br.ReadBytes(62);
            Data = new byte[Size_1];
            if (!Complement)
            {
                Data = br.ReadBytes(Size_1);
            }
        }

        public void Read64(BinaryReader br)
        {
            Size_1 = (int)br.ReadInt64();
            Size_2 = (int)br.ReadInt64();
            Size_3 = (int)br.ReadInt64();
            Padding = br.ReadBytes(62);
            Data = new byte[Size_1];
            if (!Complement)
            {
                Data = br.ReadBytes(Size_1);
            }
        }

        public void Write(BinaryWriter bw)
        {
            bw.Write(Size_1);
            bw.Write(Size_2);
            bw.Write(Size_3);
            bw.Write(Padding);
            if (!Complement)
            {
                bw.Write(Data);
            }
        }

        public void Write64(BinaryWriter bw)
        {
            bw.Write((long)Size_1);
            bw.Write((long)Size_2);
            bw.Write((long)Size_3);
            bw.Write(Padding);
            if (!Complement)
            {
                bw.Write(Data);
            }
        }
    }

    public class BDAT_LIST
    {
        public byte Unknown1;

        public int ID;

        public int Unknown2;

        public int Unknown3;

        public int Size;

        public BDAT_COLLECTION Collection;

        public void Read(BinaryReader br)
        {
            Unknown1 = br.ReadByte();
            ID = Ultily.ReadIntFrom2Bytes(br.ReadBytes(2));
            Unknown2 = Ultily.ReadIntFrom2Bytes(br.ReadBytes(2));
            Unknown3 = Ultily.ReadIntFrom2Bytes(br.ReadBytes(2));
            Size = br.ReadInt32();
            long position = br.BaseStream.Position;
            Collection = new BDAT_COLLECTION();
            Collection.Read(br);
            long position2 = br.BaseStream.Position;
            if (position + Size != position2)
            {
                br.BaseStream.Seek(position + Size, SeekOrigin.Begin);
            }
        }

        public void Read64(BinaryReader br)
        {
            Unknown1 = br.ReadByte();
            ID = Ultily.ReadIntFrom2Bytes(br.ReadBytes(2));
            Unknown2 = Ultily.ReadIntFrom2Bytes(br.ReadBytes(2));
            Unknown3 = Ultily.ReadIntFrom2Bytes(br.ReadBytes(2));
            Size = br.ReadInt32();
            long position = br.BaseStream.Position;
            Collection = new BDAT_COLLECTION();
            Collection.Read64(br);
            long position2 = br.BaseStream.Position;
            if (position + Size != position2)
            {
                br.BaseStream.Seek(position + Size, SeekOrigin.Begin);
            }
        }

        public void Write(BinaryWriter bw)
        {
            bw.Write(Unknown1);
            bw.Write(Ultily.WriteIntTo2Bytes(ID));
            bw.Write(Ultily.WriteIntTo2Bytes(Unknown2));
            bw.Write(Ultily.WriteIntTo2Bytes(Unknown3));
            bw.Write(Size);
            long position = bw.BaseStream.Position;
            Collection.Write(bw);
            long position2 = bw.BaseStream.Position;
            bw.Seek((int)position - 4, SeekOrigin.Begin);
            Size = (int)(position2 - position);
            bw.Write(Size);
            bw.Seek(Size, SeekOrigin.Current);
        }

        // New
        public void Write64(BinaryWriter bw)
        {
            bw.Write(Unknown1);
            bw.Write(Ultily.WriteIntTo2Bytes(ID));
            bw.Write(Ultily.WriteIntTo2Bytes(Unknown2));
            bw.Write(Ultily.WriteIntTo2Bytes(Unknown3));
            bw.Write(Size);
            long position = bw.BaseStream.Position;
            Collection.Write64(bw);
            long position2 = bw.BaseStream.Position;
            bw.Seek((int)position - 4, SeekOrigin.Begin);
            Size = (int)(position2 - position);
            bw.Write(Size);
            bw.Seek(Size, SeekOrigin.Current);
        }
    }

    public class BDAT_LOOKUPTABLE
    {
        public int Size;

        public byte[] Data;

        public void Read(BinaryReader br)
        {
            Data = br.ReadBytes(Size);
        }

        public void Write(BinaryWriter bw)
        {
            bw.Write(Data);
        }

        public int UseChange(BXML_LOOKUPTABLE newData)
        {
            int SizeLookup = 0;
            Data = bnsTool.WordToLookUpData(newData.words, ref SizeLookup);
            Size = SizeLookup;
            return SizeLookup;
        }

        public int Compare(BXML_LOOKUPTABLE newData)
        {
            int SizeLookup = 0;
            byte[] array = bnsTool.WordToLookUpData(newData.words, ref SizeLookup);
            if (SizeLookup != Size)
            {
                return 1;
            }
            for (int i = 0; i < Size; i++)
            {
                if (Data[i] != array[i])
                {
                    return 2;
                }
            }
            return 0;
        }
    }
    public class BDAT_LOOSE
    {
        public int FieldCountUnfixed;

        public int FieldCount;

        public int SizeFields;

        public int SizeLookup;

        public byte Unknown;

        public BDAT_FIELDTABLE[] Fields;

        public int SizePadding;

        public byte[] Padding;

        public BDAT_LOOKUPTABLE Lookup;

        public bool Is64;

        public void Read(BinaryReader br)
        {
            FieldCount = br.ReadInt32();
            FieldCountUnfixed = FieldCount;
            SizeFields = br.ReadInt32();
            SizeLookup = br.ReadInt32();
            Unknown = br.ReadByte();
            long num = br.BaseStream.Position + SizeFields;
            Fields = new BDAT_FIELDTABLE[FieldCount];
            long position;
            for (int i = 0; i < FieldCount; i++)
            {
                position = br.BaseStream.Position;
                if (position >= num)
                {
                    FieldCount = i;
                    br.BaseStream.Seek(num - position, SeekOrigin.Current);
                    break;
                }
                Fields[i] = new BDAT_FIELDTABLE();
                Fields[i].Read(br);
            }
            position = br.BaseStream.Position;
            SizePadding = (int)(num - position);
            if (SizePadding >= 0)
            {
                if (SizePadding > 0)
                {
                    Padding = br.ReadBytes(SizePadding);
                }
                Lookup = new BDAT_LOOKUPTABLE();
                Lookup.Size = SizeLookup;
                Lookup.Read(br);
            }
        }

        public void Read64(BinaryReader br)
        {
            FieldCount = br.ReadInt32();
            FieldCountUnfixed = FieldCount;
            SizeFields = br.ReadInt32();
            SizeLookup = br.ReadInt32();
            Unknown = br.ReadByte();
            if (FieldCount > 0 && SizeFields <= 0)
            {
                br.BaseStream.Position -= 13L;
                FieldCount = (int)br.ReadInt64();
                FieldCountUnfixed = FieldCount;
                SizeFields = br.ReadInt32();
                SizeLookup = br.ReadInt32();
                Unknown = br.ReadByte();
            }
            Is64 = true;
            long num = br.BaseStream.Position + SizeFields;
            Fields = new BDAT_FIELDTABLE[FieldCount];
            long position;
            for (int i = 0; i < FieldCount; i++)
            {
                position = br.BaseStream.Position;
                if (position >= num)
                {
                    FieldCount = i;
                    br.BaseStream.Seek(num - position, SeekOrigin.Current);
                    break;
                }
                Fields[i] = new BDAT_FIELDTABLE();
                Fields[i].Read(br);
            }
            position = br.BaseStream.Position;
            SizePadding = (int)(num - position);
            if (SizePadding >= 0)
            {
                if (SizePadding > 0)
                {
                    Padding = br.ReadBytes(SizePadding);
                }
                Lookup = new BDAT_LOOKUPTABLE();
                Lookup.Size = SizeLookup;
                Lookup.Read(br);
            }
        }

        public void Write(BinaryWriter bw)
        {
            bw.Write(FieldCountUnfixed);
            int num = (int)bw.BaseStream.Position;
            bw.Write(SizeFields);
            bw.Write(SizeLookup);
            bw.Write(Unknown);
            int num2 = (int)bw.BaseStream.Position;
            for (int i = 0; i < FieldCount; i++)
            {
                Fields[i].Write(bw);
            }
            if (SizePadding >= 0)
            {
                if (SizePadding > 0)
                {
                    bw.Write(Padding);
                }
                SizeFields = (int)bw.BaseStream.Position - num2;
                Lookup.Size = SizeLookup;
                Lookup.Write(bw);
                SizeLookup = (int)bw.BaseStream.Position - num2 - SizeFields;
                bw.BaseStream.Seek(num, SeekOrigin.Begin);
                bw.Write(SizeFields);
                bw.Write(SizeLookup);
                bw.BaseStream.Seek(1 + SizeFields + SizeLookup, SeekOrigin.Current);
            }
        }

        // new
        public void Write64(BinaryWriter bw)
        {
            bw.Write((long)FieldCountUnfixed);
            int num = (int)bw.BaseStream.Position;
            bw.Write(SizeFields);
            bw.Write(SizeLookup);
            bw.Write(Unknown);
            int num2 = (int)bw.BaseStream.Position;
            for (int i = 0; i < FieldCount; i++)
            {
                Fields[i].Write(bw);
            }
            if (SizePadding >= 0)
            {
                if (SizePadding > 0)
                {
                    bw.Write(Padding);
                }
                SizeFields = (int)bw.BaseStream.Position - num2;
                Lookup.Size = SizeLookup;
                Lookup.Write(bw);
                SizeLookup = (int)bw.BaseStream.Position - num2 - SizeFields;
                bw.BaseStream.Seek(num, SeekOrigin.Begin);
                bw.Write(SizeFields);
                bw.Write(SizeLookup);
                bw.BaseStream.Seek(1 + SizeFields + SizeLookup, SeekOrigin.Current);
            }
        }

        public void UseChange(BXML_LOOSE newData)
        {
            BXML_FIELDTABLE[] fields = newData.fields;
            FieldCountUnfixed = fields.Length;
            Fields = new BDAT_FIELDTABLE[fields.Length];
            int num = 0;
            for (int i = 0; i < fields.Length; i++)
            {
                Fields[i] = new BDAT_FIELDTABLE();
                Fields[i].UseChange(fields[i]);
                if (Fields[i].IsEmpty() & (Fields[i].Size == 0))
                {
                    num++;
                }
            }
            FieldCount = (int)(FieldCountUnfixed - (uint)num);
            Lookup = new BDAT_LOOKUPTABLE();
            SizeLookup = Lookup.UseChange(newData.lookup);
        }

        public bool Compare(BXML_LOOSE newData)
        {
            return Lookup.Compare(newData.lookup) <= 0;
        }
    }

    public class BDAT_SUBARCHIVE
    {
        public byte[] StartAndEndFieldId;

        public int SizeCompressed;

        public int SizeDecompressed;

        public int FieldLookupCount;

        public BDAT_FIELDTABLE[] Fields;

        public BDAT_LOOKUPTABLE[] Lookups;

        private BNSDat m_bnsDat = new BNSDat();

        private int m_maxSize;

        public void Read(BinaryReader br)
        {
            StartAndEndFieldId = br.ReadBytes(16);
            SizeCompressed = Ultily.ReadIntFrom2Bytes(br.ReadBytes(2));
            byte[] buffer = br.ReadBytes(SizeCompressed);
            SizeDecompressed = Ultily.ReadIntFrom2Bytes(br.ReadBytes(2));
            if (SizeDecompressed < 0)
            {
                Console.WriteLine("SizeCompressed: " + SizeCompressed + "|SizeDecompressed: " + SizeDecompressed);
            }
            byte[] buffer2 = m_bnsDat.Deflate(buffer, SizeCompressed, SizeDecompressed);
            FieldLookupCount = br.ReadInt32();
            Fields = new BDAT_FIELDTABLE[FieldLookupCount];
            Lookups = new BDAT_LOOKUPTABLE[FieldLookupCount];
            BinaryReader binaryReader = new BinaryReader(new MemoryStream(buffer2));
            int num = Ultily.ReadIntFrom2Bytes(br.ReadBytes(2));
            for (int i = 1; i <= FieldLookupCount; i++)
            {
                binaryReader.BaseStream.Seek(num, SeekOrigin.Begin);
                Fields[i - 1] = new BDAT_FIELDTABLE();
                Fields[i - 1].Read(binaryReader);
                num = ((i >= FieldLookupCount) ? SizeDecompressed : Ultily.ReadIntFrom2Bytes(br.ReadBytes(2)));
                Lookups[i - 1] = new BDAT_LOOKUPTABLE();
                Lookups[i - 1].Size = num - (int)binaryReader.BaseStream.Position;
                Lookups[i - 1].Read(binaryReader);
            }
        }

        public void Write(BinaryWriter bw)
        {
            BinaryWriter binaryWriter = new BinaryWriter(new MemoryStream());
            int[] array = new int[FieldLookupCount];
            array[0] = 0;
            for (int i = 1; i <= FieldLookupCount; i++)
            {
                Fields[i - 1].Write(binaryWriter);
                Lookups[i - 1].Write(binaryWriter);
                if (i < FieldLookupCount)
                {
                    array[i] = (int)binaryWriter.BaseStream.Position;
                }
                if ((int)binaryWriter.BaseStream.Length < 65535)
                {
                    m_maxSize = i;
                }
            }
            SizeDecompressed = (int)binaryWriter.BaseStream.Length;
            byte[] array2 = new byte[SizeDecompressed];
            Array.Copy(((MemoryStream)binaryWriter.BaseStream).ToArray(), array2, SizeDecompressed);
            int sizeCompressed;
            byte[] buffer = m_bnsDat.Inflate(array2, SizeDecompressed, out sizeCompressed, 6);
            SizeCompressed = sizeCompressed;
            bw.Write(StartAndEndFieldId);
            bw.Write(Ultily.WriteIntTo2Bytes(SizeCompressed));
            bw.Write(buffer);
            bw.Write(Ultily.WriteIntTo2Bytes(SizeDecompressed));
            bw.Write(FieldLookupCount);
            for (int j = 0; j < FieldLookupCount; j++)
            {
                bw.Write(Ultily.WriteIntTo2Bytes(array[j]));
            }
        }

        public void UseChange(BXML_SUBARCHIVE newData)
        {
            BXML_FIELDTABLE[] fields = newData.fields;
            FieldLookupCount = fields.Length;
            Fields = new BDAT_FIELDTABLE[fields.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                Fields[i] = new BDAT_FIELDTABLE();
                Fields[i].UseChange(newData.fields[i]);
            }
            BXML_LOOKUPTABLE[] lookup = newData.lookup;
            Lookups = new BDAT_LOOKUPTABLE[lookup.Length];
            for (int j = 0; j < lookup.Length; j++)
            {
                Lookups[j] = new BDAT_LOOKUPTABLE();
                Lookups[j].UseChange(newData.lookup[j]);
            }
        }

        public bool Compare(BXML_SUBARCHIVE newData)
        {
            for (int i = 0; i < Lookups.Length; i++)
            {
                if (Lookups[i].Compare(newData.lookup[i]) > 0)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
