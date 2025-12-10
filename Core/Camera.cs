using System;
using System.Collections.Generic;

namespace MassaKWin.Core
{
    public class Camera
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public int Port { get; set; } = 80;
        public string Username { get; set; } = "admin";
        public string Password { get; set; } = string.Empty;

        public int BasePosX { get; set; } = 100;
        public int BasePosY { get; set; } = 100;
        public int LineHeight { get; set; } = 40;

        public List<CameraScaleBinding> Bindings { get; set; } = new List<CameraScaleBinding>();
    }
}
