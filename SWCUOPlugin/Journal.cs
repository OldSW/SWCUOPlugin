using System;
using System.IO;
using System.Text;

namespace Assistant
{
    /// <summary>
    /// Writes received speech, emotes and system messages to <c>&lt;folder&gt;/&lt;character&gt;_&lt;yyyy-MM-dd&gt;.txt</c>.
    /// Reads the server → client packets 0x1C (ASCII speech), 0xAE (Unicode speech),
    /// 0xC1 (cliloc message) and 0xCC (cliloc message with affix).
    /// </summary>
    public class Journal
    {
        // MessageType values (ClassicUO.Game.Data.MessageType)
        private const byte TypeRegular = 0;
        private const byte TypeSystem  = 1;
        private const byte TypeLabel   = 6;

        private static readonly string[] TypeNames =
        {
            "", "System", "Emote", "", "", "", "Label", "Focus", "Whisper", "Yell", "Spell",
            "", "", "Guild", "Alliance", "Command", "GM",
        };

        private readonly string _folder;
        private readonly object _lock = new object();
        private StreamWriter? _writer;
        private string? _currentPath;
        private string _character = "unknown";

        public Journal(string folder) => _folder = folder;

        // ── Packet hooks ────────────────────────────────────────────────────

        /// <summary>Client → server. Picks up the character name from 0x5D (login character) / 0x00 (create character).</summary>
        public unsafe void OnSend(IntPtr data, int length)
        {
            byte* p = (byte*)data;
            if (length >= 35 && p[0] == 0x5D)
                SetCharacter(ReadAscii(p, 5, 30, length));
            else if (length >= 40 && p[0] == 0x00)
                SetCharacter(ReadAscii(p, 10, 30, length));
        }

        /// <summary>Server → client.</summary>
        public unsafe void OnRecv(IntPtr data, int length)
        {
            byte* p = (byte*)data;
            if (length < 3)
                return;

            switch (p[0])
            {
                case 0x1C: ParseAsciiSpeech(p, length);   break;
                case 0xAE: ParseUnicodeSpeech(p, length); break;
                case 0xC1:
                case 0xCC: ParseCliloc(p, length);        break;
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                _writer?.Dispose();
                _writer = null;
                _currentPath = null;
            }
        }

        // ── Packet parsing ──────────────────────────────────────────────────
        // Common header: id(1) len(2) serial(4) graphic(2) type(1) hue(2) font(2)

        private unsafe void ParseAsciiSpeech(byte* p, int length)
        {
            if (length < 44)
                return;
            uint serial = ReadUInt32(p, 3);
            ushort graphic = ReadUInt16(p, 7);
            byte type = p[9];
            ushort hue = ReadUInt16(p, 10);
            ushort font = ReadUInt16(p, 12);
            string name = ReadAscii(p, 14, 30, length);
            string text = ReadAscii(p, 44, length - 44, length);

            // Handshake packet some servers send, not a real message
            if (serial == 0 && graphic == 0 && type == TypeRegular && font == 0xFFFF && hue == 0xFFFF &&
                name.StartsWith("SYSTEM", StringComparison.Ordinal))
                return;

            Write(serial, type, name, text);
        }

        private unsafe void ParseUnicodeSpeech(byte* p, int length)
        {
            if (length < 48)
                return;
            uint serial = ReadUInt32(p, 3);
            ushort graphic = ReadUInt16(p, 7);
            byte type = p[9];
            ushort hue = ReadUInt16(p, 10);
            ushort font = ReadUInt16(p, 12);
            // lang(4) at 14
            string name = ReadAscii(p, 18, 30, length);
            string text = ReadUnicode(p, 48, length - 48, Encoding.BigEndianUnicode);

            if (serial == 0 && graphic == 0 && type == TypeRegular && font == 0xFFFF && hue == 0xFFFF &&
                name.Equals("system", StringComparison.OrdinalIgnoreCase))
                return;

            Write(serial, type, name, text);
        }

        private unsafe void ParseCliloc(byte* p, int length)
        {
            bool affixed = p[0] == 0xCC;
            int nameOffset = affixed ? 19 : 18;
            if (length < nameOffset + 30)
                return;

            uint serial = ReadUInt32(p, 3);
            byte type = p[9];
            int cliloc = (int)ReadUInt32(p, 14);
            byte flags = affixed ? p[18] : (byte)0;
            string name = ReadAscii(p, nameOffset, 30, length);

            int pos = nameOffset + 30;
            string affix = "";
            if (affixed)
            {
                affix = ReadAscii(p, pos, length - pos, length);
                pos += affix.Length + 1;
            }

            string args = pos < length
                ? ReadUnicode(p, pos, length - pos, affixed ? Encoding.BigEndianUnicode : Encoding.Unicode)
                : "";

            string text = Engine.GetCliloc(cliloc, args, false) ?? $"#{cliloc} {args.Replace('\t', ' ')}".TrimEnd();

            if (affix.Trim().Length > 0)
                text = (flags & 0x01) != 0 ? affix + text : text + affix; // 0x01 = prepend

            Write(serial, type, name, text);
        }

        // ── Output ──────────────────────────────────────────────────────────

        private void Write(uint serial, byte type, string name, string text)
        {
            if (type == TypeLabel || text.Trim().Length == 0)
                return;

            bool system = type == TypeSystem || serial == 0 || serial == 0xFFFFFFFF || name.Length == 0;
            string label = system ? "System" : type < TypeNames.Length ? TypeNames[type] : "";

            var line = new StringBuilder();
            line.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append("] ");
            if (label.Length > 0)
                line.Append('[').Append(label).Append("] ");
            if (!system)
                line.Append(name).Append(": ");
            line.Append(text.Replace("\r", "").Replace('\n', ' '));

            WriteLine(line.ToString());
        }

        private void WriteLine(string line)
        {
            lock (_lock)
            {
                try
                {
                    string path = Path.Combine(_folder, $"{_character}_{DateTime.Now:yyyy-MM-dd}.txt");
                    if (path != _currentPath)
                    {
                        _writer?.Dispose();
                        _writer = null;
                        Directory.CreateDirectory(_folder);
                        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                        _currentPath = path;
                        DebugLog.Write($"Journal: writing to {path}");
                    }
                    _writer!.WriteLine(line);
                }
                catch (Exception ex)
                {
                    DebugLog.Once("Journal write failed", $"Journal: write failed (further errors suppressed): {ex}");
                    _writer?.Dispose();
                    _writer = null;
                    _currentPath = null;
                }
            }
        }

        private void SetCharacter(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name.Trim())
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            string character = sb.Length > 0 ? sb.ToString() : "unknown";

            lock (_lock)
            {
                if (character == _character)
                    return;
                _character = character;
            }
            DebugLog.Write($"Journal: character {character}");
        }

        // ── Readers ─────────────────────────────────────────────────────────

        private static unsafe uint ReadUInt32(byte* p, int i)
            => (uint)(p[i] << 24 | p[i + 1] << 16 | p[i + 2] << 8 | p[i + 3]);

        private static unsafe ushort ReadUInt16(byte* p, int i)
            => (ushort)(p[i] << 8 | p[i + 1]);

        /// <summary>Reads up to <paramref name="max"/> single-byte chars (Latin-1, like ClassicUO), stopping at NUL.</summary>
        private static unsafe string ReadAscii(byte* p, int offset, int max, int length)
        {
            var sb = new StringBuilder();
            for (int i = offset; i < offset + max && i < length && p[i] != 0; i++)
                sb.Append((char)p[i]);
            return sb.ToString();
        }

        /// <summary>Reads UTF-16 text of at most <paramref name="byteCount"/> bytes, stopping at a NUL char.</summary>
        private static unsafe string ReadUnicode(byte* p, int offset, int byteCount, Encoding encoding)
        {
            int n = 0;
            while (n + 1 < byteCount && (p[offset + n] != 0 || p[offset + n + 1] != 0))
                n += 2;
            return n > 0 ? encoding.GetString(p + offset, n) : "";
        }
    }
}
