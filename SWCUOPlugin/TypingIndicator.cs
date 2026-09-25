using System;

namespace Assistant
{
    public class TypingIndicator
    {
        public DateTime LastTypingPacket { get; private set; } = DateTime.MinValue;
        public int TypingCount { get; private set; } = 0;

        private TimeSpan TypingDelay { get; } = TimeSpan.FromSeconds(5);

        /// <summary>Call when a message is sent (Enter), so the next message can trigger the indicator right away.</summary>
        public void Reset()
        {
            TypingCount = 0;
            LastTypingPacket = DateTime.MinValue;
        }

        /// <summary>Call on every letter key release.</summary>
        public void Update()
        {
            DateTime now = DateTime.UtcNow;
            if (now - LastTypingPacket > TypingDelay && TypingCount > 10)
            {
                LastTypingPacket = now;
                TypingCount = 0;
                SendTypingPacket();
            }
            TypingCount++;
        }

        private static void SendTypingPacket()
        {
            // Packet 0xBF — General Information / extended command (client → server)
            // Subcommand 0xEF — custom typing indicator, handled server-side via
            // PacketHandlers.RegisterExtended(0xEF, ...) (see the swcuors README).
            int totalLength = 5;

            var packet = new byte[totalLength];
            int i = 0;
            packet[i++] = 0xBF;                          // packet id
            packet[i++] = (byte)(totalLength >> 8);      // length high
            packet[i++] = (byte)(totalLength & 0xFF);    // length low
            packet[i++] = 0x00; packet[i++] = 0xEF;     // subcommand: 0x00EF

            Engine.SendToServer(ref packet, ref totalLength);
        }
    }
}