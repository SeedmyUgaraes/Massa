using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
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

        private const int ProbePort = 5000;
        private const int ConnectTimeoutMs = 400;

        public ScaleDiscoveryForm(ScaleManager scaleManager)
        {
            _scaleManager = scaleManager;
            InitializeComponent();
        }

        #region UI

        private void InitializeComponent()
        {
            Text = "Автопоиск весов";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new System.Drawing.Size(720, 420);

            var panelTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 60,
                Padding = new Padding(10)
            };

            var lblFrom = new Label
            {
                Text = "IP от:",
                AutoSize = true,
                Location = new System.Drawing.Point(10, 12)
            };

            _txtFromIp = new TextBox
            {
                Location = new System.Drawing.Point(60, 9),
                Width = 140,
                Text = "192.168.0.1"
            };

            var lblTo = new Label
            {
                Text = "IP до:",
                AutoSize = true,
                Location = new System.Drawing.Point(220, 12)
            };

            _txtToIp = new TextBox
            {
                Location = new System.Drawing.Point(270, 9),
                Width = 140,
                Text = "192.168.0.254"
            };

            _btnScan = new Button
            {
                Text = "Сканировать",
                Location = new System.Drawing.Point(430, 7),
                Width = 110
            };
            _btnScan.Click += OnScanClicked;

            _btnStop = new Button
            {
                Text = "Стоп",
                Location = new System.Drawing.Point(550, 7),
                Width = 80,
                Enabled = false
            };
            _btnStop.Click += OnStopClicked;

            panelTop.Controls.Add(lblFrom);
            panelTop.Controls.Add(_txtFromIp);
            panelTop.Controls.Add(lblTo);
            panelTop.Controls.Add(_txtToIp);
            panelTop.Controls.Add(_btnScan);
            panelTop.Controls.Add(_btnStop);

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

            var panelBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 70,
                Padding = new Padding(10)
            };

            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 18
            };

            _lblStatus = new Label
            {
                Dock = DockStyle.Top,
                Height = 18,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Text = "Готово."
            };

            _btnAddSelected = new Button
            {
                Text = "Добавить выделенные в весы",
                Dock = DockStyle.Right,
                Width = 220
            };
            _btnAddSelected.Click += OnAddSelectedClicked;

            panelBottom.Controls.Add(_btnAddSelected);
            panelBottom.Controls.Add(_lblStatus);
            panelBottom.Controls.Add(_progressBar);

            Controls.Add(_dgvResults);
            Controls.Add(panelBottom);
            Controls.Add(panelTop);
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

            for (uint val = fromVal; val <= toVal; val++)
            {
                ct.ThrowIfCancellationRequested();

                var b1 = (byte)((val >> 24) & 0xFF);
                var b2 = (byte)((val >> 16) & 0xFF);
                var b3 = (byte)((val >> 8) & 0xFF);
                var b4 = (byte)(val & 0xFF);
                var ip = new IPAddress(new[] { b1, b2, b3, b4 });

                var (isScale, name) = await ProbeHostAndReadNameAsync(ip.ToString(), ProbePort, ConnectTimeoutMs, ct);
                processed++;

                if (processed <= _progressBar.Maximum)
                    _progressBar.Value = processed;
                _lblStatus.Text = $"Просканировано {processed}/{total}";

                if (isScale)
                {
                    string status = string.IsNullOrWhiteSpace(name)
                        ? "Найдено устройство, имя не прочитано"
                        : "Найдены весы Massa-K";

                    void addRow()
                    {
                        AddResultRow(ip.ToString(), ProbePort, name ?? string.Empty, status);
                    }

                    if (InvokeRequired)
                        BeginInvoke((Action)addRow);
                    else
                        addRow();
                }
            }
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

                // Для чтения/записи делаем локальный токен с таймаутом
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linkedCts.CancelAfter(timeoutMs);

                // 1. Проверяем, что это вообще наши весы — CMD_GET_MASSA
                var massaReq = BuildGetMassaRequest();
                await stream.WriteAsync(massaReq, 0, massaReq.Length, linkedCts.Token);
                await stream.FlushAsync(linkedCts.Token);

                var massaPayload = await ReadPacketNoCrcAsync(stream, linkedCts.Token);
                if (massaPayload.Length == 0 || massaPayload[0] != 0x24) // CMD_ACK_MASSA
                    return (false, null);

                // На этом этапе мы уже точно знаем, что это наши весы
                // 2. Пробуем прочитать имя, если поддерживается
                try
                {
                    var nameReq = BuildGetNameRequest();
                    await stream.WriteAsync(nameReq, 0, nameReq.Length, linkedCts.Token);
                    await stream.FlushAsync(linkedCts.Token);

                    var namePayload = await ReadPacketNoCrcAsync(stream, linkedCts.Token);
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

                int port = ProbePort;

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
