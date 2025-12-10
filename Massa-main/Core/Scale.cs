using System;

namespace MassaKWin.Core
{
    public class Scale
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public int Port { get; set; }
        public ScaleProtocol Protocol { get; set; } = ScaleProtocol.Unknown;
        public ScaleState State { get; set; } = new ScaleState();
    }
}
