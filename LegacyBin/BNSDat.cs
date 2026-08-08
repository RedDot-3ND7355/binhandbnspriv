using System;
using System.IO;
using System.IO.Compression;

namespace LegacyBin
{
	public class BNSDat
	{
		public static bool newversion = false;

		public string BytesToHex(byte[] bytes)
		{
			char[] array = new char[bytes.Length * 2];
			int num = 0;
			int num2 = 0;
			while (num < bytes.Length)
			{
				byte b = (byte)(bytes[num] >> 4);
				array[num2] = (char)((b > 9) ? (b + 55 + 32) : (b + 48));
				b = (byte)(bytes[num] & 0xFu);
				array[++num2] = (char)((b > 9) ? (b + 55 + 32) : (b + 48));
				num++;
				num2++;
			}
			return new string(array);
		}

		public byte[] Deflate(byte[] buffer, int sizeCompressed, int sizeDecompressed)
		{
			byte[] array;
			using (var zs = new ZLibStream(new MemoryStream(buffer), CompressionMode.Decompress))
			{
				using (var ms = new MemoryStream())
				{
					zs.CopyTo(ms);
					array = ms.ToArray();
				}
			}
			byte[] array2 = new byte[sizeDecompressed];
			if (array.Length > sizeDecompressed)
			{
				Array.Copy(array, 0, array2, 0, sizeDecompressed);
			}
			else
			{
				Array.Copy(array, 0, array2, 0, array.Length);
			}
			return array2;
		}

		public byte[] Inflate(byte[] buffer, int sizeDecompressed, out int sizeCompressed, int compressionLevel)
		{
			MemoryStream memoryStream = new MemoryStream();
			using (ZLibStream zlibStream = new ZLibStream(memoryStream, CompressionLevel.Optimal, leaveOpen: true))
			{
				zlibStream.Write(buffer, 0, sizeDecompressed);
				zlibStream.Flush();
			}
			sizeCompressed = (int)memoryStream.Length;
			return memoryStream.ToArray();
		}
	}
}
