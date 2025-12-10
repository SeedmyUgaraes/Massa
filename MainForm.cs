using System;
using System.Windows.Forms;

namespace MassaKWin
{
    public class MainForm : Form
    {
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
            dgvCameras.Columns.Add("Ip", "IP");
            dgvCameras.Columns.Add("OverlayInfo", "Привязанные весы / OSD");

            // TODO: привязать к CameraManager и отрисовывать конфигурацию

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

        // TODO: позже добавить привязку к менеджерам, обработчики двойного клика для открытия трендов и т.д.
    }
}
