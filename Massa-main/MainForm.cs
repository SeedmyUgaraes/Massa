using System;
using System.Windows.Forms;
using MassaKWin.Core;
using Timer = System.Windows.Forms.Timer;

namespace MassaKWin
{
    public class MainForm : Form
    {
        private readonly ScaleManager _scaleManager;
        private readonly WeightHistoryManager _historyManager;
        private readonly MassaKClient _massaClient;
        private CameraManager _cameraManager;
        private CameraOsdService? _cameraOsdService;
        private readonly TimeSpan _offlineThreshold = TimeSpan.FromSeconds(10);
        private readonly Timer _uiTimer;

        private TabControl tabControl;
        private TabPage tabScales;
        private TabPage tabCameras;
        private TabPage tabSettings;
        private TabPage tabLog;

        private DataGridView dgvScales;
        private DataGridView dgvCameras;
        private TextBox txtLog;

        public MainForm()
        {
            Text = "Massa-K / Hikvision Monitor";
            Width = 1100;
            Height = 700;
            StartPosition = FormStartPosition.CenterScreen;

            InitializeComponents();

            _scaleManager = new ScaleManager();
            _historyManager = new WeightHistoryManager();
            _scaleManager.OfflineThreshold = _offlineThreshold;

            _scaleManager.AddScale(new Scale { Name = "Scale 1", Ip = "192.168.0.80", Port = 5000 });
            _scaleManager.AddScale(new Scale { Name = "Scale 2", Ip = "192.168.0.81", Port = 5001 });

            _massaClient = new MassaKClient(
                _scaleManager.Scales,
                pollInterval: TimeSpan.FromMilliseconds(200),
                connectTimeout: TimeSpan.FromSeconds(3),
                offlineThreshold: _offlineThreshold,
                reconnectDelay: TimeSpan.FromSeconds(5));

            _massaClient.LogMessage += AppendLog;
            _massaClient.ScaleUpdated += OnScaleUpdated;
            _massaClient.Start();

            _cameraManager = new CameraManager();

            var cam1 = new Camera
            {
                Name = "Cam 1",
                Ip = "192.168.0.90",
                Port = 80,
                Username = "admin",
                Password = "12345",
                BasePosX = 100,
                BasePosY = 100,
                LineHeight = 40
            };

            int overlayId = 1;
            foreach (var scale in _scaleManager.Scales)
            {
                cam1.Bindings.Add(new CameraScaleBinding
                {
                    Camera = cam1,
                    Scale = scale,
                    OverlayId = overlayId++,
                    AutoPosition = true,
                    Enabled = true
                });
            }

            _cameraManager.Cameras.Add(cam1);

            _cameraOsdService = new CameraOsdService(
                _cameraManager.Cameras,
                _scaleManager,
                TimeSpan.FromMilliseconds(100));

            _cameraOsdService.LogMessage += AppendLog;

            _cameraOsdService.Start();

            _uiTimer = new Timer
            {
                Interval = 500
            };
            _uiTimer.Tick += UiTimerOnTick;
            _uiTimer.Start();

            FormClosing += OnFormClosing;
        }

        private void InitializeComponents()
        {
            tabControl = new TabControl
            {
                Dock = DockStyle.Fill
            };

            tabScales = new TabPage("Весы");
            tabCameras = new TabPage("Камеры");
            tabSettings = new TabPage("Настройки");
            tabLog = new TabPage("Лог");

            tabControl.TabPages.Add(tabScales);
            tabControl.TabPages.Add(tabCameras);
            tabControl.TabPages.Add(tabSettings);
            tabControl.TabPages.Add(tabLog);

            InitializeScalesTab();
            InitializeCamerasTab();
            InitializeSettingsTab();
            InitializeLogTab();

            Controls.Add(tabControl);
        }

        private void InitializeScalesTab()
        {
            dgvScales = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            dgvScales.Columns.Add("Name", "Имя");
            dgvScales.Columns.Add("IpPort", "IP:Port");
            dgvScales.Columns.Add("Protocol", "Протокол");
            dgvScales.Columns.Add("Net", "Net, кг");
            dgvScales.Columns.Add("Tare", "Tare, кг");
            dgvScales.Columns.Add("Stable", "Stable");
            dgvScales.Columns.Add("Online", "Online");

            // TODO: привязать к ScaleManager и заполнить источником данных

            tabScales.Controls.Add(dgvScales);
        }

        private void InitializeCamerasTab()
        {
            dgvCameras = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            dgvCameras.Columns.Add("Name", "Имя камеры");
            dgvCameras.Columns.Add("IpPort", "IP:Port");
            dgvCameras.Columns.Add("Bindings", "Привязок весов");
            dgvCameras.Columns.Add("OsdStatus", "Статус OSD");

            tabCameras.Controls.Add(dgvCameras);
        }

        private void InitializeSettingsTab()
        {
            // Пока просто заглушка, чтобы форма собиралась
            var lblPlaceholder = new Label
            {
                Dock = DockStyle.Fill,
                Text = "Здесь будут глобальные настройки (интервалы опроса, таймауты, история и т.п.)",
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter
            };
            tabSettings.Controls.Add(lblPlaceholder);
        }

        private void InitializeLogTab()
        {
            txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical
            };
            tabLog.Controls.Add(txtLog);
        }

        private void UiTimerOnTick(object sender, EventArgs e)
        {
            RefreshScalesGrid();
            RefreshCamerasGrid();
        }

        private void RefreshScalesGrid()
        {
            dgvScales.Rows.Clear();

            foreach (var scale in _scaleManager.Scales)
            {
                string name = scale.Name;
                string ipPort = $"{scale.Ip}:{scale.Port}";
                string protocol = scale.Protocol.ToString();
                string netKg = (scale.State.NetGrams / 1000.0).ToString("F3");
                string tareKg = (scale.State.TareGrams / 1000.0).ToString("F3");
                string stable = scale.State.Stable ? "Да" : "Нет";
                string online = scale.State.IsOnline(_offlineThreshold) ? "Да" : "Нет";

                dgvScales.Rows.Add(name, ipPort, protocol, netKg, tareKg, stable, online);
            }
        }

        private void OnScaleUpdated(Scale scale)
        {
            _historyManager.AddSample(scale);
            BeginInvoke(new Action(RefreshScalesGrid));
        }

        private void RefreshCamerasGrid()
        {
            if (_cameraManager == null) return;

            dgvCameras.Rows.Clear();

            foreach (var cam in _cameraManager.Cameras)
            {
                var ipPort = $"{cam.Ip}:{cam.Port}";
                var bindingsCount = cam.Bindings?.Count ?? 0;
                string? status = null;

                if (_cameraOsdService != null)
                {
                    status = _cameraOsdService.GetCameraStatus(cam.Id);
                }

                dgvCameras.Rows.Add(
                    cam.Name,
                    ipPort,
                    bindingsCount,
                    status ?? string.Empty);
            }
        }

        private void AppendLog(string message)
        {
            BeginInvoke(new Action(() =>
            {
                txtLog.AppendText(message + Environment.NewLine);
            }));
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            _uiTimer.Stop();
            _massaClient.StopAsync().GetAwaiter().GetResult();
            if (_cameraOsdService != null)
            {
                _cameraOsdService.StopAsync().GetAwaiter().GetResult();
                _cameraOsdService = null;
            }
        }
    }
}
