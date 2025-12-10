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
        private MassaKClient? _massaClient;
        private CameraManager _cameraManager;
        private CameraOsdService? _cameraOsdService;
        private readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(200);
        private readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(3);
        private readonly TimeSpan _offlineThreshold = TimeSpan.FromSeconds(10);
        private readonly TimeSpan _reconnectDelay = TimeSpan.FromSeconds(5);
        private readonly Timer _uiTimer;

        private TabControl tabControl;
        private TabPage tabScales;
        private TabPage tabCameras;
        private TabPage tabSettings;
        private TabPage tabLog;

        private DataGridView dgvScales;
        private DataGridView dgvCameras;
        private TextBox txtLog;
        private Button _btnAddScale;
        private Button _btnDeleteScale;
        private Button _btnAddCamera;
        private Button _btnDeleteCamera;
        private Button _btnEditBindings;

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

            _cameraManager = new CameraManager();

            RecreateScaleClient();
            RecreateCameraOsdService();

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
            // Колонка с текстовым статусом и временем нахождения в состоянии
            dgvScales.Columns.Add("Status", "Статус");

            // TODO: привязать к ScaleManager и заполнить источником данных

            _btnAddScale = new Button
            {
                Text = "Добавить весы",
                Height = 35
            };
            _btnAddScale.Click += OnAddScaleClicked;

            _btnDeleteScale = new Button
            {
                Text = "Удалить весы",
                Height = 35
            };
            _btnDeleteScale.Click += OnDeleteScaleClicked;

            var scalesPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 40,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            scalesPanel.Controls.Add(_btnAddScale);
            scalesPanel.Controls.Add(_btnDeleteScale);

            tabScales.Controls.Add(dgvScales);
            tabScales.Controls.Add(scalesPanel);
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

            _btnAddCamera = new Button
            {
                Text = "Добавить камеру",
                Height = 35
            };
            _btnAddCamera.Click += OnAddCameraClicked;

            _btnDeleteCamera = new Button
            {
                Text = "Удалить камеру",
                Height = 35
            };
            _btnDeleteCamera.Click += OnDeleteCameraClicked;

            _btnEditBindings = new Button
            {
                Text = "Привязки…",
                Height = 35
            };
            _btnEditBindings.Click += OnEditBindingsClicked;

            var camerasPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 40,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            camerasPanel.Controls.Add(_btnAddCamera);
            camerasPanel.Controls.Add(_btnDeleteCamera);
            camerasPanel.Controls.Add(_btnEditBindings);

            tabCameras.Controls.Add(dgvCameras);
            tabCameras.Controls.Add(camerasPanel);
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

            var now = DateTime.UtcNow;

            foreach (var scale in _scaleManager.Scales)
            {
                string name = scale.Name;
                string ipPort = $"{scale.Ip}:{scale.Port}";
                string protocol = scale.Protocol.ToString();
                string netKg = (scale.State.NetGrams / 1000.0).ToString("F3");
                string tareKg = (scale.State.TareGrams / 1000.0).ToString("F3");
                string stable = scale.State.Stable ? "Да" : "Нет";
                var age = now - scale.State.LastUpdateUtc;
                bool online = scale.State.IsOnline(_offlineThreshold);
                string onlineText = online ? "Да" : "Нет";
                string statusText = online
                    ? $"Online {age:hh\\:mm\\:ss}"
                    : $"Offline {age:hh\\:mm\\:ss}";

                dgvScales.Rows.Add(name, ipPort, protocol, netKg, tareKg, stable, onlineText, statusText);
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

        private void OnAddScaleClicked(object? sender, EventArgs e)
        {
            using (var dlg = new ScaleEditForm())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var scale = dlg.Scale;
                    _scaleManager.Scales.Add(scale);
                    RecreateScaleClient();
                    RecreateCameraOsdService();
                    RefreshScalesGrid();
                    RefreshCamerasGrid();
                }
            }
        }

        private void OnAddCameraClicked(object? sender, EventArgs e)
        {
            using (var dlg = new CameraEditForm())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var cam = dlg.Camera;

                    int overlayId = 1;
                    foreach (var scale in _scaleManager.Scales)
                    {
                        cam.Bindings.Add(new CameraScaleBinding
                        {
                            Camera = cam,
                            Scale = scale,
                            OverlayId = overlayId++,
                            AutoPosition = true,
                            Enabled = true
                        });
                    }

                    _cameraManager.Cameras.Add(cam);
                    RecreateCameraOsdService();
                    RefreshCamerasGrid();
                }
            }
        }

        private void OnEditBindingsClicked(object? sender, EventArgs e)
        {
            if (dgvCameras.CurrentRow == null) return;
            int rowIndex = dgvCameras.CurrentRow.Index;
            if (rowIndex < 0 || rowIndex >= _cameraManager.Cameras.Count) return;

            var cam = _cameraManager.Cameras[rowIndex];

            using (var dlg = new CameraBindingsForm(cam, _scaleManager))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    RecreateCameraOsdService();
                    RefreshCamerasGrid();
                }
            }
        }

        private void OnDeleteScaleClicked(object? sender, EventArgs e)
        {
            if (dgvScales.CurrentRow == null) return;
            int rowIndex = dgvScales.CurrentRow.Index;
            if (rowIndex < 0 || rowIndex >= _scaleManager.Scales.Count) return;

            var scale = _scaleManager.Scales[rowIndex];

            var result = MessageBox.Show(
                $"Удалить весы \"{scale.Name}\"?",
                "Подтверждение удаления",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            foreach (var cam in _cameraManager.Cameras)
            {
                for (int i = cam.Bindings.Count - 1; i >= 0; i--)
                {
                    if (cam.Bindings[i].Scale != null && cam.Bindings[i].Scale.Id == scale.Id)
                        cam.Bindings.RemoveAt(i);
                }
            }

            _scaleManager.Scales.Remove(scale);

            RecreateScaleClient();
            RecreateCameraOsdService();

            RefreshScalesGrid();
            RefreshCamerasGrid();
        }

        private void OnDeleteCameraClicked(object? sender, EventArgs e)
        {
            if (dgvCameras.CurrentRow == null) return;
            int rowIndex = dgvCameras.CurrentRow.Index;
            if (rowIndex < 0 || rowIndex >= _cameraManager.Cameras.Count) return;

            var cam = _cameraManager.Cameras[rowIndex];

            var result = MessageBox.Show(
                $"Удалить камеру \"{cam.Name}\"?",
                "Подтверждение удаления",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            _cameraManager.Cameras.Remove(cam);

            RecreateCameraOsdService();
            RefreshCamerasGrid();
        }

        private void AppendLog(string message)
        {
            BeginInvoke(new Action(() =>
            {
                txtLog.AppendText(message + Environment.NewLine);
            }));
        }

        private void RecreateScaleClient()
        {
            _massaClient?.StopAsync().GetAwaiter().GetResult();

            _massaClient = new MassaKClient(
                _scaleManager.Scales,
                pollInterval: _pollInterval,
                connectTimeout: _connectTimeout,
                offlineThreshold: _offlineThreshold,
                reconnectDelay: _reconnectDelay);

            _massaClient.LogMessage += AppendLog;
            _massaClient.ScaleUpdated += OnScaleUpdated;
            _massaClient.Start();
        }

        private void RecreateCameraOsdService()
        {
            _cameraOsdService?.StopAsync().GetAwaiter().GetResult();

            _cameraOsdService = new CameraOsdService(
                _cameraManager.Cameras,
                _scaleManager,
                TimeSpan.FromMilliseconds(100));

            _cameraOsdService.LogMessage += AppendLog;

            _cameraOsdService.Start();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            _uiTimer.Stop();
            _massaClient?.StopAsync().GetAwaiter().GetResult();
            _cameraOsdService?.StopAsync().GetAwaiter().GetResult();
        }
    }
}
