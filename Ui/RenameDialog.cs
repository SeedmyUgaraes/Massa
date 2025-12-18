using System;
using System.Drawing;
using System.Windows.Forms;
using Krypton.Toolkit;

namespace MassaKWin.Ui
{
    public class RenameDialog : KryptonForm
    {
        private const int MaxNameLength = 64;

        private readonly KryptonTextBox _txtName;
        private readonly KryptonButton _btnOk;
        private readonly KryptonButton _btnCancel;

        private RenameDialog(string title, string currentName)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(360, 140);

            var lblPrompt = new KryptonLabel
            {
                Text = "Новое имя",
                Location = new Point(16, 18),
                AutoSize = true
            };

            _txtName = new KryptonTextBox
            {
                Location = new Point(16, 44),
                Width = 320,
                Text = currentName ?? string.Empty,
                MaxLength = MaxNameLength
            };

            _btnOk = new KryptonButton
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(168, 88),
                MinimumSize = new Size(80, 30)
            };
            _btnOk.Click += (_, _) => OnOk();

            _btnCancel = new KryptonButton
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(256, 88),
                MinimumSize = new Size(80, 30)
            };

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            Controls.Add(lblPrompt);
            Controls.Add(_txtName);
            Controls.Add(_btnOk);
            Controls.Add(_btnCancel);

            Shown += (_, _) =>
            {
                _txtName.SelectAll();
                _txtName.Focus();
            };
        }

        public static bool TryGetName(IWin32Window owner, string title, string currentName, out string newName)
        {
            using var dlg = new RenameDialog(title, currentName);
            var result = dlg.ShowDialog(owner);
            if (result == DialogResult.OK)
            {
                newName = dlg._txtName.Text.Trim();
                return !string.IsNullOrEmpty(newName);
            }

            newName = currentName;
            return false;
        }

        private void OnOk()
        {
            var trimmed = _txtName.Text.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                KryptonMessageBox.Show(this, "Имя не может быть пустым.", "Переименование", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (trimmed.Length > MaxNameLength)
            {
                KryptonMessageBox.Show(this, $"Длина имени не должна превышать {MaxNameLength} символов.", "Переименование", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
