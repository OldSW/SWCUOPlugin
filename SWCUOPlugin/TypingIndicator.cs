using System;

namespace Assistant
{
    public class TypingIndicator
    {
        public DateTime LastTypingTime { get; private set; } = DateTime.MinValue;

        private TimeSpan TypingDelay { get; } = TimeSpan.FromSeconds(5);

        public void Update()
        {
            long elapsedTicks = Environment.TickCount64 - LastTypingTime.Ticks;

            if (elapsedTicks > TypingDelay.Ticks)
            {
                LastTypingTime = DateTime.UtcNow;
                SendTypingPacket();
            }
        }

        private static void SendTypingPacket()
        {
            // Packet 0xAD — Unicode Speech Request (client → server)
            // https://docs.polserver.com/packets/index.php?Packet=0xAD (PolServer packet reference)
            // Type 0 = regular speech, font 3 = normal, colour 0x0026 = default
            const string text = "[typing]";   // zero-width placeholder; no visible message
            byte[] textBytes = System.Text.Encoding.BigEndianUnicode.GetBytes(text + "\0");

            int headerSize = 13;
            int totalLength = headerSize + textBytes.Length;

            var packet = new byte[totalLength];
            int i = 0;
            packet[i++] = 0xAD;                          // packet id
            packet[i++] = (byte)(totalLength >> 8);      // length high
            packet[i++] = (byte)(totalLength & 0xFF);    // length low
            packet[i++] = 0x00;                          // type: regular
            packet[i++] = 0x00; packet[i++] = 0x03;     // font: 3
            packet[i++] = 0x00; packet[i++] = 0x26;     // colour: 0x0026
            packet[i++] = 0x00;                          // language high
            packet[i++] = 0x00; packet[i++] = 0x00;     // language mid/low
            packet[i++] = 0x00;                          // language terminator

            Array.Copy(textBytes, 0, packet, i, textBytes.Length);

            Engine.SendToServer(ref packet, ref totalLength);
        }
    }
}