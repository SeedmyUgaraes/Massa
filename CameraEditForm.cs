using System;
using System.Drawing;
using System.Windows.Forms;
using Krypton.Toolkit;
using MassaKWin.Core;
using ThemeManager = MassaKWin.Ui.ThemeManager;

namespace MassaKWin
{
    public partial class CameraEditForm : KryptonForm
    {
        private KryptonTextBox _nameTextBox = null!;
        private KryptonTextBox _ipTextBox = null!;
        private KryptonTextBox _portTextBox = null!;
        private KryptonTextBox _usernameTextBox = null!;
        private KryptonTextBox _passwordTextBox = null!;
        private KryptonButton _okButton = null!;
        private KryptonButton _cancelButton = null!;

        public MassaKWin.Core.Camera Camera { get; private set; }

        public CameraEditForm(MassaKWin.Core.Camera? camera = null)
        {
            InitializeComponent();
            ThemeManager.Apply(this);

            if (camera != null)
            {
                _nameTextBox.Text = camera.Name;
                _ipTextBox.Text = camera.Ip;
                _portTextBox.Text = camera.Port.ToString();
                _usernameTextBox.Text = camera.Username;
                _passwordTextBox.Text = camera.Password;
                Camera = camera;
            }
            else
            {
                Camera = new MassaKWin.Core.Camera();
            }
        }

        private void InitializeComponent()
        {
            _nameTextBox = new KryptonTextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            _ipTextBox = new KryptonTextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            _portTextBox = new KryptonTextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            _usernameTextBox = new KryptonTextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            _passwordTextBox = new KryptonTextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, PasswordChar = '•' };
            _okButton = new KryptonButton { Text = "ОК", AutoSize = true, MinimumSize = new Size(90, 32) };
            _cancelButton = new KryptonButton { Text = "Отмена", AutoSize = true, MinimumSize = new Size(90, 32) };

            _okButton.Click += OkButton_Click;
            _cancelButton.Click += CancelButton_Click;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Padding = new Padding(12),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            string[] labels = { "Имя:", "IP:", "Порт:", "Логин:", "Пароль:" };
            Control[] controls = { _nameTextBox, _ipTextBox, _portTextBox, _usernameTextBox, _passwordTextBox };

            for (int i = 0; i < labels.Length; i++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var lbl = new Label { Text = labels[i], AutoSize = true, Anchor = AnchorStyles.Left };
                layout.Controls.Add(lbl, 0, i);
                layout.Controls.Add(controls[i], 1, i);
            }

            var buttonsPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(0, 12, 0, 0)
            };
            buttonsPanel.Controls.Add(_cancelButton);
            buttonsPanel.Controls.Add(_okButton);

            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(buttonsPanel, 0, labels.Length);
            layout.SetColumnSpan(buttonsPanel, 2);

            Text = "Камера";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(360, 260);

            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            Controls.Add(layout);
        }

        private void OkButton_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_ipTextBox.Text))
            {
                MessageBox.Show("IP не может быть пустым", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(_usernameTextBox.Text))
            {
                MessageBox.Show("Логин не может быть пустым", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!int.TryParse(_portTextBox.Text, out var port))
            {
                MessageBox.Show("Порт должен быть числом", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Camera.Name = _nameTextBox.Text;
            Camera.Ip = _ipTextBox.Text;
            Camera.Port = port;
            Camera.Username = _usernameTextBox.Text;
            Camera.Password = _passwordTextBox.Text;

            DialogResult = DialogResult.OK;
            Close();
        }

        private void CancelButton_Click(object? sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
