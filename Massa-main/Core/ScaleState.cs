using System;

namespace MassaKWin.Core
{
    public class ScaleState
    {
        public double NetGrams { get; set; }
        public double TareGrams { get; set; }
        public bool Stable { get; set; }
        public bool NetFlag { get; set; }
        public bool ZeroFlag { get; set; }
        public DateTime LastUpdateUtc { get; set; }

        public bool IsOnline(TimeSpan offlineThreshold)
        {
            if (LastUpdateUtc == default)
                return false;

            return DateTime.UtcNow - LastUpdateUtc <= offlineThreshold;
        }
    }
}
