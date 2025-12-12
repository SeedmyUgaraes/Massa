using System;
using System.Drawing;
using System.Windows.Forms;
using MassaKWin.Core;

namespace MassaKWin
{
    public partial class ScaleEditForm : Form
    {
        private Label lblName;
        private TextBox txtName;
        private Label lblIp;
        private TextBox txtIp;
        private Label lblPort;
        private TextBox txtPort;
        private Button btnOk;
        private Button btnCancel;

        public new Scale Scale { get; private set; }

        public ScaleEditForm(Scale? scale = null)
        {
            InitializeComponent();

            if (scale != null)
            {
                Scale = scale;
                txtName.Text = Scale.Name;
                txtIp.Text = Scale.Ip;
                txtPort.Text = Scale.Port.ToString();
            }
            else
            {
                Scale = new Scale();
            }
        }

        private void InitializeComponent()
        {
            lblName = new Label
            {
                Text = "Имя",
                AutoSize = true,
                Location = new Point(10, 15)
            };

            txtName = new TextBox
            {
                Location = new Point(100, 12),
                Width = 170
            };

            lblIp = new Label
            {
                Text = "IP",
                AutoSize = true,
                Location = new Point(10, 50)
            };

            txtIp = new TextBox
            {
                Location = new Point(100, 47),
                Width = 170
            };

            lblPort = new Label
            {
                Text = "Порт",
                AutoSize = true,
                Location = new Point(10, 85)
            };

            txtPort = new TextBox
            {
                Location = new Point(100, 82),
                Width = 170
            };

            btnOk = new Button
            {
                Text = "ОК",
                Location = new Point(100, 120),
                DialogResult = DialogResult.None
            };
            btnOk.Click += BtnOkOnClick;

            btnCancel = new Button
            {
                Text = "Отмена",
                Location = new Point(190, 120)
            };
            btnCancel.Click += BtnCancelOnClick;

            AcceptButton = btnOk;
            CancelButton = btnCancel;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(290, 170);
            Text = "Весы";

            Controls.Add(lblName);
            Controls.Add(txtName);
            Controls.Add(lblIp);
            Controls.Add(txtIp);
            Controls.Add(lblPort);
            Controls.Add(txtPort);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
        }

        private void BtnOkOnClick(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtIp.Text))
            {
                MessageBox.Show("IP не может быть пустым.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!int.TryParse(txtPort.Text, out int port))
            {
                MessageBox.Show("Некорректный порт.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Scale.Name = txtName.Text;
            Scale.Ip = txtIp.Text;
            Scale.Port = port;

            DialogResult = DialogResult.OK;
            Close();
        }

        private void BtnCancelOnClick(object? sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
