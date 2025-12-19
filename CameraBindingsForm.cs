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
        private const int DefaultNormalizedWidth = 704;
        private const int DefaultNormalizedHeight = 576;
        private const int OverlayMarginLeft = 16;
        private const int OverlayMarginBottom = 32;
        private readonly Camera _camera;
        private readonly ScaleManager _scaleManager;
        private DataGridView _dgvBindings = null!;
        private KryptonButton _btnOk = null!;
        private KryptonButton _btnCancel = null!;
        private KryptonCheckBox _chkManualOsd = null!;
        private NumericUpDown _numOsdX = null!;
        private NumericUpDown _numOsdY = null!;
        private Label _lblOsdHint = null!;
        private Label _lblOsdWarning = null!;
        private int _normalizedWidth = DefaultNormalizedWidth;
        private int _normalizedHeight = DefaultNormalizedHeight;
        private bool _normalizedSizeFallback;
        private int? _pendingOsdX;
        private int? _pendingOsdY;

        public CameraBindingsForm(Camera camera, ScaleManager scaleManager)
        {
            _camera = camera;
            _scaleManager = scaleManager;

            InitializeComponent();
            ThemeManager.Apply(this);
            FillBindings();
            _pendingOsdX = _camera.OsdBasePosX;
            _pendingOsdY = _camera.OsdBasePosY;
            _chkManualOsd.Checked = _pendingOsdX.HasValue && _pendingOsdY.HasValue;
            UpdateManualControls();
            ApplyNormalizedRanges();
            ApplyOsdValues();
            UpdateOsdHint();
            Shown += async (_, _) => await LoadNormalizedSizeAsync();
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

            ApplyOsdSettings();

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
                    await client.ClearOverlayAsync(_camera.Ip, _camera.Port, overlayId);
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

        private void ApplyOsdSettings()
        {
            if (!_chkManualOsd.Checked)
            {
                _camera.OsdBasePosX = null;
                _camera.OsdBasePosY = null;
                return;
            }

            var baseX = (int)_numOsdX.Value;
            var baseY = (int)_numOsdY.Value;
            var maxX = Math.Max(_normalizedWidth - 1, 0);
            var maxY = Math.Max(_normalizedHeight - 1, 0);
            baseX = Math.Clamp(baseX, 0, maxX);
            baseY = Math.Clamp(baseY, 0, maxY);

            var lineHeight = Math.Clamp(_camera.LineHeight, 18, 48);
            var requiredMinY = (MaxOverlayId - 1) * lineHeight;
            if (baseY < requiredMinY)
            {
                var adjustedY = Math.Min(requiredMinY, maxY);
                if (adjustedY != baseY)
                {
                    baseY = adjustedY;
                    MessageBox.Show(
                        $"Значение OSD Y было повышено до {baseY}, чтобы все строки оверлея оставались в пределах экрана.",
                        "Предупреждение",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }

            SetNumericValue(_numOsdX, baseX);
            SetNumericValue(_numOsdY, baseY);

            _camera.OsdBasePosX = baseX;
            _camera.OsdBasePosY = baseY;
        }

        private async Task LoadNormalizedSizeAsync()
        {
            _normalizedSizeFallback = false;
            try
            {
                using var client = new HikvisionOsdClient(_camera.Username, _camera.Password);
                var (width, height) = await client.GetNormalizedScreenSizeAsync(_camera.Ip, _camera.Port);
                _normalizedWidth = width;
                _normalizedHeight = height;
            }
            catch
            {
                _normalizedWidth = DefaultNormalizedWidth;
                _normalizedHeight = DefaultNormalizedHeight;
                _normalizedSizeFallback = true;
            }

            ApplyNormalizedRanges();
            ApplyOsdValues();
            UpdateOsdHint();
        }

        private void ApplyNormalizedRanges()
        {
            _numOsdX.Minimum = 0;
            _numOsdX.Maximum = Math.Max(_normalizedWidth - 1, 0);
            _numOsdY.Minimum = 0;
            _numOsdY.Maximum = Math.Max(_normalizedHeight - 1, 0);

            SetNumericValue(_numOsdX, (int)_numOsdX.Value);
            SetNumericValue(_numOsdY, (int)_numOsdY.Value);
        }

        private void ApplyOsdValues()
        {
            if (_chkManualOsd.Checked && _pendingOsdX.HasValue && _pendingOsdY.HasValue)
            {
                SetNumericValue(_numOsdX, _pendingOsdX.Value);
                SetNumericValue(_numOsdY, _pendingOsdY.Value);
                _pendingOsdX = null;
                _pendingOsdY = null;
                return;
            }

            if (!_chkManualOsd.Checked)
            {
                var autoX = OverlayMarginLeft;
                var autoY = Math.Max(_normalizedHeight - OverlayMarginBottom, 0);
                SetNumericValue(_numOsdX, autoX);
                SetNumericValue(_numOsdY, autoY);
            }
        }

        private void UpdateManualControls()
        {
            var enabled = _chkManualOsd.Checked;
            _numOsdX.Enabled = enabled;
            _numOsdY.Enabled = enabled;
            _lblOsdWarning.Visible = enabled;
        }

        private void UpdateOsdHint()
        {
            var fallbackText = _normalizedSizeFallback ? " (используется fallback)" : string.Empty;
            _lblOsdHint.Text = $"Координаты в системе normalizedScreenSize Hikvision (обычно 704×576). Текущий размер: {_normalizedWidth}×{_normalizedHeight}{fallbackText}.";
        }

        private static void SetNumericValue(NumericUpDown numeric, int value)
        {
            var min = (int)numeric.Minimum;
            var max = (int)numeric.Maximum;
            var clamped = Math.Clamp(value, min, max);
            numeric.Value = clamped;
        }

        private void InitializeComponent()
        {
            _dgvBindings = new DataGridView();
            _btnOk = new KryptonButton();
            _btnCancel = new KryptonButton();
            _chkManualOsd = new KryptonCheckBox();
            _numOsdX = new NumericUpDown();
            _numOsdY = new NumericUpDown();
            _lblOsdHint = new Label();
            _lblOsdWarning = new Label();

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
                RowCount = 3,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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

            _chkManualOsd.Text = "Ручные координаты";
            _chkManualOsd.AutoSize = true;
            _chkManualOsd.CheckedChanged += (_, _) => UpdateManualControls();

            _numOsdX.Minimum = 0;
            _numOsdX.Maximum = DefaultNormalizedWidth - 1;
            _numOsdX.Width = 120;

            _numOsdY.Minimum = 0;
            _numOsdY.Maximum = DefaultNormalizedHeight - 1;
            _numOsdY.Width = 120;

            _lblOsdHint.AutoSize = true;
            _lblOsdHint.ForeColor = System.Drawing.Color.DimGray;

            _lblOsdWarning.AutoSize = true;
            _lblOsdWarning.ForeColor = System.Drawing.Color.DarkGoldenrod;
            _lblOsdWarning.Text = "Если Y слишком низкий, значение будет автоматически увеличено, чтобы все строки оверлея были видимы.";

            var osdPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(0, 0, 0, 8)
            };
            osdPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            osdPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var osdXLabel = new Label { Text = "OSD X (0..W-1)", AutoSize = true, Anchor = AnchorStyles.Left };
            var osdYLabel = new Label { Text = "OSD Y (0..H-1)", AutoSize = true, Anchor = AnchorStyles.Left };

            osdPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            osdPanel.Controls.Add(_chkManualOsd, 0, 0);
            osdPanel.SetColumnSpan(_chkManualOsd, 2);

            osdPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            osdPanel.Controls.Add(osdXLabel, 0, 1);
            osdPanel.Controls.Add(_numOsdX, 1, 1);

            osdPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            osdPanel.Controls.Add(osdYLabel, 0, 2);
            osdPanel.Controls.Add(_numOsdY, 1, 2);

            osdPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            osdPanel.Controls.Add(_lblOsdHint, 0, 3);
            osdPanel.SetColumnSpan(_lblOsdHint, 2);

            osdPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            osdPanel.Controls.Add(_lblOsdWarning, 0, 4);
            osdPanel.SetColumnSpan(_lblOsdWarning, 2);

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

            layout.Controls.Add(osdPanel, 0, 0);
            layout.Controls.Add(_dgvBindings, 0, 1);
            layout.Controls.Add(buttonsPanel, 0, 2);

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
