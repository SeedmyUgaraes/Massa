using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MassaKWin.Core;

namespace MassaKWin
{
    public partial class ScaleDiscoveryForm : Form
    {
        private readonly ScaleManager _scaleManager;
        private TextBox _txtFromIp = null!;
        private TextBox _txtToIp = null!;
        private Button _btnScan = null!;
        private Button _btnStop = null!;
        private Button _btnAddSelected = null!;
        private DataGridView _dgvResults = null!;
        private ProgressBar _progressBar = null!;
        private Label _lblStatus = null!;
        private CancellationTokenSource? _cts;
        private bool _hasChanges;

        public ScaleDiscoveryForm(ScaleManager scaleManager)
        {
            _scaleManager = scaleManager;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "Автопоиск весов";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new System.Drawing.Size(700, 400);

            var lblFromIp = new Label
            {
                Text = "IP от:",
                AutoSize = true,
                Location = new System.Drawing.Point(10, 15)
            };

            _txtFromIp = new TextBox
            {
                Location = new System.Drawing.Point(70, 12),
                Width = 150
            };

            var lblToIp = new Label
            {
                Text = "IP до:",
                AutoSize = true,
                Location = new System.Drawing.Point(240, 15)
            };

            _txtToIp = new TextBox
            {
                Location = new System.Drawing.Point(300, 12),
                Width = 150
            };

            _btnScan = new Button
            {
                Text = "Сканировать",
                Location = new System.Drawing.Point(470, 10),
                Width = 100
            };
            _btnScan.Click += OnScanClicked;

            _btnStop = new Button
            {
                Text = "Стоп",
                Location = new System.Drawing.Point(580, 10),
                Width = 100,
                Enabled = false
            };
            _btnStop.Click += OnStopClicked;

            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 45
            };
            topPanel.Controls.Add(lblFromIp);
            topPanel.Controls.Add(_txtFromIp);
            topPanel.Controls.Add(lblToIp);
            topPanel.Controls.Add(_txtToIp);
            topPanel.Controls.Add(_btnScan);
            topPanel.Controls.Add(_btnStop);

            _dgvResults = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false
            };

            var colSelected = new DataGridViewCheckBoxColumn
            {
                Name = "Selected",
                HeaderText = "",
                Width = 30
            };

            var colIp = new DataGridViewTextBoxColumn
            {
                Name = "Ip",
                HeaderText = "IP",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells
            };

            var colPort = new DataGridViewTextBoxColumn
            {
                Name = "Port",
                HeaderText = "Port",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells
            };

            var colStatus = new DataGridViewTextBoxColumn
            {
                Name = "Status",
                HeaderText = "Статус",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            };

            _dgvResults.Columns.AddRange(colSelected, colIp, colPort, colStatus);

            _btnAddSelected = new Button
            {
                Text = "Добавить выделенные в весы",
                Dock = DockStyle.Bottom,
                Height = 35
            };
            _btnAddSelected.Click += OnAddSelectedClicked;

            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Bottom,
                Height = 18
            };

            _lblStatus = new Label
            {
                Text = "Готово",
                Dock = DockStyle.Bottom,
                Height = 20,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };

            Controls.Add(_dgvResults);
            Controls.Add(_btnAddSelected);
            Controls.Add(_progressBar);
            Controls.Add(_lblStatus);
            Controls.Add(topPanel);
        }

        private async void OnScanClicked(object? sender, EventArgs e)
        {
            if (_cts != null)
            {
                MessageBox.Show("Сканирование уже выполняется.", "Автопоиск", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!TryParseIpRange(_txtFromIp.Text, _txtToIp.Text, out var fromIp, out var toIp))
            {
                MessageBox.Show("Некорректный диапазон IP.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _cts = new CancellationTokenSource();
            _btnScan.Enabled = false;
            _btnStop.Enabled = true;
            _dgvResults.Rows.Clear();
            _progressBar.Value = 0;
            _lblStatus.Text = "Сканирование...";

            try
            {
                await ScanRangeAsync(fromIp, toIp, _cts.Token);
                _lblStatus.Text = "Сканирование завершено.";
            }
            catch (OperationCanceledException)
            {
                _lblStatus.Text = "Сканирование отменено.";
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Ошибка сканирования.";
                MessageBox.Show($"Ошибка сканирования: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                _btnScan.Enabled = true;
                _btnStop.Enabled = false;
            }
        }

        private void OnStopClicked(object? sender, EventArgs e)
        {
            _cts?.Cancel();
        }

        private bool TryParseIpRange(string fromText, string toText, out System.Net.IPAddress fromIp, out System.Net.IPAddress toIp)
        {
            fromIp = System.Net.IPAddress.None;
            toIp = System.Net.IPAddress.None;

            if (!System.Net.IPAddress.TryParse(fromText, out fromIp))
                return false;
            if (!System.Net.IPAddress.TryParse(toText, out toIp))
                return false;

            var fromBytes = fromIp.GetAddressBytes();
            var toBytes = toIp.GetAddressBytes();
            if (fromBytes.Length != 4 || toBytes.Length != 4)
                return false;

            uint fromVal = (uint)(fromBytes[0] << 24 | fromBytes[1] << 16 | fromBytes[2] << 8 | fromBytes[3]);
            uint toVal = (uint)(toBytes[0] << 24 | toBytes[1] << 16 | toBytes[2] << 8 | toBytes[3]);

            if (fromVal > toVal)
                return false;

            return true;
        }

        private async Task ScanRangeAsync(System.Net.IPAddress fromIp, System.Net.IPAddress toIp, CancellationToken ct)
        {
            var fromBytes = fromIp.GetAddressBytes();
            var toBytes = toIp.GetAddressBytes();

            uint fromVal = (uint)(fromBytes[0] << 24 | fromBytes[1] << 16 | fromBytes[2] << 8 | fromBytes[3]);
            uint toVal = (uint)(toBytes[0] << 24 | toBytes[1] << 16 | toBytes[2] << 8 | toBytes[3]);

            int total = (int)(toVal - fromVal + 1);
            if (total <= 0) total = 1;

            _progressBar.Minimum = 0;
            _progressBar.Maximum = total;
            int processed = 0;

            const int port = 5000;
            const int timeoutMs = 300;

            for (uint val = fromVal; val <= toVal; val++)
            {
                ct.ThrowIfCancellationRequested();

                var b1 = (byte)((val >> 24) & 0xFF);
                var b2 = (byte)((val >> 16) & 0xFF);
                var b3 = (byte)((val >> 8) & 0xFF);
                var b4 = (byte)(val & 0xFF);
                var ip = new System.Net.IPAddress(new[] { b1, b2, b3, b4 });

                bool isScale = await ProbeHostAsync(ip.ToString(), port, timeoutMs, ct);
                processed++;

                _progressBar.Value = Math.Min(processed, _progressBar.Maximum);
                _lblStatus.Text = $"Просканировано {processed}/{total}";

                if (isScale)
                {
                    if (InvokeRequired)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            AddResultRow(ip.ToString(), port, "Найдено устройство");
                        }));
                    }
                    else
                    {
                        AddResultRow(ip.ToString(), port, "Найдено устройство");
                    }
                }
            }
        }

        private async Task<bool> ProbeHostAsync(string ip, int port, int timeoutMs, CancellationToken ct)
        {
            try
            {
                using var client = new System.Net.Sockets.TcpClient();

                var connectTask = client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(timeoutMs, ct);

                var completed = await Task.WhenAny(connectTask, timeoutTask);
                if (completed != connectTask)
                    return false;

                if (!client.Connected)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void AddResultRow(string ip, int port, string status)
        {
            int rowIndex = _dgvResults.Rows.Add(false, ip, port, status);
            _dgvResults.Rows[rowIndex].Tag = ip;
        }

        private void OnAddSelectedClicked(object? sender, EventArgs e)
        {
            const int port = 5000;

            foreach (DataGridViewRow row in _dgvResults.Rows)
            {
                var cellSelected = row.Cells["Selected"].Value;
                bool selected = cellSelected is bool b && b;
                if (!selected)
                    continue;

                string? ip = row.Cells["Ip"].Value?.ToString();
                if (string.IsNullOrWhiteSpace(ip))
                    continue;

                if (_scaleManager.Scales.Any(s => s.Ip == ip && s.Port == port))
                    continue;

                var scale = new Scale
                {
                    Name = $"Scale {ip}",
                    Ip = ip,
                    Port = port
                };

                _scaleManager.Scales.Add(scale);
                _hasChanges = true;
            }

            if (_hasChanges)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                MessageBox.Show("Не выбрано ни одного нового устройства для добавления.", "Автопоиск", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
