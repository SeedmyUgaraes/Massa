using System;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Reflection;
using Krypton.Toolkit;
using MassaKWin.Core;
using MassaKWin.Ui;

namespace MassaKWin
{
    public partial class ScaleDiscoveryForm : KryptonForm
    {
        private readonly ScaleManager _scaleManager;

        private KryptonTextBox _txtFromIp = null!;
        private KryptonTextBox _txtToIp = null!;
        private KryptonButton _btnScan = null!;
        private KryptonButton _btnStop = null!;
        private KryptonButton _btnAddSelected = null!;
        private DataGridView _dgvResults = null!;
        private ProgressBar _progressBar = null!;
        private Label _lblStatus = null!;

        private CancellationTokenSource? _cts;
        private bool _hasChanges;
        private readonly GlobalSettings _settings;
        private readonly int _probePort;
        private readonly int _connectTimeoutMs;
        private readonly int _parallelConnections;

        public ScaleDiscoveryForm(ScaleManager scaleManager, GlobalSettings settings)
        {
            _scaleManager = scaleManager;
            _settings = settings;
            _probePort = settings.DefaultScalePort;
            _connectTimeoutMs = settings.ScanIpTimeoutMs;
            _parallelConnections = Math.Max(1, settings.ScanParallelConnections);
            InitializeComponent();
            ThemeManager.Apply(this);
        }

        #region UI

        private void InitializeComponent()
        {
            Text = "Автопоиск весов";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new System.Drawing.Size(760, 460);
            Padding = new Padding(8);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var header = new TableLayoutPanel
            {
                ColumnCount = 5,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Padding = new Padding(8),
                Margin = new Padding(0, 0, 0, 8)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var lblFrom = new Label { Text = "IP от:", AutoSize = true, Anchor = AnchorStyles.Left };
            _txtFromIp = new KryptonTextBox { Width = 140, Text = _settings.AutoDiscoveryIpStart, Anchor = AnchorStyles.Left };
            var lblTo = new Label { Text = "IP до:", AutoSize = true, Anchor = AnchorStyles.Left };
            _txtToIp = new KryptonTextBox { Width = 140, Text = _settings.AutoDiscoveryIpEnd, Anchor = AnchorStyles.Left };

            _btnScan = new KryptonButton { Text = "Сканировать", AutoSize = true, MinimumSize = new Size(120, 34) };
            _btnScan.Click += OnScanClicked;
            _btnStop = new KryptonButton { Text = "Стоп", AutoSize = true, MinimumSize = new Size(90, 34), Enabled = false };
            _btnStop.Click += OnStopClicked;

            var buttonsPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Anchor = AnchorStyles.Right
            };
            buttonsPanel.Controls.Add(_btnScan);
            buttonsPanel.Controls.Add(_btnStop);

            header.Controls.Add(lblFrom, 0, 0);
            header.Controls.Add(_txtFromIp, 1, 0);
            header.Controls.Add(lblTo, 2, 0);
            header.Controls.Add(_txtToIp, 3, 0);
            header.Controls.Add(buttonsPanel, 4, 0);

            _dgvResults = new DataGridView
            {
                Dock = DockStyle.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false
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

            var colName = new DataGridViewTextBoxColumn
            {
                Name = "ScaleName",
                HeaderText = "Имя весов",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            };

            var colStatus = new DataGridViewTextBoxColumn
            {
                Name = "Status",
                HeaderText = "Статус",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            };

            _dgvResults.Columns.AddRange(colSelected, colIp, colPort, colName, colStatus);
            StyleResultsGrid();

            var footer = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 8, 8, 0)
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var statusPanel = new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill
            };

            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 18,
                Margin = new Padding(0, 0, 0, 6)
            };

            _lblStatus = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Text = "Готово."
            };

            statusPanel.Controls.Add(_progressBar, 0, 0);
            statusPanel.Controls.Add(_lblStatus, 0, 1);

            _btnAddSelected = new KryptonButton
            {
                Text = "Добавить выделенные в весы",
                AutoSize = true,
                MinimumSize = new Size(240, 34),
                Anchor = AnchorStyles.Right
            };
            _btnAddSelected.Click += OnAddSelectedClicked;

            footer.Controls.Add(statusPanel, 0, 0);
            footer.Controls.Add(_btnAddSelected, 1, 0);

            root.Controls.Add(header, 0, 0);
            root.Controls.Add(_dgvResults, 0, 1);
            root.Controls.Add(footer, 0, 2);

            Controls.Add(root);
        }

        private void StyleResultsGrid()
        {
            _dgvResults.RowHeadersVisible = false;
            _dgvResults.BackgroundColor = Color.White;
            _dgvResults.BorderStyle = BorderStyle.None;
            _dgvResults.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            _dgvResults.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            _dgvResults.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(245, 247, 250);
            _dgvResults.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(50, 50, 50);
            _dgvResults.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
            _dgvResults.DefaultCellStyle.SelectionBackColor = Color.FromArgb(227, 235, 250);
            _dgvResults.DefaultCellStyle.SelectionForeColor = Color.Black;
            _dgvResults.RowTemplate.Height = 28;

            var prop = typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            prop?.SetValue(_dgvResults, true);
        }

        #endregion

        #region Сканирование

        private async void OnScanClicked(object? sender, EventArgs e)
        {
            if (_cts != null)
            {
                MessageBox.Show(
                    "Сканирование уже выполняется.",
                    "Автопоиск",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!TryParseIpRange(_txtFromIp.Text, _txtToIp.Text, out var fromIp, out var toIp))
            {
                MessageBox.Show(
                    "Некорректный диапазон IP.",
                    "Ошибка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
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
                MessageBox.Show(
                    $"Ошибка сканирования: {ex.Message}",
                    "Ошибка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
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

        private bool TryParseIpRange(string fromText, string toText,
            out IPAddress fromIp, out IPAddress toIp)
        {
            fromIp = IPAddress.None;
            toIp = IPAddress.None;

            if (!IPAddress.TryParse(fromText, out fromIp))
                return false;
            if (!IPAddress.TryParse(toText, out toIp))
                return false;

            var fb = fromIp.GetAddressBytes();
            var tb = toIp.GetAddressBytes();
            if (fb.Length != 4 || tb.Length != 4)
                return false;

            uint fromVal = (uint)(fb[0] << 24 | fb[1] << 16 | fb[2] << 8 | fb[3]);
            uint toVal = (uint)(tb[0] << 24 | tb[1] << 16 | tb[2] << 8 | tb[3]);

            if (fromVal > toVal)
                return false;

            return true;
        }

        private async Task ScanRangeAsync(IPAddress fromIp, IPAddress toIp, CancellationToken ct)
        {
            var fb = fromIp.GetAddressBytes();
            var tb = toIp.GetAddressBytes();

            uint fromVal = (uint)(fb[0] << 24 | fb[1] << 16 | fb[2] << 8 | fb[3]);
            uint toVal = (uint)(tb[0] << 24 | tb[1] << 16 | tb[2] << 8 | tb[3]);

            int total = (int)(toVal - fromVal + 1);
            if (total <= 0) total = 1;

            _progressBar.Minimum = 0;
            _progressBar.Maximum = total;
            int processed = 0;
            using var semaphore = new SemaphoreSlim(_parallelConnections);
            var tasks = new List<Task>();

            for (uint val = fromVal; val <= toVal; val++)
            {
                ct.ThrowIfCancellationRequested();

                var b1 = (byte)((val >> 24) & 0xFF);
                var b2 = (byte)((val >> 16) & 0xFF);
                var b3 = (byte)((val >> 8) & 0xFF);
                var b4 = (byte)(val & 0xFF);
                var ip = new IPAddress(new[] { b1, b2, b3, b4 });
                var ipText = ip.ToString();

                await semaphore.WaitAsync(ct);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var (isScale, name) = await ProbeHostAndReadNameAsync(ipText, _probePort, _connectTimeoutMs, ct);
                        if (isScale)
                        {
                            string status = string.IsNullOrWhiteSpace(name)
                                ? "Найдено устройство, имя не прочитано"
                                : "Найдены весы Massa-K";

                            void addRow()
                            {
                                AddResultRow(ipText, _probePort, name ?? string.Empty, status);
                            }

                            if (InvokeRequired)
                                BeginInvoke((Action)addRow);
                            else
                                addRow();
                        }
                    }
                    finally
                    {
                        var processedLocal = Interlocked.Increment(ref processed);
                        if (InvokeRequired)
                        {
                            BeginInvoke(new Action(() =>
                            {
                                if (processedLocal <= _progressBar.Maximum)
                                    _progressBar.Value = processedLocal;
                                _lblStatus.Text = $"Просканировано {processedLocal}/{total}";
                            }));
                        }
                        else
                        {
                            if (processedLocal <= _progressBar.Maximum)
                                _progressBar.Value = processedLocal;
                            _lblStatus.Text = $"Просканировано {processedLocal}/{total}";
                        }

                        semaphore.Release();
                    }
                }, ct));
            }

            await Task.WhenAll(tasks);
        }

        #endregion

        #region TCP + протокол Massa-K

        /// <summary>
        /// Строим запрос CMD_GET_MASSA (0x23) по протоколу 100.
        /// Нужен только чтобы убедиться, что это именно наши весы.
        /// </summary>
        private byte[] BuildGetMassaRequest()
        {
            byte[] header = { 0xF8, 0x55, 0xCE };
            byte[] len = { 0x01, 0x00 };   // тело = 1 байт (Command)
            byte cmd = 0x23;              // CMD_GET_MASSA

            var buf = new byte[header.Length + len.Length + 1 + 2];

            Buffer.BlockCopy(header, 0, buf, 0, header.Length);
            Buffer.BlockCopy(len, 0, buf, header.Length, len.Length);

            int idx = header.Length + len.Length;
            buf[idx] = cmd;

            ushort crc = ComputeCrc16(new[] { cmd });
            buf[idx + 1] = (byte)(crc & 0xFF);
            buf[idx + 2] = (byte)(crc >> 8);

            return buf;
        }

        /// <summary>
        /// Строим запрос CMD_GET_NAME (0x20) – читаем имя весов, если поддерживается.
        /// </summary>
        private byte[] BuildGetNameRequest()
        {
            byte[] header = { 0xF8, 0x55, 0xCE };
            byte[] len = { 0x01, 0x00 };   // тело = 1 байт (Command)
            byte cmd = 0x20;               // CMD_GET_NAME

            var buf = new byte[header.Length + len.Length + 1 + 2];

            Buffer.BlockCopy(header, 0, buf, 0, header.Length);
            Buffer.BlockCopy(len, 0, buf, header.Length, len.Length);

            int idx = header.Length + len.Length;
            buf[idx] = cmd;

            ushort crc = ComputeCrc16(new[] { cmd });
            buf[idx + 1] = (byte)(crc & 0xFF);
            buf[idx + 2] = (byte)(crc >> 8);

            return buf;
        }

        /// <summary>
        /// Читает ровно count байт или бросает IOException.
        /// </summary>
        private async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, CancellationToken token)
        {
            var buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = await stream.ReadAsync(buffer, offset, count - offset, token);
                if (read == 0)
                    throw new IOException("Connection closed");
                offset += read;
            }
            return buffer;
        }

        /// <summary>
        /// Чтение одного пакетa протокола Massa-K:
        /// F8 55 CE | len(lo,hi) | payload[len] | crc(lo,hi)
        /// </summary>
        private async Task<byte[]> ReadPacketAsync(NetworkStream stream, CancellationToken token)
        {
            var header = await ReadExactAsync(stream, 3, token);
            if (header[0] != 0xF8 || header[1] != 0x55 || header[2] != 0xCE)
                throw new IOException("Invalid header");

            var lenBuf = await ReadExactAsync(stream, 2, token);
            int payloadLen = lenBuf[0] | (lenBuf[1] << 8);

            var payloadPlusCrc = await ReadExactAsync(stream, payloadLen + 2, token);
            var payload = new byte[payloadLen];
            Buffer.BlockCopy(payloadPlusCrc, 0, payload, 0, payloadLen);

            ushort expectedCrc = ComputeCrc16(payload);
            ushort actualCrc = (ushort)(payloadPlusCrc[payloadLen] | (payloadPlusCrc[payloadLen + 1] << 8));
            if (expectedCrc != actualCrc)
                throw new IOException("CRC mismatch");

            return payload;
        }

        /// <summary>
        /// Чтение одного пакетa протокола Massa-K без проверки CRC.
        /// F8 55 CE | len(lo,hi) | payload[len] | crc(lo,hi)
        /// CRC-байты читаем из потока, но не сравниваем.
        /// </summary>
        private async Task<byte[]> ReadPacketNoCrcAsync(NetworkStream stream, CancellationToken token)
        {
            var header = await ReadExactAsync(stream, 3, token);
            if (header[0] != 0xF8 || header[1] != 0x55 || header[2] != 0xCE)
                throw new IOException("Invalid header");

            var lenBuf = await ReadExactAsync(stream, 2, token);
            int payloadLen = lenBuf[0] | (lenBuf[1] << 8);

            if (payloadLen < 1 || payloadLen > 256)
                throw new IOException("Invalid payload length");

            var payloadPlusCrc = await ReadExactAsync(stream, payloadLen + 2, token);
            var payload = new byte[payloadLen];
            Buffer.BlockCopy(payloadPlusCrc, 0, payload, 0, payloadLen);

            return payload;
        }

        /// <summary>
        /// CRC-16-CCITT (0x1021), seed 0xFFFF.
        /// Должен совпадать с тем, что используется в MassaKClient.
        /// </summary>
        private ushort ComputeCrc16(ReadOnlySpan<byte> data)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= (ushort)(data[i] << 8);
                for (int j = 0; j < 8; j++)
                {
                    crc = (crc & 0x8000) != 0
                        ? (ushort)((crc << 1) ^ 0x1021)
                        : (ushort)(crc << 1);
                }
            }
            return crc;
        }

        /// <summary>
        /// Пробуем подключиться к IP, проверить, что это весы (CMD_GET_MASSA),
        /// и, если возможно, дочитать имя (CMD_GET_NAME).
        /// Возвращаем (isScale, nameOrNull).
        /// </summary>
        private async Task<(bool isScale, string? name)> ProbeHostAndReadNameAsync(
            string ip,
            int port,
            int timeoutMs,
            CancellationToken ct)
        {
            try
            {
                using var client = new TcpClient();

                // Подключаемся с таймаутом
                var connectTask = client.ConnectAsync(ip, port);
                var timeoutTask = Task.Delay(timeoutMs, ct);

                var completed = await Task.WhenAny(connectTask, timeoutTask);
                if (completed != connectTask || !client.Connected)
                    return (false, null); // не успели подключиться

                var stream = client.GetStream();

                // 1. Проверяем, что это вообще наши весы — CMD_GET_MASSA
                using (var massaCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    massaCts.CancelAfter(timeoutMs);

                    var massaReq = BuildGetMassaRequest();
                    await stream.WriteAsync(massaReq, 0, massaReq.Length, massaCts.Token);
                    await stream.FlushAsync(massaCts.Token);

                    var massaPayload = await ReadPacketNoCrcAsync(stream, massaCts.Token);
                    if (massaPayload.Length == 0 || (massaPayload[0] != 0x24 && massaPayload[0] != 0x28))
                        return (false, null);
                }

                // На этом этапе мы уже точно знаем, что это наши весы
                // 2. Пробуем прочитать имя, если поддерживается
                try
                {
                    using var nameCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    nameCts.CancelAfter(timeoutMs);

                    var nameReq = BuildGetNameRequest();
                    await stream.WriteAsync(nameReq, 0, nameReq.Length, nameCts.Token);
                    await stream.FlushAsync(nameCts.Token);

                    var namePayload = await ReadPacketNoCrcAsync(stream, nameCts.Token);
                    if (namePayload.Length < 5 || namePayload.Length > 128)
                        return (true, null);

                    if (namePayload[0] != 0x21) // CMD_ACK_NAME
                        return (true, null); // весы есть, имя не получили

                    int nameLen = namePayload.Length - 5; // 1 (cmd) + 4 (ScalesID)
                    var nameBytes = new byte[nameLen];
                    Buffer.BlockCopy(namePayload, 5, nameBytes, 0, nameLen);

                    // убираем CRLF в конце, если есть
                    int actualLen = nameLen;
                    if (actualLen >= 2 &&
                        nameBytes[actualLen - 2] == 0x0D &&
                        nameBytes[actualLen - 1] == 0x0A)
                    {
                        actualLen -= 2;
                    }

                    if (actualLen <= 0)
                        return (true, null);

                    string name;
                    try
                    {
                        name = Encoding.GetEncoding(1251).GetString(nameBytes, 0, actualLen);
                    }
                    catch
                    {
                        name = Encoding.UTF8.GetString(nameBytes, 0, actualLen);
                    }

                    return (true, name.Trim());
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Локальный таймаут именно на чтении имени — весы считаем найденными, имени просто нет
                    return (true, null);
                }
                catch
                {
                    // Любая ошибка при запросе имени — весы считаем найденными, но без имени
                    return (true, null);
                }
            }
            // Локальные таймауты на подключение/опрос — просто «не наши» узлы
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return (false, null);
            }
            // Глобальная отмена (_cts.Cancel()) — реально прерываем сканирование
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Любая другая ошибка — не наши весы
                return (false, null);
            }
        }

        #endregion


        #region Добавление весов

        private void AddResultRow(string ip, int port, string name, string status)
        {
            int rowIndex = _dgvResults.Rows.Add(false, ip, port, name, status);
            _dgvResults.Rows[rowIndex].Tag = ip;
        }

        private void OnAddSelectedClicked(object? sender, EventArgs e)
        {
            foreach (DataGridViewRow row in _dgvResults.Rows)
            {
                var cellSelected = row.Cells["Selected"].Value;
                bool selected = cellSelected is bool b && b;
                if (!selected)
                    continue;

                string? ip = row.Cells["Ip"].Value?.ToString();
                if (string.IsNullOrWhiteSpace(ip))
                    continue;

                int port = _probePort;

                if (_scaleManager.Scales.Any(s => s.Ip == ip && s.Port == port))
                    continue;

                string? name = row.Cells["ScaleName"].Value?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                    name = $"Scale {ip}";

                var scale = new Scale
                {
                    Name = name,
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
                MessageBox.Show(
                    "Не выбрано ни одного нового устройства для добавления.",
                    "Автопоиск",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        #endregion
    }
}
