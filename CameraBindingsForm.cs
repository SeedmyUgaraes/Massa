using System;
using System.Linq;
using System.Windows.Forms;
using MassaKWin.Core;

namespace MassaKWin
{
    public partial class CameraBindingsForm : Form
    {
        private readonly Camera _camera;
        private readonly ScaleManager _scaleManager;
        private DataGridView _dgvBindings = null!;
        private Button _okButton = null!;
        private Button _cancelButton = null!;

        public CameraBindingsForm(Camera camera, ScaleManager scaleManager)
        {
            _camera = camera;
            _scaleManager = scaleManager;

            InitializeComponent();
            FillBindings();
        }

        private void FillBindings()
        {
            _dgvBindings.Rows.Clear();

            foreach (var scale in _scaleManager.Scales)
            {
                var existingBinding = _camera.Bindings.FirstOrDefault(b => b.Scale?.Id == scale.Id);
                var rowIndex = _dgvBindings.Rows.Add(
                    existingBinding?.Enabled ?? false,
                    scale.Name,
                    existingBinding?.OverlayId > 0 ? existingBinding?.OverlayId : null
                );

                _dgvBindings.Rows[rowIndex].Tag = scale;
            }
        }

        private void OkButton_Click(object? sender, EventArgs e)
        {
            _camera.Bindings.Clear();
            var overlayCounter = 1;

            foreach (DataGridViewRow row in _dgvBindings.Rows)
            {
                if (row.Tag is not Scale scale)
                {
                    continue;
                }

                var enabledValue = row.Cells["Enabled"].Value;
                var enabled = enabledValue != null && enabledValue is bool boolVal && boolVal;

                if (!enabled)
                {
                    continue;
                }

                var overlayIdValue = row.Cells["OverlayId"].Value?.ToString();
                int overlayId = 0;
                if (!string.IsNullOrWhiteSpace(overlayIdValue))
                {
                    int.TryParse(overlayIdValue, out overlayId);
                }

                if (overlayId <= 0)
                {
                    overlayId = overlayCounter;
                }

                overlayCounter = Math.Max(overlayCounter, overlayId + 1);

                _camera.Bindings.Add(new CameraScaleBinding
                {
                    Camera = _camera,
                    Scale = scale,
                    OverlayId = overlayId,
                    AutoPosition = true,
                    Enabled = true
                });
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void CancelButton_Click(object? sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void InitializeComponent()
        {
            _dgvBindings = new DataGridView();
            _okButton = new Button();
            _cancelButton = new Button();
            var buttonsPanel = new Panel();

            SuspendLayout();

            Text = "Привязки весов";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new System.Drawing.Size(500, 300);

            _dgvBindings.AllowUserToAddRows = false;
            _dgvBindings.AllowUserToDeleteRows = false;
            _dgvBindings.RowHeadersVisible = false;
            _dgvBindings.Dock = DockStyle.Fill;
            _dgvBindings.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _dgvBindings.MultiSelect = false;

            var enabledColumn = new DataGridViewCheckBoxColumn
            {
                Name = "Enabled",
                HeaderText = "Enabled",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells
            };

            var scaleNameColumn = new DataGridViewTextBoxColumn
            {
                Name = "ScaleName",
                HeaderText = "ScaleName",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            };

            var overlayIdColumn = new DataGridViewTextBoxColumn
            {
                Name = "OverlayId",
                HeaderText = "OverlayId",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells
            };

            _dgvBindings.Columns.AddRange(enabledColumn, scaleNameColumn, overlayIdColumn);

            buttonsPanel.Dock = DockStyle.Bottom;
            buttonsPanel.Height = 50;
            buttonsPanel.Padding = new Padding(10);

            _okButton.Text = "ОК";
            _okButton.DialogResult = DialogResult.OK;
            _okButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            _okButton.Location = new System.Drawing.Point(300, 10);
            _okButton.Click += OkButton_Click;

            _cancelButton.Text = "Отмена";
            _cancelButton.DialogResult = DialogResult.Cancel;
            _cancelButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            _cancelButton.Location = new System.Drawing.Point(400, 10);
            _cancelButton.Click += CancelButton_Click;

            buttonsPanel.Controls.Add(_okButton);
            buttonsPanel.Controls.Add(_cancelButton);

            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            Controls.Add(_dgvBindings);
            Controls.Add(buttonsPanel);

            ResumeLayout(false);
        }
    }
}
