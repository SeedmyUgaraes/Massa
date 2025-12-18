using System;
using System.Drawing;
using System.Windows.Forms;
using Krypton.Toolkit;
using MassaKWin.Core;
using ThemeManager = MassaKWin.Ui.ThemeManager;

namespace MassaKWin
{
    public partial class ScaleEditForm : KryptonForm
    {
        private Label lblName = null!;
        private KryptonTextBox txtName = null!;
        private Label lblIp = null!;
        private KryptonTextBox txtIp = null!;
        private Label lblPort = null!;
        private KryptonTextBox txtPort = null!;
        private KryptonButton btnOk = null!;
        private KryptonButton btnCancel = null!;

        public new Scale Scale { get; private set; }

        public ScaleEditForm(Scale? scale = null)
        {
            InitializeComponent();
            ThemeManager.Apply(this);

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
            lblName = new Label { Text = "Имя", AutoSize = true, Anchor = AnchorStyles.Left };
            txtName = new KryptonTextBox { Width = 200, Anchor = AnchorStyles.Left | AnchorStyles.Right };

            lblIp = new Label { Text = "IP", AutoSize = true, Anchor = AnchorStyles.Left };
            txtIp = new KryptonTextBox { Width = 200, Anchor = AnchorStyles.Left | AnchorStyles.Right };

            lblPort = new Label { Text = "Порт", AutoSize = true, Anchor = AnchorStyles.Left };
            txtPort = new KryptonTextBox { Width = 200, Anchor = AnchorStyles.Left | AnchorStyles.Right };

            btnOk = new KryptonButton
            {
                Text = "ОК",
                DialogResult = DialogResult.None,
                AutoSize = true,
                MinimumSize = new Size(90, 32)
            };
            btnOk.Click += BtnOkOnClick;

            btnCancel = new KryptonButton
            {
                Text = "Отмена",
                AutoSize = true,
                MinimumSize = new Size(90, 32)
            };
            btnCancel.Click += BtnCancelOnClick;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                Padding = new Padding(12),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(lblName, 0, 0);
            layout.Controls.Add(txtName, 1, 0);
            layout.Controls.Add(lblIp, 0, 1);
            layout.Controls.Add(txtIp, 1, 1);
            layout.Controls.Add(lblPort, 0, 2);
            layout.Controls.Add(txtPort, 1, 2);

            var buttonsPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(0, 12, 0, 0)
            };
            buttonsPanel.Controls.Add(btnCancel);
            buttonsPanel.Controls.Add(btnOk);

            layout.Controls.Add(buttonsPanel, 0, 3);
            layout.SetColumnSpan(buttonsPanel, 2);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(340, 190);
            Text = "Весы";

            Controls.Add(layout);
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
