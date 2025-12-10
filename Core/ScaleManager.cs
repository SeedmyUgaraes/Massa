using System;
using System.Collections.Generic;

namespace MassaKWin.Core
{
    public class ScaleManager
    {
        public IList<Scale> Scales { get; } = new List<Scale>();

        public TimeSpan OfflineThreshold { get; set; } = TimeSpan.FromSeconds(10);

        public void AddScale(Scale scale)
        {
            Scales.Add(scale);
        }

        public void RemoveScale(Scale scale)
        {
            Scales.Remove(scale);
        }
    }
}
