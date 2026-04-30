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
                Console.WriteLine("Typing indicator");
                LastTypingTime = DateTime.UtcNow;
            }
        }
    }
}