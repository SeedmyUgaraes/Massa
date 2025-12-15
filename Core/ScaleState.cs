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

        /// <summary>
        /// Последний известный онлайн-статус. Null означает, что статус еще не определён.
        /// </summary>
        public bool? LastOnline { get; private set; }

        /// <summary>
        /// Момент времени, с которого весы находятся в текущем статусе.
        /// </summary>
        public DateTime StatusSinceUtc { get; private set; } = DateTime.UtcNow;

        public bool IsOnline(TimeSpan offlineThreshold)
        {
            // Определяем онлайн состояние по давности последнего обновления
            if (LastUpdateUtc == default)
                return false;

            return DateTime.UtcNow - LastUpdateUtc <= offlineThreshold;
        }

        /// <summary>
        /// Обновляет статус и при необходимости фиксирует момент смены состояния.
        /// </summary>
        /// <param name="online">Актуальный статус онлайн/оффлайн.</param>
        public bool UpdateStatus(bool online)
        {
            bool changed = LastOnline is null || online != LastOnline;

            if (changed)
            {
                StatusSinceUtc = DateTime.UtcNow;
            }

            LastOnline = online;

            return changed;
        }
    }
}
