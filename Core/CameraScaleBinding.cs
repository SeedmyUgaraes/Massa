using System;

namespace MassaKWin.Core
{
    public class CameraScaleBinding
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Camera? Camera { get; set; }
        public Scale? Scale { get; set; }

        public int OverlayId { get; set; }
        public int PositionX { get; set; }
        public int PositionY { get; set; }

        public bool Enabled { get; set; } = true;
        public bool AutoPosition { get; set; } = true;
    }
}
