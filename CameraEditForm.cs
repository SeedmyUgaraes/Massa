using System;
using System.Windows.Forms;
using MassaKWin.Core;

namespace MassaKWin
{
    public partial class CameraEditForm : Form
    {
        private TextBox _nameTextBox;
        private TextBox _ipTextBox;
        private TextBox _portTextBox;
        private TextBox _usernameTextBox;
        private TextBox _passwordTextBox;
        private Button _okButton;
        private Button _cancelButton;

        public MassaKWin.Core.Camera Camera { get; private set; }

        public CameraEditForm(MassaKWin.Core.Camera? camera = null)
        {
            InitializeComponent();

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
            _nameTextBox = new TextBox();
            _ipTextBox = new TextBox();
            _portTextBox = new TextBox();
            _usernameTextBox = new TextBox();
            _passwordTextBox = new TextBox();
            _okButton = new Button();
            _cancelButton = new Button();

            SuspendLayout();

            Text = "Камера";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new System.Drawing.Size(300, 250);

            var nameLabel = new Label
            {
                Text = "Имя:",
                Location = new System.Drawing.Point(10, 15),
                AutoSize = true
            };
            _nameTextBox.Location = new System.Drawing.Point(110, 12);
            _nameTextBox.Width = 170;

            var ipLabel = new Label
            {
                Text = "IP:",
                Location = new System.Drawing.Point(10, 50),
                AutoSize = true
            };
            _ipTextBox.Location = new System.Drawing.Point(110, 47);
            _ipTextBox.Width = 170;

            var portLabel = new Label
            {
                Text = "Порт:",
                Location = new System.Drawing.Point(10, 85),
                AutoSize = true
            };
            _portTextBox.Location = new System.Drawing.Point(110, 82);
            _portTextBox.Width = 170;

            var usernameLabel = new Label
            {
                Text = "Логин:",
                Location = new System.Drawing.Point(10, 120),
                AutoSize = true
            };
            _usernameTextBox.Location = new System.Drawing.Point(110, 117);
            _usernameTextBox.Width = 170;

            var passwordLabel = new Label
            {
                Text = "Пароль:",
                Location = new System.Drawing.Point(10, 155),
                AutoSize = true
            };
            _passwordTextBox.Location = new System.Drawing.Point(110, 152);
            _passwordTextBox.Width = 170;
            _passwordTextBox.PasswordChar = '•';

            _okButton.Text = "ОК";
            _okButton.Location = new System.Drawing.Point(110, 195);
            _okButton.Click += OkButton_Click;

            _cancelButton.Text = "Отмена";
            _cancelButton.Location = new System.Drawing.Point(200, 195);
            _cancelButton.Click += CancelButton_Click;

            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            Controls.Add(nameLabel);
            Controls.Add(_nameTextBox);
            Controls.Add(ipLabel);
            Controls.Add(_ipTextBox);
            Controls.Add(portLabel);
            Controls.Add(_portTextBox);
            Controls.Add(usernameLabel);
            Controls.Add(_usernameTextBox);
            Controls.Add(passwordLabel);
            Controls.Add(_passwordTextBox);
            Controls.Add(_okButton);
            Controls.Add(_cancelButton);

            ResumeLayout(false);
            PerformLayout();
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
