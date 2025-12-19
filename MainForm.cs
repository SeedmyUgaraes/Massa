using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Krypton.Toolkit;
using MassaKWin.Core;
using Timer = System.Windows.Forms.Timer;
using MassaKWin.Ui;
using ThemeManager = MassaKWin.Ui.ThemeManager;

namespace MassaKWin
{
    public class MainForm : KryptonForm
    {
        private readonly ScaleManager _scaleManager;
        private readonly WeightHistoryManager _historyManager;
        private readonly ConfigStorage _configStorage;
        private GlobalSettings _settings = new();
        private MassaKClient? _massaClient;
        private CameraManager _cameraManager;
        private CameraOsdService? _cameraOsdService;
        private readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(200);
        private readonly TimeSpan _offlineThreshold = TimeSpan.FromSeconds(10);
        private readonly TimeSpan _reconnectDelay = TimeSpan.FromSeconds(5);
        private bool _pollingEnabled = true;
        private readonly Dictionary<Guid, bool> _lastOnlineStates = new();
        private readonly Timer _uiTimer;
        private readonly Timer _connectivityTimer;
        private bool? _lastInternetOk;
        private bool _checkingInternet;

        private KryptonSplitContainer _shellSplitContainer = null!;
        private KryptonPanel _sidebarPanel = null!;
        private KryptonPanel _contentHost = null!;
        private FlowLayoutPanel _navButtonsPanel = null!;
        private KryptonLabel _pageTitleLabel = null!;
        private KryptonButton _btnNavScales = null!;
        private KryptonButton _btnNavCameras = null!;
        private KryptonButton _btnNavSettings = null!;
        private KryptonButton _btnNavLog = null!;
        private KryptonButton _btnToggleSidebar = null!;
        private ToolTip _sidebarToolTip = null!;
        private bool _sidebarCollapsed;
        private bool _loadingConfig;

        private Control _pageScales = null!;
        private Control _pageCameras = null!;
        private Control _pageSettings = null!;
        private Control _pageLog = null!;
        private readonly List<NavigationItem> _navItems = new();
        private MainPage _currentPage = MainPage.Scales;

        private const int SidebarExpandedWidth = 220;
        private const int SidebarCollapsedWidth = 56;

        private enum MainPage
        {
            Scales,
            Cameras,
            Settings,
            Log
        }

        private class NavigationItem
        {
            public MainPage Page { get; init; }
            public KryptonButton Button { get; init; } = null!;
            public string Text { get; init; } = string.Empty;
            public Image? Icon { get; init; }
        }

        private DataGridView dgvScales;
        private DataGridView dgvCameras;
        private KryptonRichTextBox txtLog;
        private KryptonButton _btnAddScale;
        private KryptonButton _btnDeleteScale;
        private KryptonButton _btnAutoDiscoverScales = null!;
        private KryptonButton _btnStartPolling = null!;
        private KryptonButton _btnStopPolling = null!;
        private KryptonButton _btnAddCamera;
        private KryptonButton _btnDeleteCamera;
        private KryptonButton _btnEditBindings;
        private KryptonCheckBox _chkStartPollingOnStartup = null!;
        private NumericUpDown _numScaleTimeout = null!;
        private NumericUpDown _numDeadband = null!;
        private KryptonComboBox _cbWeightUnit = null!;
        private NumericUpDown _numWeightDecimals = null!;
        private KryptonCheckBox _chkAutoZeroOnConnect = null!;
        private KryptonTextBox _txtDiscoveryFrom = null!;
        private KryptonTextBox _txtDiscoveryTo = null!;
        private NumericUpDown _numDefaultPort = null!;
        private NumericUpDown _numParallelScan = null!;
        private NumericUpDown _numScanTimeout = null!;
        private KryptonTextBox _txtOverlayTemplate = null!;
        private KryptonTextBox _txtOverlayNoConnection = null!;
        private KryptonTextBox _txtOverlayUnstable = null!;
        private KryptonComboBox _cbOverlayPosition = null!;
        private KryptonTextBox _txtLogDirectory = null!;
        private KryptonButton _btnBrowseLogDirectory = null!;
        private KryptonCheckBox _chkEnableSounds = null!;
        private KryptonButton _btnSaveSettings = null!;
        private ContextMenuStrip _scalesMenu = null!;
        private ContextMenuStrip _camerasMenu = null!;
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
            await RecreateScaleClientAsync(_pollingEnabled);
            await RecreateCameraOsdServiceAsync();

            // Обновляем таблицы
            RefreshScalesGrid();
            RefreshCamerasGrid();
        }

        private async void OnAutoDiscoverScalesClicked(object? sender, EventArgs e)
        {
            using (var dlg = new ScaleDiscoveryForm(_scaleManager, _settings))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    SaveConfig();
                    await RecreateScaleClientAsync(_pollingEnabled);
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

            _loadingConfig = true;

            _scaleManager = new ScaleManager();
            _historyManager = new WeightHistoryManager();
            _scaleManager.OfflineThreshold = _offlineThreshold;

            _cameraManager = new CameraManager();

            _configStorage = new ConfigStorage();

            InitializeComponents();

            _ = LoadConfigAsync();

            _uiTimer = new Timer
            {
                Interval = 500
            };
            _uiTimer.Tick += UiTimerOnTick;
            _uiTimer.Start();

            _connectivityTimer = new Timer
            {
                Interval = 2000
            };
            _connectivityTimer.Tick += ConnectivityTimerOnTick;
            _connectivityTimer.Start();
        }

        private void InitializeComponents()
        {
            KeyPreview = true;
            _sidebarToolTip = new ToolTip();

            _shellSplitContainer = new KryptonSplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                IsSplitterFixed = true,
                Panel1MinSize = SidebarCollapsedWidth,
                SplitterDistance = SidebarExpandedWidth,
                SplitterWidth = 1
            };

            BuildSidebar();
            BuildContentHost();

            Controls.Add(_shellSplitContainer);

            ThemeManager.Apply(this);
            UpdateSidebarVisualState();
            ShowPage(MainPage.Scales);
        }

        private void BuildSidebar()
        {
            _sidebarPanel = new KryptonPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            var sidebarLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1
            };
            sidebarLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            sidebarLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            sidebarLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 48,
                Padding = new Padding(4)
            };
            var headerLabel = new KryptonLabel
            {
                Dock = DockStyle.Fill,
                Values = { Text = "MassaK" }
            };
            headerLabel.StateCommon.ShortText.Font = new Font(Font, FontStyle.Bold);
            headerLabel.StateCommon.ShortText.Color1 = Color.FromArgb(52, 91, 170);
            headerPanel.Controls.Add(headerLabel);

            _navButtonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(0, 8, 0, 8)
            };

            _btnNavScales = CreateNavButton("Весы", MainPage.Scales, SystemIcons.Information.ToBitmap());
            _btnNavCameras = CreateNavButton("Камеры", MainPage.Cameras, SystemIcons.Application.ToBitmap());
            _btnNavSettings = CreateNavButton("Настройки", MainPage.Settings, SystemIcons.Shield.ToBitmap());
            _btnNavLog = CreateNavButton("Лог", MainPage.Log, SystemIcons.Warning.ToBitmap());

            _navButtonsPanel.Controls.AddRange(new Control[]
            {
                _btnNavScales,
                _btnNavCameras,
                _btnNavSettings,
                _btnNavLog
            });

            _btnToggleSidebar = new KryptonButton
            {
                Dock = DockStyle.Fill,
                Height = 40,
                ButtonStyle = ButtonStyle.LowProfile,
                Margin = new Padding(0, 8, 0, 0)
            };
            _btnToggleSidebar.Values.Text = "≡";
            _btnToggleSidebar.Click += (_, _) => ToggleSidebar();

            var togglePanel = new Panel
            {
                Dock = DockStyle.Fill,
                Height = 48,
                Padding = new Padding(0, 8, 0, 0)
            };
            togglePanel.Controls.Add(_btnToggleSidebar);

            sidebarLayout.Controls.Add(headerPanel, 0, 0);
            sidebarLayout.Controls.Add(_navButtonsPanel, 0, 1);
            sidebarLayout.Controls.Add(togglePanel, 0, 2);

            _sidebarPanel.Controls.Add(sidebarLayout);
            _shellSplitContainer.Panel1.Controls.Add(_sidebarPanel);
        }

        private void BuildContentHost()
        {
            _contentHost = new KryptonPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            _pageScales = BuildScalesPage();
            _pageCameras = BuildCamerasPage();
            _pageSettings = BuildSettingsPage();
            _pageLog = BuildLogPage();

            _contentHost.Controls.Add(_pageScales);
            _contentHost.Controls.Add(_pageCameras);
            _contentHost.Controls.Add(_pageSettings);
            _contentHost.Controls.Add(_pageLog);

            _pageTitleLabel = new KryptonLabel
            {
                Dock = DockStyle.Fill,
                Values = { Text = "MassaK" }
            };
            _pageTitleLabel.StateCommon.ShortText.Font = new Font(Font, FontStyle.Bold);
            _pageTitleLabel.StateCommon.ShortText.Color1 = Color.FromArgb(40, 40, 40);

            var titlePanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 38,
                Padding = new Padding(8, 8, 8, 4)
            };
            titlePanel.Controls.Add(_pageTitleLabel);

            var contentLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1
            };
            contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            contentLayout.Controls.Add(titlePanel, 0, 0);
            contentLayout.Controls.Add(_contentHost, 0, 1);

            _shellSplitContainer.Panel2.Controls.Add(contentLayout);
        }

        private KryptonPanel BuildScalesPage()
        {
            var page = new KryptonPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            dgvScales = CreateStyledGrid();
            dgvScales.CellDoubleClick += DgvScales_CellDoubleClick;
            dgvScales.CellMouseDown += DgvScales_CellMouseDown;
            dgvScales.KeyDown += DgvScales_KeyDown;

            dgvScales.Columns.Add("Name", "Имя");
            dgvScales.Columns.Add("IpPort", "IP:Port");
            dgvScales.Columns.Add("Protocol", "Протокол");
            dgvScales.Columns.Add("Net", "Net, кг");
            dgvScales.Columns.Add("Tare", "Tare, кг");
            dgvScales.Columns.Add("Stable", "Stable");
            dgvScales.Columns.Add("Online", "Online");
            dgvScales.Columns.Add("Status", "Статус");

            _scalesMenu = new ContextMenuStrip();
            var renameScaleItem = new ToolStripMenuItem("Переименовать");
            renameScaleItem.Click += (_, _) => RenameSelectedScale();
            _scalesMenu.Items.Add(renameScaleItem);
            dgvScales.ContextMenuStrip = _scalesMenu;

            _btnAddScale = CreateActionButton("Добавить весы", OnAddScaleClicked);
            _btnDeleteScale = CreateActionButton("Удалить весы", OnDeleteScaleClicked);
            _btnAutoDiscoverScales = CreateActionButton("Автопоиск", OnAutoDiscoverScalesClicked);

            var scalesPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 52,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(8, 8, 8, 4),
                BackColor = Color.FromArgb(245, 247, 250)
            };
            scalesPanel.Controls.AddRange(new Control[]
            {
                _btnAddScale, _btnDeleteScale, _btnAutoDiscoverScales
            });

            page.Controls.Add(dgvScales);
            page.Controls.Add(scalesPanel);
            return page;
        }

        private KryptonPanel BuildCamerasPage()
        {
            var page = new KryptonPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            dgvCameras = CreateStyledGrid();
            dgvCameras.CellMouseDown += DgvCameras_CellMouseDown;
            dgvCameras.KeyDown += DgvCameras_KeyDown;

            dgvCameras.Columns.Add("Name", "Имя камеры");
            dgvCameras.Columns.Add("IpPort", "IP:Port");
            dgvCameras.Columns.Add("Bindings", "Привязок весов");
            dgvCameras.Columns.Add("OsdStatus", "Статус OSD");

            _camerasMenu = new ContextMenuStrip();
            var renameCameraItem = new ToolStripMenuItem("Переименовать");
            renameCameraItem.Click += (_, _) => RenameSelectedCamera();
            _camerasMenu.Items.Add(renameCameraItem);
            dgvCameras.ContextMenuStrip = _camerasMenu;

            _btnAddCamera = CreateActionButton("Добавить камеру", OnAddCameraClicked);
            _btnDeleteCamera = CreateActionButton("Удалить камеру", OnDeleteCameraClicked);
            _btnEditBindings = CreateActionButton("Привязки…", OnEditBindingsClicked);

            var camerasPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 52,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(8, 8, 8, 4),
                BackColor = Color.FromArgb(245, 247, 250)
            };
            camerasPanel.Controls.AddRange(new Control[]
            {
                _btnAddCamera, _btnDeleteCamera, _btnEditBindings
            });

            page.Controls.Add(dgvCameras);
            page.Controls.Add(camerasPanel);
            return page;
        }

        private KryptonPanel BuildSettingsPage()
        {
            var page = new KryptonPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            var scrollPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Padding = new Padding(8)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            layout.Controls.Add(CreatePollingGroup(), 0, 0);
            layout.Controls.Add(CreateDiscoveryGroup(), 0, 1);
            layout.Controls.Add(CreateOverlayGroup(), 0, 2);
            layout.Controls.Add(CreateLoggingGroup(), 0, 3);
            layout.Controls.Add(CreateNotificationsGroup(), 0, 4);

            var bottomPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(0, 8, 0, 0)
            };

            _btnSaveSettings = new KryptonButton
            {
                Text = "Сохранить настройки",
                AutoSize = true,
                MinimumSize = new Size(180, 34)
            };
            _btnSaveSettings.Click += async (_, _) => await SaveSettingsAsync();

            bottomPanel.Controls.Add(_btnSaveSettings);

            layout.Controls.Add(bottomPanel, 0, 5);

            scrollPanel.Controls.Add(layout);
            page.Controls.Add(scrollPanel);
            return page;
        }

        private KryptonPanel BuildLogPage()
        {
            var page = new KryptonPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8)
            };

            txtLog = new KryptonRichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                HideSelection = false,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                StateCommon =
                {
                    Border = { DrawBorders = PaletteDrawBorders.All, Rounding = 6 }
                }
            };
            page.Controls.Add(txtLog);
            return page;
        }

        private KryptonButton CreateNavButton(string text, MainPage page, Image? icon)
        {
            var button = new KryptonButton
            {
                Width = SidebarExpandedWidth - 16,
                Height = 44,
                ButtonStyle = ButtonStyle.LowProfile,
                Margin = new Padding(0, 0, 0, 8),
                Tag = page
            };

            button.Values.Text = text;
            // Читаемый цвет текста для навигации
            var navText = Color.FromArgb(40, 40, 40);
            var navTextDisabled = Color.FromArgb(150, 150, 150);

            button.StateCommon.Content.ShortText.Color1 = navText;
            button.StateCommon.Content.ShortText.Color2 = navText;

            button.StateNormal.Content.ShortText.Color1 = navText;
            button.StateNormal.Content.ShortText.Color2 = navText;

            button.StateTracking.Content.ShortText.Color1 = navText;
            button.StateTracking.Content.ShortText.Color2 = navText;

            button.StatePressed.Content.ShortText.Color1 = navText;
            button.StatePressed.Content.ShortText.Color2 = navText;

            button.StateDisabled.Content.ShortText.Color1 = navTextDisabled;
            button.StateDisabled.Content.ShortText.Color2 = navTextDisabled;
            button.Values.Image = icon;
            button.StateCommon.Content.ShortText.TextH = PaletteRelativeAlign.Near;
            button.StateCommon.Content.Padding = new Padding(12, 8, 12, 8);
            button.Click += (_, _) => ShowPage(page);

            _navItems.Add(new NavigationItem
            {
                Page = page,
                Button = button,
                Text = text,
                Icon = icon
            });

            ApplyNavButtonState(button, false);
            return button;
        }

        private void ApplyNavButtonState(KryptonButton button, bool selected)
        {
            var background = selected ? Color.FromArgb(227, 235, 250) : Color.Transparent;
            var border = selected ? Color.FromArgb(95, 134, 238) : Color.Transparent;

            button.StateCommon.Back.Color1 = background;
            button.StateCommon.Back.Color2 = background;
            button.StateCommon.Border.DrawBorders = PaletteDrawBorders.All;
            button.StateCommon.Border.Rounding = 8;
            button.StateCommon.Border.Width = selected ? 1 : 0;
            button.StateCommon.Border.Color1 = border;
            button.StateCommon.Border.Color2 = border;
            button.StateCommon.Content.ShortText.Font = selected ? new Font(Font, FontStyle.Bold) : Font;
            button.StateTracking.Back.Color1 = Color.FromArgb(235, 240, 248);
            button.StateTracking.Back.Color2 = button.StateTracking.Back.Color1;
            button.StateTracking.Border.DrawBorders = PaletteDrawBorders.All;
            button.StateTracking.Border.Rounding = 8;
        }

        private void UpdateSidebarVisualState()
        {
            if (_shellSplitContainer != null)
                _shellSplitContainer.SplitterDistance = _sidebarCollapsed ? SidebarCollapsedWidth : SidebarExpandedWidth;

            foreach (var item in _navItems)
            {
                item.Button.Values.Text = _sidebarCollapsed ? string.Empty : item.Text;
                item.Button.Width = (_sidebarCollapsed ? SidebarCollapsedWidth : SidebarExpandedWidth) - 16;
                item.Button.StateCommon.Content.Padding = _sidebarCollapsed ? new Padding(8) : new Padding(12, 8, 12, 8);
                _sidebarToolTip.SetToolTip(item.Button, _sidebarCollapsed ? item.Text : string.Empty);
                ApplyNavButtonState(item.Button, item.Page == _currentPage);
            }

            _btnToggleSidebar.Values.Text = _sidebarCollapsed ? "≡" : "≡ Свернуть меню";
            _sidebarToolTip.SetToolTip(_btnToggleSidebar, _sidebarCollapsed ? "Развернуть меню" : "Свернуть меню");
        }

        private void ToggleSidebar()
        {
            _sidebarCollapsed = !_sidebarCollapsed;
            _settings.SidebarCollapsed = _sidebarCollapsed;
            UpdateSidebarVisualState();
            if (!_loadingConfig)
                SaveConfig();
        }

        private MainPage ParsePageFromSettings(string? pageName)
        {
            if (Enum.TryParse(pageName, out MainPage page))
                return page;

            return MainPage.Scales;
        }

        private void ShowPage(MainPage page)
        {
            _currentPage = page;

            foreach (Control control in _contentHost.Controls)
                control.Visible = false;

            var target = page switch
            {
                MainPage.Scales => _pageScales,
                MainPage.Cameras => _pageCameras,
                MainPage.Settings => _pageSettings,
                MainPage.Log => _pageLog,
                _ => _pageScales
            };

            if (target != null)
            {
                target.Visible = true;
                target.BringToFront();
            }

            foreach (var item in _navItems)
            {
                ApplyNavButtonState(item.Button, item.Page == page);
            }

            var pageTitle = page switch
            {
                MainPage.Scales => "Весы",
                MainPage.Cameras => "Камеры",
                MainPage.Settings => "Настройки",
                MainPage.Log => "Лог",
                _ => "MassaK"
            };

            _pageTitleLabel.Values.Text = pageTitle;
            Text = $"MassaK — {pageTitle}";

            _settings.LastPage = page.ToString();
            if (!_loadingConfig)
                SaveConfig();
        }

        private void ApplyNavigationPreferences()
        {
            _sidebarCollapsed = _settings.SidebarCollapsed;
            UpdateSidebarVisualState();
            ShowPage(ParsePageFromSettings(_settings.LastPage));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.D1:
                    ShowPage(MainPage.Scales);
                    return true;
                case Keys.Control | Keys.D2:
                    ShowPage(MainPage.Cameras);
                    return true;
                case Keys.Control | Keys.D3:
                    ShowPage(MainPage.Settings);
                    return true;
                case Keys.Control | Keys.D4:
                    ShowPage(MainPage.Log);
                    return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private KryptonButton CreateActionButton(string text, EventHandler onClick)
        {
            var button = new KryptonButton
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(140, 32),
                Margin = new Padding(0, 0, 8, 0)
            };
            button.Click += onClick;
            return button;
        }

        private DataGridView CreateStyledGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersHeight = 34
            };

            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(245, 247, 250);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(50, 50, 50);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(227, 235, 250);
            grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            grid.DefaultCellStyle.Font = Font;
            grid.RowTemplate.Height = 30;
            grid.GridColor = Color.FromArgb(230, 234, 240);
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 251, 255);

            EnableDoubleBuffering(grid);
            return grid;
        }

        private static void EnableDoubleBuffering(DataGridView grid)
        {
            var property = typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            property?.SetValue(grid, true, null);
        }

        private void ApplyScaleRowStyle(DataGridViewRow row, bool online, bool stable)
        {
            var onlineColor = online ? Color.FromArgb(0, 128, 96) : Color.FromArgb(204, 52, 44);
            var stableColor = stable ? Color.FromArgb(0, 128, 0) : Color.FromArgb(205, 92, 0);
            row.DefaultCellStyle.BackColor = online ? Color.White : Color.FromArgb(255, 245, 245);
            row.DefaultCellStyle.ForeColor = online ? Color.FromArgb(40, 40, 40) : Color.FromArgb(160, 40, 40);

            if (row.Cells["Online"] is DataGridViewCell onlineCell)
                onlineCell.Style.ForeColor = onlineColor;

            if (row.Cells["Stable"] is DataGridViewCell stableCell)
                stableCell.Style.ForeColor = stableColor;
        }

        private static TableLayoutPanel CreateTwoColumnLayout()
        {
            var layout = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            return layout;
        }

        private static Label CreateLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 8, 8)
            };
        }

        private static void AddRow(TableLayoutPanel layout, Control label, Control control)
        {
            var rowIndex = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            label.Margin = new Padding(label.Margin.Left, label.Margin.Top, label.Margin.Right, 8);
            control.Margin = new Padding(control.Margin.Left, control.Margin.Top, control.Margin.Right, 8);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            layout.Controls.Add(label, 0, rowIndex);
            layout.Controls.Add(control, 1, rowIndex);
        }

        private GroupBox CreatePollingGroup()
        {
            var group = new GroupBox
            {
                Text = "Опрос весов",
                Dock = DockStyle.Top,
                Padding = new Padding(16),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 16)
            };

            var layout = CreateTwoColumnLayout();

            _chkStartPollingOnStartup = new KryptonCheckBox
            {
                Text = "Стартовать опрос при запуске",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 0, 8)
            };
            AddRow(layout, new Label(), _chkStartPollingOnStartup);

            _numScaleTimeout = new NumericUpDown
            {
                Minimum = 100,
                Maximum = 60000,
                Increment = 100,
                Anchor = AnchorStyles.Left | AnchorStyles.Right
            };
            AddRow(layout, CreateLabel("Таймаут ответа весов, мс"), _numScaleTimeout);

            _numDeadband = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 100000,
                DecimalPlaces = 1,
                Increment = 0.5M,
                Anchor = AnchorStyles.Left | AnchorStyles.Right
            };
            AddRow(layout, CreateLabel("Deadband (|Δweight| ≤ ... г игнорируется)"), _numDeadband);

            _cbWeightUnit = new KryptonComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Anchor = AnchorStyles.Left,
                Width = 120
            };
            _cbWeightUnit.Items.AddRange(new object[] { "kg", "g" });
            AddRow(layout, CreateLabel("Единицы веса по умолчанию"), _cbWeightUnit);

            _numWeightDecimals = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 6,
                Anchor = AnchorStyles.Left | AnchorStyles.Right
            };
            AddRow(layout, CreateLabel("Цифр после запятой"), _numWeightDecimals);

            _chkAutoZeroOnConnect = new KryptonCheckBox
            {
                Text = "Авто-ноль/тара при подключении",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 0, 8)
            };
            AddRow(layout, new Label(), _chkAutoZeroOnConnect);

            _btnStartPolling = CreateActionButton("Запустить опрос", async (_, _) => await StartPollingAsync());
            _btnStopPolling = CreateActionButton("Остановить опрос", async (_, _) => await StopPollingAsync());
            var buttonsPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0, 4, 0, 0)
            };
            buttonsPanel.Controls.Add(_btnStartPolling);
            buttonsPanel.Controls.Add(_btnStopPolling);

            var row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(buttonsPanel, 1, row);

            group.Controls.Add(layout);

            return group;
        }

        private GroupBox CreateDiscoveryGroup()
        {
            var group = new GroupBox
            {
                Text = "Автопоиск",
                Dock = DockStyle.Top,
                Padding = new Padding(16),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 16)
            };

            var layout = CreateTwoColumnLayout();

            _txtDiscoveryFrom = new KryptonTextBox { Width = 140 };
            _txtDiscoveryTo = new KryptonTextBox { Width = 140 };
            var ipRangePanel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 0, 0, 8)
            };
            ipRangePanel.Controls.Add(_txtDiscoveryFrom);
            ipRangePanel.Controls.Add(new Label { Text = "до", AutoSize = true, Padding = new Padding(6, 6, 6, 0) });
            ipRangePanel.Controls.Add(_txtDiscoveryTo);
            AddRow(layout, CreateLabel("Диапазон IP: от ... до ..."), ipRangePanel);

            _numDefaultPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Width = 100 };
            AddRow(layout, CreateLabel("Порт по умолчанию"), _numDefaultPort);

            _numParallelScan = new NumericUpDown { Minimum = 1, Maximum = 64, Width = 100 };
            AddRow(layout, CreateLabel("Параллельных подключений"), _numParallelScan);

            _numScanTimeout = new NumericUpDown { Minimum = 100, Maximum = 60000, Increment = 100, Width = 100 };
            AddRow(layout, CreateLabel("Таймаут на IP, мс"), _numScanTimeout);

            group.Controls.Add(layout);

            return group;
        }

        private GroupBox CreateOverlayGroup()
        {
            var group = new GroupBox
            {
                Text = "Overlay",
                Dock = DockStyle.Top,
                Padding = new Padding(16),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 16)
            };

            var layout = CreateTwoColumnLayout();

            _txtOverlayTemplate = new KryptonTextBox { Width = 420 };
            AddRow(layout, CreateLabel("Шаблон текста (используйте {net}, {tare}, {status}, {unit})"), _txtOverlayTemplate);

            _txtOverlayNoConnection = new KryptonTextBox { Width = 420 };
            AddRow(layout, CreateLabel("Текст при отсутствии подключения"), _txtOverlayNoConnection);

            _txtOverlayUnstable = new KryptonTextBox { Width = 420 };
            AddRow(layout, CreateLabel("Статус при нестабильном весе"), _txtOverlayUnstable);

            _cbOverlayPosition = new KryptonComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
            _cbOverlayPosition.Items.AddRange(new object[]
            {
                "Слева сверху",
                "Справа сверху",
                "Слева снизу",
                "Справа снизу"
            });
            AddRow(layout, CreateLabel("Положение по умолчанию"), _cbOverlayPosition);

            group.Controls.Add(layout);

            return group;
        }

        private GroupBox CreateLoggingGroup()
        {
            var group = new GroupBox
            {
                Text = "Логи",
                Dock = DockStyle.Top,
                Padding = new Padding(16),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 16)
            };

            var layout = CreateTwoColumnLayout();

            _txtLogDirectory = new KryptonTextBox { Width = 420 };
            _btnBrowseLogDirectory = new KryptonButton { Text = "Обзор...", AutoSize = true, MinimumSize = new Size(90, 32), Margin = new Padding(8, 0, 0, 8) };
            _btnBrowseLogDirectory.Click += (_, _) => BrowseLogFolder();

            var directoryPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 0, 0, 8)
            };
            directoryPanel.Controls.Add(_txtLogDirectory);
            directoryPanel.Controls.Add(_btnBrowseLogDirectory);

            AddRow(layout, CreateLabel("Папка для логов"), directoryPanel);

            group.Controls.Add(layout);

            return group;
        }

        private GroupBox CreateNotificationsGroup()
        {
            var group = new GroupBox
            {
                Text = "Уведомления",
                Dock = DockStyle.Top,
                Padding = new Padding(16),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 16)
            };

            var layout = CreateTwoColumnLayout();

            _chkEnableSounds = new KryptonCheckBox
            {
                Text = "Включить звуковые уведомления",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 0, 8)
            };
            AddRow(layout, new Label(), _chkEnableSounds);

            group.Controls.Add(layout);
            return group;
        }

        private void UiTimerOnTick(object sender, EventArgs e)
        {
            RefreshScalesGrid();
            RefreshCamerasGrid();
        }

        private async void ConnectivityTimerOnTick(object? sender, EventArgs e)
        {
            if (_checkingInternet)
                return;

            _checkingInternet = true;
            try
            {
                bool internetOk = await Task.Run(CheckInternetAvailability);
                if (_lastInternetOk != internetOk)
                {
                    _lastInternetOk = internetOk;
                    if (internetOk)
                        LogInfo("Internet RESTORED");
                    else
                        LogWarn("Internet LOST");
                }
            }
            finally
            {
                _checkingInternet = false;
            }
        }

        private bool CheckInternetAvailability()
        {
            try
            {
                if (!NetworkInterface.GetIsNetworkAvailable())
                    return false;

                var dnsTask = Dns.GetHostEntryAsync("cloudflare.com");
                var completedTask = Task.WhenAny(dnsTask, Task.Delay(1000)).GetAwaiter().GetResult();
                if (completedTask != dnsTask)
                    return false;

                dnsTask.GetAwaiter().GetResult();
                return true;
            }
            catch
            {
                return false;
            }
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

                    var netDisplay = WeightFormatter.FormatWeight(state.NetGrams, _settings.DefaultWeightUnit, _settings.WeightDecimalPlaces);
                    var tareDisplay = WeightFormatter.FormatWeight(state.TareGrams, _settings.DefaultWeightUnit, _settings.WeightDecimalPlaces);

                    var now = DateTime.UtcNow;

                    // онлайн/оффлайн по порогу
                    bool online = state.IsOnline(_offlineThreshold);

                    // обновляем "момент начала" текущего статуса
                    var changed = state.UpdateStatus(online);
                    if (changed)
                    {
                        _lastOnlineStates[scale.Id] = online;

                        if (online)
                            LogInfo($"Scale {scale.Name} ({scale.Ip}:{scale.Port}) ONLINE");
                        else
                            LogWarn($"Scale {scale.Name} ({scale.Ip}:{scale.Port}) OFFLINE");

                        if (_settings.EnableSoundNotifications)
                            System.Media.SystemSounds.Exclamation.Play();
                    }
                    else
                    {
                        _lastOnlineStates[scale.Id] = online;
                    }

                    // сколько времени в текущем статусе
                    var statusAge = now - state.StatusSinceUtc;
                    if (statusAge < TimeSpan.Zero)
                        statusAge = TimeSpan.Zero;

                    var durationText = FormatStatusDuration(statusAge);
                    string statusText = online
                        ? $"Online {durationText}"
                        : $"Offline {durationText}";

                    // добавляем строку
                    int rowIndex = dgvScales.Rows.Add(
                        scale.Name,                         // Имя весов
                        $"{scale.Ip}:{scale.Port}",         // IP:Port
                        scale.Protocol.ToString(),          // Протокол
                        netDisplay,                          // Net
                        tareDisplay,                         // Tare
                        state.Stable ? "Да" : "Нет",        // Stable
                        online ? "Да" : "Нет",              // Online
                        statusText                          // Статус + время
                    );

                    // сохраняем ссылку на объект веса в Tag
                    var row = dgvScales.Rows[rowIndex];
                    row.Tag = scale;
                    ApplyScaleRowStyle(row, online, state.Stable);
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

        private string FormatStatusDuration(TimeSpan statusAge)
        {
            if (statusAge.TotalDays >= 1)
            {
                var days = (int)statusAge.TotalDays;
                return $"{days}d {statusAge:hh\\:mm\\:ss}";
            }

            return statusAge.ToString(@"hh\:mm\:ss");
        }

        private void DgvScales_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _scaleManager.Scales.Count)
                return;

            var scale = _scaleManager.Scales[e.RowIndex];

            var trend = new WeightTrendForm(scale, _historyManager);
            trend.Show(this);
        }

        private void DgvScales_CellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                dgvScales.ClearSelection();
                dgvScales.Rows[e.RowIndex].Selected = true;
                dgvScales.CurrentCell = dgvScales.Rows[e.RowIndex].Cells[0];
            }
        }

        private void DgvScales_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F2)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                RenameSelectedScale();
            }
        }

        private void RenameSelectedScale()
        {
            var scale = dgvScales.CurrentRow?.Tag as Scale;
            if (scale == null && dgvScales.CurrentRow != null)
            {
                var rowIndex = dgvScales.CurrentRow.Index;
                if (rowIndex >= 0 && rowIndex < _scaleManager.Scales.Count)
                    scale = _scaleManager.Scales[rowIndex];
            }

            if (scale == null)
                return;

            if (!RenameDialog.TryGetName(this, "Переименование весов", scale.Name, out var newName))
                return;

            if (string.Equals(scale.Name, newName, StringComparison.Ordinal))
                return;

            scale.Name = newName;
            SaveConfig();
            RefreshScalesGrid();
            RefreshCamerasGrid();
        }

        private void OnScaleUpdated(Scale scale)
        {
            _historyManager.AddSample(scale);
            _cameraOsdService?.MarkScaleDirty(scale.Id);

            var online = scale.State.IsOnline(_offlineThreshold);
            var changed = scale.State.UpdateStatus(online);
            if (changed)
            {
                _lastOnlineStates[scale.Id] = online;

                _cameraOsdService?.MarkScaleDirty(scale.Id);

                if (_settings.EnableSoundNotifications)
                {
                    System.Media.SystemSounds.Exclamation.Play();
                }

                if (online)
                    LogInfo($"Scale {scale.Name} ({scale.Ip}:{scale.Port}) ONLINE");
                else
                    LogWarn($"Scale {scale.Name} ({scale.Ip}:{scale.Port}) OFFLINE");
            }
            else
            {
                _lastOnlineStates[scale.Id] = online;
            }
            BeginInvoke(new Action(RefreshScalesGrid));
        }

        private void OnCameraStatusChanged(Guid cameraId, bool isOnline, string? reason)
        {
            var camera = _cameraManager.Cameras.FirstOrDefault(c => c.Id == cameraId);
            var name = camera?.Name ?? cameraId.ToString();
            var endpoint = camera != null ? $"{camera.Ip}:{camera.Port}" : "unknown";

            var statusText = isOnline ? "ONLINE" : "OFFLINE";
            if (!isOnline && !string.IsNullOrWhiteSpace(reason))
            {
                statusText += $" (reason: {reason})";
            }

            if (isOnline)
                LogInfo($"Camera {name} ({endpoint}) {statusText}");
            else
                LogWarn($"Camera {name} ({endpoint}) {statusText}");
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

        private void DgvCameras_CellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                dgvCameras.ClearSelection();
                dgvCameras.Rows[e.RowIndex].Selected = true;
                dgvCameras.CurrentCell = dgvCameras.Rows[e.RowIndex].Cells[0];
            }
        }

        private void DgvCameras_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F2)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                RenameSelectedCamera();
            }
        }

        private void RenameSelectedCamera()
        {
            var cam = dgvCameras.CurrentRow?.Tag as Camera;
            if (cam == null && dgvCameras.CurrentRow != null)
            {
                var rowIndex = dgvCameras.CurrentRow.Index;
                if (rowIndex >= 0 && rowIndex < _cameraManager.Cameras.Count)
                    cam = _cameraManager.Cameras[rowIndex];
            }

            if (cam == null)
                return;

            if (!RenameDialog.TryGetName(this, "Переименование камеры", cam.Name, out var newName))
                return;

            if (string.Equals(cam.Name, newName, StringComparison.Ordinal))
                return;

            cam.Name = newName;
            SaveConfig();
            RefreshCamerasGrid();
            _cameraOsdService?.MarkCameraDirty(cam.Id);
        }


        private async void OnAddCameraClicked(object? sender, EventArgs e)
        {
            using (var dlg = new CameraEditForm())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var cam = dlg.Camera;

                    ApplyOverlayDefaults(cam);
                    cam.Bindings.Clear();

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
            await RecreateScaleClientAsync(_pollingEnabled);
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
            _loadingConfig = true;
            try
            {
                var config = _configStorage.Load();
                _settings = config.Settings ?? new GlobalSettings();
                _pollingEnabled = _settings.StartPollingOnStartup;
                _configStorage.ApplyToManagers(config, _scaleManager, _cameraManager);
                BindSettingsToUi();

                await RecreateScaleClientAsync(_pollingEnabled);
                await RecreateCameraOsdServiceAsync();

                RefreshScalesGrid();
                RefreshCamerasGrid();
                ApplyNavigationPreferences();
            }
            catch (Exception ex)
            {
                AppendLog($"[{DateTime.Now:HH:mm:ss}] Ошибка загрузки конфигурации: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _loadingConfig = false;
            }
        }

        private void SaveConfig()
        {
            try
            {
                var config = _configStorage.CreateFromManagers(_scaleManager, _cameraManager, _settings);
                _configStorage.Save(config);
            }
            catch (Exception ex)
            {
                AppendLog($"[{DateTime.Now:HH:mm:ss}] Ошибка сохранения конфигурации: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private void BindSettingsToUi()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(BindSettingsToUi));
                return;
            }

            _chkStartPollingOnStartup.Checked = _settings.StartPollingOnStartup;
            _numScaleTimeout.Value = Math.Max(_numScaleTimeout.Minimum, Math.Min(_numScaleTimeout.Maximum, _settings.ScaleResponseTimeoutMs));
            _numDeadband.Value = (decimal)_settings.WeightDeadband;
            _cbWeightUnit.SelectedIndex = _settings.DefaultWeightUnit == WeightUnit.Kg ? 0 : 1;
            _numWeightDecimals.Value = Math.Max(_numWeightDecimals.Minimum, Math.Min(_numWeightDecimals.Maximum, _settings.WeightDecimalPlaces));
            _chkAutoZeroOnConnect.Checked = _settings.AutoZeroOnConnect;
            _txtDiscoveryFrom.Text = _settings.AutoDiscoveryIpStart;
            _txtDiscoveryTo.Text = _settings.AutoDiscoveryIpEnd;
            _numDefaultPort.Value = Math.Max(_numDefaultPort.Minimum, Math.Min(_numDefaultPort.Maximum, _settings.DefaultScalePort));
            _numParallelScan.Value = Math.Max(_numParallelScan.Minimum, Math.Min(_numParallelScan.Maximum, _settings.ScanParallelConnections));
            _numScanTimeout.Value = Math.Max(_numScanTimeout.Minimum, Math.Min(_numScanTimeout.Maximum, _settings.ScanIpTimeoutMs));
            _txtOverlayTemplate.Text = _settings.OverlayTextTemplate;
            _txtOverlayNoConnection.Text = _settings.OverlayNoConnectionText;
            _txtOverlayUnstable.Text = _settings.OverlayUnstableText;
            _cbOverlayPosition.SelectedIndex = (int)_settings.OverlayDefaultPosition;
            _txtLogDirectory.Text = _settings.LogDirectory;
            _chkEnableSounds.Checked = _settings.EnableSoundNotifications;
        }

        private void ReadSettingsFromUi()
        {
            _settings.StartPollingOnStartup = _chkStartPollingOnStartup.Checked;
            _settings.ScaleResponseTimeoutMs = (int)_numScaleTimeout.Value;
            _settings.WeightDeadband = (double)_numDeadband.Value;
            _settings.DefaultWeightUnit = _cbWeightUnit.SelectedIndex == 0 ? WeightUnit.Kg : WeightUnit.Gram;
            _settings.WeightDecimalPlaces = (int)_numWeightDecimals.Value;
            _settings.AutoZeroOnConnect = _chkAutoZeroOnConnect.Checked;
            _settings.AutoDiscoveryIpStart = _txtDiscoveryFrom.Text;
            _settings.AutoDiscoveryIpEnd = _txtDiscoveryTo.Text;
            _settings.DefaultScalePort = (int)_numDefaultPort.Value;
            _settings.ScanParallelConnections = (int)_numParallelScan.Value;
            _settings.ScanIpTimeoutMs = (int)_numScanTimeout.Value;
            _settings.OverlayTextTemplate = _txtOverlayTemplate.Text;
            _settings.OverlayNoConnectionText = _txtOverlayNoConnection.Text;
            _settings.OverlayUnstableText = _txtOverlayUnstable.Text;
            _settings.OverlayDefaultPosition = (OverlayPosition)_cbOverlayPosition.SelectedIndex;
            _settings.LogDirectory = _txtLogDirectory.Text;
            _settings.EnableSoundNotifications = _chkEnableSounds.Checked;
        }

        private async Task SaveSettingsAsync()
        {
            ReadSettingsFromUi();
            _pollingEnabled = _settings.StartPollingOnStartup;
            SaveConfig();
            if (_pollingEnabled)
                await RecreateScaleClientAsync(true);
            else
                await StopPollingAsync();
            await RecreateCameraOsdServiceAsync();
            RefreshScalesGrid();
            RefreshCamerasGrid();
        }

        private void BrowseLogFolder()
        {
            using var dlg = new FolderBrowserDialog();
            dlg.SelectedPath = Directory.Exists(_txtLogDirectory.Text)
                ? _txtLogDirectory.Text
                : AppContext.BaseDirectory;

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _txtLogDirectory.Text = dlg.SelectedPath;
            }
        }

        private async Task StartPollingAsync()
        {
            _pollingEnabled = true;
            await RecreateScaleClientAsync(true);
        }

        private async Task StopPollingAsync()
        {
            _pollingEnabled = false;
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
        }

        private void LogInfo(string message)
        {
            AppendLog($"[{DateTime.Now:HH:mm:ss}] INFO {message}");
        }

        private void LogWarn(string message)
        {
            AppendLog($"[{DateTime.Now:HH:mm:ss}] WARN {message}");
        }

        private void LogError(string message)
        {
            AppendLog($"[{DateTime.Now:HH:mm:ss}] ERROR {message}");
        }

        private void AppendLog(string message)
        {
            BeginInvoke(new Action(() =>
            {
                var color = GetLogColor(message);
                var isAtBottom = txtLog.SelectionStart >= txtLog.TextLength - 2;
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.SelectionLength = 0;
                txtLog.SelectionColor = color;
                txtLog.AppendText(message + Environment.NewLine);
                txtLog.SelectionColor = txtLog.StateCommon.Content.Color1;
                if (isAtBottom)
                {
                    txtLog.ScrollToCaret();
                }
            }));

            TryWriteLogToFile(message);
        }

        private Color GetLogColor(string message)
        {
            if (message.Contains("ERROR"))
                return Color.FromArgb(204, 52, 44);
            if (message.Contains("WARN"))
                return Color.FromArgb(205, 128, 40);
            return Color.FromArgb(52, 91, 170);
        }

        private async Task RecreateScaleClientAsync(bool startImmediately)
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
                connectTimeout: TimeSpan.FromMilliseconds(_settings.ScaleResponseTimeoutMs),
                offlineThreshold: _offlineThreshold,
                reconnectDelay: _reconnectDelay,
                deadbandGrams: _settings.WeightDeadband,
                autoZeroOnConnect: _settings.AutoZeroOnConnect);

            _massaClient.LogMessage += AppendLog;
            _massaClient.ScaleUpdated += OnScaleUpdated;

            if (startImmediately)
            {
                _massaClient.Start();
            }
        }

        private void TryWriteLogToFile(string message)
        {
            try
            {
                var directory = string.IsNullOrWhiteSpace(_settings.LogDirectory)
                    ? Path.Combine(AppContext.BaseDirectory, "logs")
                    : _settings.LogDirectory;

                Directory.CreateDirectory(directory);
                var logPath = Path.Combine(directory, "app.log");
                File.AppendAllText(logPath, message + Environment.NewLine);
            }
            catch
            {
                // Файловые ошибки намеренно игнорируем, чтобы не блокировать UI.
            }
        }

        private void ApplyOverlayDefaults(Camera cam)
        {
            switch (_settings.OverlayDefaultPosition)
            {
                case OverlayPosition.TopLeft:
                    cam.BasePosX = 100;
                    cam.BasePosY = 100;
                    break;
                case OverlayPosition.TopRight:
                    cam.BasePosX = 900;
                    cam.BasePosY = 100;
                    break;
                case OverlayPosition.BottomLeft:
                    cam.BasePosX = 100;
                    cam.BasePosY = 900;
                    break;
                case OverlayPosition.BottomRight:
                    cam.BasePosX = 900;
                    cam.BasePosY = 900;
                    break;
            }
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
                TimeSpan.FromMilliseconds(100),
                _settings,
                _settings.DefaultWeightUnit,
                _settings.WeightDecimalPlaces);

            _cameraOsdService.CameraStatusChanged += OnCameraStatusChanged;
            _cameraOsdService.LogMessage += AppendLog;

            _cameraOsdService.Start();
        }

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            _uiTimer.Stop();
            _connectivityTimer.Stop();
            if (_massaClient != null)
                await _massaClient.StopAsync();

            if (_cameraOsdService != null)
                await _cameraOsdService.StopAsync();

            SaveConfig();
        }
    }
}
