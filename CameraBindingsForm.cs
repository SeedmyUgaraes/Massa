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
        private Button _btnOk = null!;
        private Button _btnCancel = null!;

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
                var existingBinding = _camera
                    .Bindings
                    .FirstOrDefault(b => b.Scale?.Id == scale.Id);

                var rowIndex = _dgvBindings.Rows.Add(
                    existingBinding?.Enabled ?? false,                        // Enabled
                    scale.Name,                                               // Имя весов
                    existingBinding != null && existingBinding.OverlayId > 0  // OverlayId
                        ? existingBinding.OverlayId
                        : null
                );

                _dgvBindings.Rows[rowIndex].Tag = scale; // чтобы потом знать, к каким весам относится строка
            }
        }

        private void OnOkClicked(object? sender, EventArgs e)
        {
            _camera.Bindings.Clear();
            var overlayCounter = 1;

            foreach (DataGridViewRow row in _dgvBindings.Rows)
            {
                if (row.Tag is not Scale scale)
                    continue;

                var enabledValue = row.Cells["Enabled"].Value;
                var enabled = enabledValue is bool enabledBool && enabledBool;
                if (!enabled)
                    continue;

                var overlayCellValue = row.Cells["OverlayId"].Value?.ToString();
                int overlayId = 0;
                if (!string.IsNullOrWhiteSpace(overlayCellValue))
                    int.TryParse(overlayCellValue, out overlayId);

                if (overlayId <= 0)
                {
                    overlayId = overlayCounter;
                    overlayCounter++;
                }
                else
                {
                    overlayCounter = Math.Max(overlayCounter, overlayId + 1);
                }

                _camera.Bindings.Add(new CameraScaleBinding
                {
                    Camera = _camera,
                    Scale = scale,
                    OverlayId = overlayId,
                    Enabled = true,
                    AutoPosition = true,
                    PositionX = 0,
                    PositionY = 0
                });
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnCancelClicked(object? sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void InitializeComponent()
        {
            _dgvBindings = new DataGridView();
            _btnOk = new Button();
            _btnCancel = new Button();
            var buttonsPanel = new Panel();

            SuspendLayout();

            // --- Форма ---
            Text = "Привязки весов";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new System.Drawing.Size(500, 300);

            // --- Таблица ---
            _dgvBindings.AllowUserToAddRows = false;
            _dgvBindings.AllowUserToDeleteRows = false;
            _dgvBindings.RowHeadersVisible = false;
            _dgvBindings.Dock = DockStyle.Fill;
            _dgvBindings.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _dgvBindings.MultiSelect = false;

            var enabledColumn = new DataGridViewCheckBoxColumn
            {
                Name = "Enabled",
                HeaderText = "Вкл.",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells
            };

            var scaleNameColumn = new DataGridViewTextBoxColumn
            {
                Name = "ScaleName",
                HeaderText = "Весы",
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

            // --- Панель с кнопками ---
            buttonsPanel.Dock = DockStyle.Bottom;
            buttonsPanel.Height = 50;
            buttonsPanel.Padding = new Padding(10);

            // Кнопка "Сохранить"
            _btnOk.Text = "Сохранить";
            _btnOk.Size = new System.Drawing.Size(90, 23);
            _btnOk.Location = new System.Drawing.Point(10, 10);
            _btnOk.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            _btnOk.Click += OnOkClicked;

            // Кнопка "Отмена"
            _btnCancel.Text = "Отмена";
            _btnCancel.Size = new System.Drawing.Size(90, 23);
            _btnCancel.Location = new System.Drawing.Point(110, 10);
            _btnCancel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            _btnCancel.Click += OnCancelClicked;

            buttonsPanel.Controls.Add(_btnOk);
            buttonsPanel.Controls.Add(_btnCancel);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            Controls.Add(_dgvBindings);
            Controls.Add(buttonsPanel);

            ResumeLayout(false);
        }

    }
}
