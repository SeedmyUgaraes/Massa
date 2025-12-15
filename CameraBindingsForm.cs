using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
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

            if (enabledRows.Count > 4)
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
                else if (overlayId < 1 || overlayId > 4)
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
                while (overlayCounter <= 4 && usedOverlayIds.Contains(overlayCounter))
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
            for (int id = start; id <= 4; id++)
            {
                if (!usedOverlayIds.Contains(id))
                    return id;
            }

            for (int id = 1; id < start && id <= 4; id++)
            {
                if (!usedOverlayIds.Contains(id))
                    return id;
            }

            return 0;
        }

        private async Task ClearCameraOverlaysAsync()
        {
            using var client = new HikvisionOsdClient(_camera.Username, _camera.Password);

            for (int overlayId = 1; overlayId <= 4; overlayId++)
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
