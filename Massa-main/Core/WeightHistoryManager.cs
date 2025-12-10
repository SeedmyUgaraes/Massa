using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace MassaKWin.Core
{
    public record WeightSample(DateTime TimestampUtc, double NetGrams, double TareGrams, bool Stable);

    public class WeightHistoryManager
    {
        private readonly ConcurrentDictionary<Guid, List<WeightSample>> _history = new();
        public TimeSpan HistoryDepth { get; set; } = TimeSpan.FromMinutes(60);

        public void AddSample(Scale scale)
        {
            var list = _history.GetOrAdd(scale.Id, _ => new List<WeightSample>());
            var sample = new WeightSample(
                DateTime.UtcNow,
                scale.State.NetGrams,
                scale.State.TareGrams,
                scale.State.Stable);

            lock (list)
            {
                list.Add(sample);
                var cutoff = DateTime.UtcNow - HistoryDepth;
                // удалить старые точки
                int idx = list.FindIndex(s => s.TimestampUtc >= cutoff);
                if (idx > 0)
                {
                    list.RemoveRange(0, idx);
                }
            }
        }

        public IList<WeightSample> GetSamples(Guid scaleId, TimeSpan window)
        {
            if (!_history.TryGetValue(scaleId, out var list))
                return Array.Empty<WeightSample>();

            var cutoff = DateTime.UtcNow - window;
            lock (list)
            {
                return list.Where(s => s.TimestampUtc >= cutoff).ToList();
            }
        }
    }
}
