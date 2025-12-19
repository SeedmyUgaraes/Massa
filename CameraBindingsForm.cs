using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Krypton.Toolkit;
using MassaKWin.Core;
using ThemeManager = MassaKWin.Ui.ThemeManager;

namespace MassaKWin
{
    public partial class CameraBindingsForm : KryptonForm
    {
        private const int MaxOverlayId = 4;
        private readonly Camera _camera;
        private readonly ScaleManager _scaleManager;
        private DataGridView _dgvBindings = null!;
        private KryptonButton _btnOk = null!;
        private KryptonButton _btnCancel = null!;

        public CameraBindingsForm(Camera camera, ScaleManager scaleManager)
        {
            _camera = camera;
            _scaleManager = scaleManager;

            InitializeComponent();
            ThemeManager.Apply(this);
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

        private async void OnOkClicked(object? sender, EventArgs e)
        {
            var newBindings = BuildBindingsFromGrid();
            if (newBindings == null)
                return;

            await ClearCameraOverlaysAsync();

            _camera.Bindings.Clear();
            _camera.Bindings.AddRange(newBindings);

            DialogResult = DialogResult.OK;
            Close();
        }

        private List<CameraScaleBinding>? BuildBindingsFromGrid()
        {
            var enabledRows = _dgvBindings.Rows
                .Cast<DataGridViewRow>()
                .Where(r => r.Tag is Scale)
                .Where(r => r.Cells["Enabled"].Value is bool enabled && enabled)
                .ToList();

            if (enabledRows.Count > MaxOverlayId)
            {
                MessageBox.Show("Количество включенных привязок не может превышать 4.");
                return null;
            }

            var result = new List<CameraScaleBinding>();
            var usedOverlayIds = new HashSet<int>();
            var overlayCounter = 1;

            foreach (var row in enabledRows)
            {
                var scale = row.Tag as Scale;
                if (scale == null)
                    continue;

                var overlayCellValue = row.Cells["OverlayId"].Value?.ToString();
                int overlayId = 0;
                if (!string.IsNullOrWhiteSpace(overlayCellValue))
                    int.TryParse(overlayCellValue, out overlayId);

                if (overlayId <= 0)
                {
                    overlayId = GetNextFreeOverlayId(overlayCounter, usedOverlayIds);
                    if (overlayId == 0)
                    {
                        MessageBox.Show("Не удалось автоматически подобрать OverlayId.");
                        return null;
                    }
                }
                else if (overlayId < 1 || overlayId > MaxOverlayId)
                {
                    MessageBox.Show("OverlayId должен быть от 1 до 4");
                    return null;
                }

                if (usedOverlayIds.Contains(overlayId))
                {
                    MessageBox.Show("OverlayId не должен повторяться для включенных привязок.");
                    return null;
                }

                usedOverlayIds.Add(overlayId);
                while (overlayCounter <= MaxOverlayId && usedOverlayIds.Contains(overlayCounter))
                {
                    overlayCounter++;
                }

                result.Add(new CameraScaleBinding
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

            return result;
        }

        private static int GetNextFreeOverlayId(int startFrom, HashSet<int> usedOverlayIds)
        {
            var start = Math.Max(1, startFrom);
            for (int id = start; id <= MaxOverlayId; id++)
            {
                if (!usedOverlayIds.Contains(id))
                    return id;
            }

            for (int id = 1; id < start && id <= MaxOverlayId; id++)
            {
                if (!usedOverlayIds.Contains(id))
                    return id;
            }

            return 0;
        }

        private async Task ClearCameraOverlaysAsync()
        {
            using var client = new HikvisionOsdClient(_camera.Username, _camera.Password);

            for (int overlayId = 1; overlayId <= MaxOverlayId; overlayId++)
            {
                try
                {
                    await client.ClearOverlayAsync(_camera.Ip, overlayId);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"Ошибка очистки OverlayId {overlayId} для камеры {_camera.Name}: {ex.Message}");
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine($"Ошибка очистки OverlayId {overlayId} для камеры {_camera.Name}: {ex.Message}");
                }
            }
        }

        private void OnCancelClicked(object? sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void InitializeComponent()
        {
            _dgvBindings = new DataGridView();
            _btnOk = new KryptonButton();
            _btnCancel = new KryptonButton();

            SuspendLayout();

            Text = "Привязки весов";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new System.Drawing.Size(520, 340);
            Padding = new Padding(8);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _dgvBindings.AllowUserToAddRows = false;
            _dgvBindings.AllowUserToDeleteRows = false;
            _dgvBindings.RowHeadersVisible = false;
            _dgvBindings.Dock = DockStyle.Fill;
            _dgvBindings.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _dgvBindings.MultiSelect = false;
            _dgvBindings.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            StyleBindingsGrid();

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

            var buttonsPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 10, 0, 0)
            };

            _btnOk.Text = "Сохранить";
            _btnOk.AutoSize = true;
            _btnOk.MinimumSize = new System.Drawing.Size(100, 32);
            _btnOk.Click += OnOkClicked;

            _btnCancel.Text = "Отмена";
            _btnCancel.AutoSize = true;
            _btnCancel.MinimumSize = new System.Drawing.Size(100, 32);
            _btnCancel.Click += OnCancelClicked;

            buttonsPanel.Controls.Add(_btnCancel);
            buttonsPanel.Controls.Add(_btnOk);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;

            layout.Controls.Add(_dgvBindings, 0, 0);
            layout.Controls.Add(buttonsPanel, 0, 1);

            Controls.Add(layout);

            ResumeLayout(false);
        }

        private void StyleBindingsGrid()
        {
            _dgvBindings.BackgroundColor = System.Drawing.Color.White;
            _dgvBindings.BorderStyle = BorderStyle.None;
            _dgvBindings.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            _dgvBindings.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            _dgvBindings.ColumnHeadersDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(245, 247, 250);
            _dgvBindings.ColumnHeadersDefaultCellStyle.Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold);
            _dgvBindings.RowTemplate.Height = 28;
            _dgvBindings.DefaultCellStyle.SelectionBackColor = System.Drawing.Color.FromArgb(227, 235, 250);
            _dgvBindings.DefaultCellStyle.SelectionForeColor = System.Drawing.Color.Black;

            var prop = typeof(DataGridView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            prop?.SetValue(_dgvBindings, true);
        }

    }
}
