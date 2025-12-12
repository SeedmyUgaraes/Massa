using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using MassaKWin.Core;
using Timer = System.Windows.Forms.Timer;

namespace MassaKWin
{
    public class MainForm : Form
    {
        private readonly ScaleManager _scaleManager;
        private readonly WeightHistoryManager _historyManager;
        private readonly ConfigStorage _configStorage;
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
        private Button _btnAutoDiscoverScales = null!;
        private Button _btnAddCamera;
        private Button _btnDeleteCamera;
        private Button _btnEditBindings;
        private async void OnAddScaleClicked(object? sender, EventArgs e)
        {
            // Окно добавления/редактирования весов
            using (var dlg = new ScaleEditForm())
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;

                var scale = dlg.Scale;
                _scaleManager.Scales.Add(scale);
            }

            // Сохраняем конфигурацию
            SaveConfig();

            // Асинхронно пересоздаём клиента весов и OSD-сервис камер
            await RecreateScaleClientAsync();
            await RecreateCameraOsdServiceAsync();

            // Обновляем таблицы
            RefreshScalesGrid();
            RefreshCamerasGrid();
        }

        private async void OnAutoDiscoverScalesClicked(object? sender, EventArgs e)
        {
            using (var dlg = new ScaleDiscoveryForm(_scaleManager))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    SaveConfig();
                    await RecreateScaleClientAsync();
                    await RecreateCameraOsdServiceAsync();
                    RefreshScalesGrid();
                    RefreshCamerasGrid();
                }
            }
        }

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

            _configStorage = new ConfigStorage();

            _ = LoadConfigAsync();

            _uiTimer = new Timer
            {
                Interval = 500
            };
            _uiTimer.Tick += UiTimerOnTick;
            _uiTimer.Start();
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

            dgvScales.CellDoubleClick += DgvScales_CellDoubleClick;

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

            _btnAutoDiscoverScales = new Button
            {
                Text = "Автопоиск",
                Height = 35
            };
            _btnAutoDiscoverScales.Click += OnAutoDiscoverScalesClicked;

            var scalesPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 40,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            scalesPanel.Controls.Add(_btnAddScale);
            scalesPanel.Controls.Add(_btnDeleteScale);
            scalesPanel.Controls.Add(_btnAutoDiscoverScales);

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
            if (InvokeRequired)
            {
                BeginInvoke(new Action(RefreshScalesGrid));
                return;
            }

            if (_scaleManager == null)
                return;

            // 1. Запоминаем, какие весы были выбраны
            Guid? selectedScaleId = null;

            if (dgvScales.CurrentRow != null)
            {
                // сначала пробуем взять Scale из Tag
                if (dgvScales.CurrentRow.Tag is Scale tagScale)
                {
                    selectedScaleId = tagScale.Id;
                }
                else
                {
                    int rowIndex = dgvScales.CurrentRow.Index;
                    if (rowIndex >= 0 && rowIndex < _scaleManager.Scales.Count)
                        selectedScaleId = _scaleManager.Scales[rowIndex].Id;
                }
            }

            dgvScales.SuspendLayout();
            try
            {
                dgvScales.Rows.Clear();

                foreach (var scale in _scaleManager.Scales)
                {
                    var state = scale.State;

                    // граммы -> кг
                    var netKg = state.NetGrams / 1000.0;
                    var tareKg = state.TareGrams / 1000.0;

                    var now = DateTime.UtcNow;

                    // онлайн/оффлайн по порогу
                    bool online = state.IsOnline(_offlineThreshold);

                    // обновляем "момент начала" текущего статуса
                    state.UpdateStatus(online);

                    // сколько времени в текущем статусе
                    var statusAge = now - state.StatusSinceUtc;
                    if (statusAge < TimeSpan.Zero)
                        statusAge = TimeSpan.Zero;

                    string statusText = online
                        ? $"Online {statusAge:hh\\:mm\\:ss}"
                        : $"Offline {statusAge:hh\\:mm\\:ss}";

                    // добавляем строку
                    int rowIndex = dgvScales.Rows.Add(
                        scale.Name,                         // Имя весов
                        $"{scale.Ip}:{scale.Port}",         // IP:Port
                        scale.Protocol.ToString(),          // Протокол
                        netKg.ToString("F3"),               // Net, кг
                        tareKg.ToString("F3"),              // Tare, кг
                        state.Stable ? "Да" : "Нет",        // Stable
                        online ? "Да" : "Нет",              // Online
                        statusText                          // Статус + время
                    );

                    // сохраняем ссылку на объект веса в Tag
                    dgvScales.Rows[rowIndex].Tag = scale;
                }
            }
            finally
            {
                dgvScales.ResumeLayout();
            }

            // 2. Восстанавливаем выбор тех же весов

            if (selectedScaleId.HasValue)
            {
                for (int i = 0; i < _scaleManager.Scales.Count; i++)
                {
                    if (_scaleManager.Scales[i].Id == selectedScaleId.Value)
                    {
                        var row = dgvScales.Rows[i];
                        row.Selected = true;
                        dgvScales.CurrentCell = row.Cells[0];
                        dgvScales.FirstDisplayedScrollingRowIndex = Math.Max(0, i);
                        return;
                    }
                }
            }

            // Если до этого ничего не было выбрано, а строки есть — выделяем первую
            if (dgvScales.Rows.Count > 0 && dgvScales.CurrentRow == null)
            {
                var row = dgvScales.Rows[0];
                row.Selected = true;
                dgvScales.CurrentCell = row.Cells[0];
                dgvScales.FirstDisplayedScrollingRowIndex = 0;
            }
        }

        private void DgvScales_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _scaleManager.Scales.Count)
                return;

            var scale = _scaleManager.Scales[e.RowIndex];

            var trend = new WeightTrendForm(scale, _historyManager);
            trend.Show(this);
        }

        private void OnScaleUpdated(Scale scale)
        {
            _historyManager.AddSample(scale);
            BeginInvoke(new Action(RefreshScalesGrid));
        }

        private void RefreshCamerasGrid()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(RefreshCamerasGrid));
                return;
            }

            if (_cameraManager == null)
                return;

            // 1. Запоминаем, какая камера была выбрана
            Guid? selectedCameraId = null;

            if (dgvCameras.CurrentRow != null)
            {
                // сначала пробуем взять из Tag
                if (dgvCameras.CurrentRow.Tag is Camera tagCamera)
                {
                    selectedCameraId = tagCamera.Id;
                }
                else
                {
                    int rowIndex = dgvCameras.CurrentRow.Index;
                    if (rowIndex >= 0 && rowIndex < _cameraManager.Cameras.Count)
                        selectedCameraId = _cameraManager.Cameras[rowIndex].Id;
                }
            }

            dgvCameras.SuspendLayout();
            try
            {
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

                    int rowIndex = dgvCameras.Rows.Add(
                        cam.Name,
                        ipPort,
                        bindingsCount,
                        status ?? string.Empty);

                    dgvCameras.Rows[rowIndex].Tag = cam;
                }
            }
            finally
            {
                dgvCameras.ResumeLayout();
            }

            // 2. Восстанавливаем выбор той же камеры

            if (selectedCameraId.HasValue)
            {
                for (int i = 0; i < _cameraManager.Cameras.Count; i++)
                {
                    if (_cameraManager.Cameras[i].Id == selectedCameraId.Value)
                    {
                        var row = dgvCameras.Rows[i];
                        row.Selected = true;
                        dgvCameras.CurrentCell = row.Cells[0];
                        dgvCameras.FirstDisplayedScrollingRowIndex = Math.Max(0, i);
                        return;
                    }
                }
            }

            // Если камеры есть, но выбор не восстановили (например, раньше ничего не было выбрано) —
            // только тогда выделяем первую строку.
            if (dgvCameras.Rows.Count > 0 && dgvCameras.CurrentRow == null)
            {
                var row = dgvCameras.Rows[0];
                row.Selected = true;
                dgvCameras.CurrentCell = row.Cells[0];
                dgvCameras.FirstDisplayedScrollingRowIndex = 0;
            }
        }


        private async void OnAddCameraClicked(object? sender, EventArgs e)
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
                    SaveConfig();
                    await RecreateCameraOsdServiceAsync();
                    RefreshCamerasGrid();
                }
            }
        }

        private async void OnEditBindingsClicked(object? sender, EventArgs e)
        {
            if (dgvCameras.CurrentRow == null) return;
            int rowIndex = dgvCameras.CurrentRow.Index;
            if (rowIndex < 0 || rowIndex >= _cameraManager.Cameras.Count) return;

            var cam = _cameraManager.Cameras[rowIndex];

            using (var dlg = new CameraBindingsForm(cam, _scaleManager))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    SaveConfig();
                    await RecreateCameraOsdServiceAsync();
                    RefreshCamerasGrid();
                }
            }
        }

        private async void OnDeleteScaleClicked(object? sender, EventArgs e)
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

            SaveConfig();
            await RecreateScaleClientAsync();
            await RecreateCameraOsdServiceAsync();

            RefreshScalesGrid();
            RefreshCamerasGrid();
        }

        private async void OnDeleteCameraClicked(object? sender, EventArgs e)
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

            SaveConfig();
            await RecreateCameraOsdServiceAsync();
            RefreshCamerasGrid();
        }

        private async Task LoadConfigAsync()
        {
            try
            {
                var config = _configStorage.Load();
                _configStorage.ApplyToManagers(config, _scaleManager, _cameraManager);

                await RecreateScaleClientAsync();
                await RecreateCameraOsdServiceAsync();

                RefreshScalesGrid();
                RefreshCamerasGrid();
            }
            catch (Exception ex)
            {
                AppendLog($"[{DateTime.Now:HH:mm:ss}] Ошибка загрузки конфигурации: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void SaveConfig()
        {
            try
            {
                var config = _configStorage.CreateFromManagers(_scaleManager, _cameraManager);
                _configStorage.Save(config);
            }
            catch (Exception ex)
            {
                AppendLog($"[{DateTime.Now:HH:mm:ss}] Ошибка сохранения конфигурации: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void AppendLog(string message)
        {
            BeginInvoke(new Action(() =>
            {
                txtLog.AppendText(message + Environment.NewLine);
            }));
        }

        private async Task RecreateScaleClientAsync()
        {
            if (_massaClient != null)
            {
                try
                {
                    await _massaClient.StopAsync();
                }
                catch
                {
                }
            }

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

        private async Task RecreateCameraOsdServiceAsync()
        {
            if (_cameraOsdService != null)
            {
                try
                {
                    await _cameraOsdService.StopAsync();
                }
                catch
                {
                }
            }

            _cameraOsdService = new CameraOsdService(
                _cameraManager.Cameras,
                _scaleManager,
                TimeSpan.FromMilliseconds(100));

            _cameraOsdService.LogMessage += AppendLog;

            _cameraOsdService.Start();
        }

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            _uiTimer.Stop();
            if (_massaClient != null)
                await _massaClient.StopAsync();

            if (_cameraOsdService != null)
                await _cameraOsdService.StopAsync();

            SaveConfig();
        }
    }
}
