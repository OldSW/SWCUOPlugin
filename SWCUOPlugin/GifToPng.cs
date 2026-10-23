using System;
using System.IO;
using System.IO.Compression;

namespace Assistant
{
    /// <summary>
    /// Minimal GIF -> PNG converter without external dependencies (System.Drawing isn't available on every
    /// runtime ClassicUO may use). Decodes the first frame (global/local palette, transparency, interlacing)
    /// and writes it as an 8-bit RGBA PNG.
    /// </summary>
    public static class GifToPng
    {
        public static byte[] Convert(byte[] gif)
        {
            int width, height;
            byte[] rgba = DecodeFirstFrame(gif, out width, out height);
            return EncodePng(rgba, width, height);
        }

        public static bool IsPng(byte[] data)
            => data.Length >= 8 && data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G';

        // ── GIF ─────────────────────────────────────────────────────────────

        private static byte[] DecodeFirstFrame(byte[] d, out int width, out int height)
        {
            if (d.Length < 13 || d[0] != 'G' || d[1] != 'I' || d[2] != 'F')
                throw new InvalidDataException("not a GIF");

            width = d[6] | d[7] << 8;
            height = d[8] | d[9] << 8;
            int flags = d[10];
            int pos = 13;
            byte[]? globalPalette = null;
            if ((flags & 0x80) != 0)
            {
                int size = 3 * (1 << ((flags & 7) + 1));
                globalPalette = Slice(d, pos, size);
                pos += size;
            }

            var canvas = new byte[width * height * 4]; // starts fully transparent
            int transparent = -1;

            while (pos < d.Length)
            {
                int block = d[pos++];
                if (block == 0x21) // extension
                {
                    int label = d[pos++];
                    if (label == 0xF9 && d[pos] >= 4) // graphic control extension
                        transparent = (d[pos + 1] & 1) != 0 ? d[pos + 4] : -1;
                    SkipSubBlocks(d, ref pos);
                }
                else if (block == 0x2C) // image descriptor
                {
                    int left = d[pos] | d[pos + 1] << 8, top = d[pos + 2] | d[pos + 3] << 8;
                    int w = d[pos + 4] | d[pos + 5] << 8, h = d[pos + 6] | d[pos + 7] << 8;
                    int iflags = d[pos + 8];
                    pos += 9;

                    byte[]? palette = globalPalette;
                    if ((iflags & 0x80) != 0)
                    {
                        int size = 3 * (1 << ((iflags & 7) + 1));
                        palette = Slice(d, pos, size);
                        pos += size;
                    }
                    if (palette == null)
                        throw new InvalidDataException("GIF has no palette");

                    int minCode = d[pos++];
                    var data = new MemoryStream();
                    while (pos < d.Length && d[pos] != 0)
                    {
                        data.Write(d, pos + 1, d[pos]);
                        pos += d[pos] + 1;
                    }

                    byte[] indices = LzwDecode(data.ToArray(), minCode, w * h);
                    bool interlaced = (iflags & 0x40) != 0;
                    int[] rows = RowOrder(h, interlaced);

                    for (int i = 0; i < h; i++)
                    {
                        int y = top + rows[i];
                        if (y >= height) continue;
                        for (int x = 0; x < w; x++)
                        {
                            int cx = left + x;
                            int index = indices[i * w + x];
                            if (cx >= width || index == transparent || index * 3 + 2 >= palette.Length) continue;
                            int o = (y * width + cx) * 4;
                            canvas[o] = palette[index * 3];
                            canvas[o + 1] = palette[index * 3 + 1];
                            canvas[o + 2] = palette[index * 3 + 2];
                            canvas[o + 3] = 255;
                        }
                    }
                    return canvas;
                }
                else // trailer or garbage
                    break;
            }
            throw new InvalidDataException("GIF contains no image");
        }

        // rows[i] = destination row of the i-th decoded row
        private static int[] RowOrder(int h, bool interlaced)
        {
            var rows = new int[h];
            if (!interlaced)
            {
                for (int i = 0; i < h; i++) rows[i] = i;
                return rows;
            }
            int n = 0;
            int[] start = { 0, 4, 2, 1 }, step = { 8, 8, 4, 2 };
            for (int pass = 0; pass < 4; pass++)
                for (int y = start[pass]; y < h; y += step[pass])
                    rows[n++] = y;
            return rows;
        }

        private static byte[] LzwDecode(byte[] data, int minCode, int pixelCount)
        {
            var output = new byte[pixelCount];
            int clear = 1 << minCode, end = clear + 1;
            var prefix = new short[4096];
            var suffix = new byte[4096];
            var stack = new byte[4097];
            for (int i = 0; i < clear; i++) suffix[i] = (byte)i;

            int codeSize = minCode + 1, next = end + 1, prev = -1, first = 0;
            int bits = 0, bitCount = 0, bytePos = 0, outPos = 0;

            while (outPos < pixelCount)
            {
                while (bitCount < codeSize)
                {
                    if (bytePos >= data.Length) return output; // truncated: keep what we have
                    bits |= data[bytePos++] << bitCount;
                    bitCount += 8;
                }
                int code = bits & ((1 << codeSize) - 1);
                bits >>= codeSize;
                bitCount -= codeSize;

                if (code == clear) { codeSize = minCode + 1; next = end + 1; prev = -1; continue; }
                if (code == end) break;

                int cur = code, sp = 0;
                if (prev == -1)
                {
                    output[outPos++] = suffix[code];
                    prev = first = code;
                    continue;
                }
                if (code >= next) // KwKwK case
                {
                    stack[sp++] = (byte)first;
                    cur = prev;
                }
                while (cur >= clear)
                {
                    stack[sp++] = suffix[cur];
                    cur = prefix[cur];
                }
                first = suffix[cur];
                stack[sp++] = (byte)first;

                if (next < 4096)
                {
                    prefix[next] = (short)prev;
                    suffix[next] = (byte)first;
                    next++;
                    if (next == (1 << codeSize) && codeSize < 12) codeSize++;
                }
                prev = code;
                while (sp > 0 && outPos < pixelCount)
                    output[outPos++] = stack[--sp];
            }
            return output;
        }

        private static void SkipSubBlocks(byte[] d, ref int pos)
        {
            while (pos < d.Length && d[pos] != 0)
                pos += d[pos] + 1;
            pos++;
        }

        private static byte[] Slice(byte[] d, int pos, int length)
        {
            var result = new byte[length];
            Array.Copy(d, pos, result, 0, Math.Min(length, d.Length - pos));
            return result;
        }

        // ── PNG ─────────────────────────────────────────────────────────────

        private static byte[] EncodePng(byte[] rgba, int width, int height)
        {
            var raw = new byte[(width * 4 + 1) * height]; // each scanline prefixed with filter type 0
            for (int y = 0; y < height; y++)
                Array.Copy(rgba, y * width * 4, raw, y * (width * 4 + 1) + 1, width * 4);

            var zlib = new MemoryStream();
            zlib.WriteByte(0x78);
            zlib.WriteByte(0x9C);
            using (var deflate = new DeflateStream(zlib, CompressionMode.Compress, true))
                deflate.Write(raw, 0, raw.Length);
            uint adler = Adler32(raw);
            zlib.Write(new[] { (byte)(adler >> 24), (byte)(adler >> 16), (byte)(adler >> 8), (byte)adler }, 0, 4);

            var png = new MemoryStream();
            png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
            var ihdr = new byte[13];
            WriteBigEndian(ihdr, 0, (uint)width);
            WriteBigEndian(ihdr, 4, (uint)height);
            ihdr[8] = 8; // bit depth
            ihdr[9] = 6; // RGBA
            WriteChunk(png, "IHDR", ihdr);
            WriteChunk(png, "IDAT", zlib.ToArray());
            WriteChunk(png, "IEND", new byte[0]);
            return png.ToArray();
        }

        private static void WriteChunk(Stream s, string type, byte[] data)
        {
            var header = new byte[8];
            WriteBigEndian(header, 0, (uint)data.Length);
            for (int i = 0; i < 4; i++) header[4 + i] = (byte)type[i];
            s.Write(header, 0, 8);
            s.Write(data, 0, data.Length);

            uint crc = Crc32(header, 4, 4, 0);
            crc = Crc32(data, 0, data.Length, crc);
            var tail = new byte[4];
            WriteBigEndian(tail, 0, crc);
            s.Write(tail, 0, 4);
        }

        private static void WriteBigEndian(byte[] b, int offset, uint v)
        {
            b[offset] = (byte)(v >> 24);
            b[offset + 1] = (byte)(v >> 16);
            b[offset + 2] = (byte)(v >> 8);
            b[offset + 3] = (byte)v;
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        // Continues a running CRC (pass 0 to start).
        private static uint Crc32(byte[] data, int offset, int length, uint crc)
        {
            crc ^= 0xFFFFFFFF;
            for (int i = offset; i < offset + length; i++)
                crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFF;
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte x in data)
            {
                a = (a + x) % 65521;
                b = (b + a) % 65521;
            }
            return b << 16 | a;
        }
    }
}
