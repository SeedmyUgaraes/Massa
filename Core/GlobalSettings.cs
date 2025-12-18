using System;

namespace MassaKWin.Core
{
    public enum WeightUnit
    {
        Kg,
        Gram
    }

    public enum OverlayPosition
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    /// <summary>
    /// Глобальные настройки приложения. Добавлены новые параметры согласно вкладке «Настройки».
    /// </summary>
    public class GlobalSettings
    {
        public bool StartPollingOnStartup { get; set; } = true;
        public int ScaleResponseTimeoutMs { get; set; } = 2000;
        public double WeightDeadband { get; set; } = 5.0;
        public WeightUnit DefaultWeightUnit { get; set; } = WeightUnit.Kg;
        public int WeightDecimalPlaces { get; set; } = 3;
        public bool AutoZeroOnConnect { get; set; } = false;
        public string AutoDiscoveryIpStart { get; set; } = "192.168.0.80";
        public string AutoDiscoveryIpEnd { get; set; } = "192.168.0.89";
        public int DefaultScalePort { get; set; } = 5000;
        public int ScanParallelConnections { get; set; } = 4;
        public int ScanIpTimeoutMs { get; set; } = 2000;
        public string OverlayTextTemplate { get; set; } = "N {net}{unit} T {tare}{unit} [{status}]";
        public string OverlayNoConnectionText { get; set; } = "No connection";
        public string OverlayUnstableText { get; set; } = "U";
        public OverlayPosition OverlayDefaultPosition { get; set; } = OverlayPosition.TopLeft;
        public string LogDirectory { get; set; } = "logs";
        public bool EnableSoundNotifications { get; set; } = false;
        public bool SidebarCollapsed { get; set; } = false;
        public string LastPage { get; set; } = "Scales";
    }
}
