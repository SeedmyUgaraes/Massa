using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MassaKWin.Core
{
    /// <summary>
    /// Клиент для опроса весов Massa-K по протоколу 100.
    /// </summary>
    public class MassaKClient
    {
        private readonly IList<Scale> _scales;
        private readonly TimeSpan _pollInterval;
        private readonly TimeSpan _connectTimeout;
        private readonly TimeSpan _offlineThreshold;
        private readonly TimeSpan _reconnectDelay;
        private readonly TimeSpan _responseTimeout;
        private readonly double _deadbandGrams;
        private readonly bool _autoZeroOnConnect;
        private readonly HashSet<Guid> _offlineLogged = new();
        private readonly object _offlineLock = new object();

        private readonly Dictionary<Guid, Task> _tasks = new();
        private readonly Dictionary<Guid, CancellationTokenSource> _tokens = new();

        public event Action<Scale>? ScaleUpdated;
        public event Action<string>? LogMessage;

        public MassaKClient(
            IList<Scale> scales,
            TimeSpan pollInterval,
            TimeSpan connectTimeout,
            TimeSpan offlineThreshold,
            TimeSpan reconnectDelay,
            double deadbandGrams,
            bool autoZeroOnConnect)
        {
            _scales = scales ?? throw new ArgumentNullException(nameof(scales));
            _pollInterval = pollInterval;
            _connectTimeout = connectTimeout;
            _offlineThreshold = offlineThreshold;
            _reconnectDelay = reconnectDelay;
            _responseTimeout = connectTimeout;
            _deadbandGrams = deadbandGrams;
            _autoZeroOnConnect = autoZeroOnConnect;
        }

        public void Start()
        {
            foreach (var scale in _scales)
            {
                if (_tasks.TryGetValue(scale.Id, out var existingTask))
                {
                    if (existingTask.IsCompleted || existingTask.IsCanceled || existingTask.IsFaulted)
                    {
                        _tasks.Remove(scale.Id);
                        if (_tokens.TryGetValue(scale.Id, out var existingCts))
                        {
                            existingCts.Cancel();
                            existingCts.Dispose();
                            _tokens.Remove(scale.Id);
                        }
                    }
                    else
                    {
                        continue;
                    }
                }

                var cts = new CancellationTokenSource();
                var task = Task.Run(() => PollLoopAsync(scale, cts.Token), cts.Token);

                _tokens[scale.Id] = cts;
                _tasks[scale.Id] = task;
            }
        }

        public async Task StopAsync()
        {
            foreach (var kv in _tokens)
            {
                kv.Value.Cancel();
            }

            try
            {
                if (_tasks.Count > 0)
                    await Task.WhenAll(_tasks.Values);
            }
            catch
            {
                // игнорируем ошибки при остановке
            }

            _tasks.Clear();
            _tokens.Clear();
        }

        private async Task PollLoopAsync(Scale scale, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient? client = null;
                NetworkStream? stream = null;

                try
                {
                    client = new TcpClient();
                    await ConnectWithTimeoutAsync(client, scale.Ip, scale.Port, _connectTimeout, token);
                    stream = client.GetStream();
                    MarkScaleOnline(scale.Id);

                    LogMessage?.Invoke(
                        $"[{DateTime.Now:HH:mm:ss}] Подключение к весам \"{scale.Name}\" ({scale.Ip}:{scale.Port}) успешно.");

                    if (_autoZeroOnConnect)
                    {
                        scale.State.NetGrams = 0;
                        scale.State.TareGrams = 0;
                        scale.State.LastUpdateUtc = DateTime.UtcNow;
                        LogMessage?.Invoke(
                            $"[{DateTime.Now:HH:mm:ss}] Авто-ноль выполнен для \"{scale.Name}\" после подключения.");
                    }

                    while (!token.IsCancellationRequested)
                    {
                        var request = BuildGetMassaRequest();
                        using var responseCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                        responseCts.CancelAfter(_responseTimeout);

                        await stream.WriteAsync(request, 0, request.Length, responseCts.Token);

                        var previousNet = scale.State.NetGrams;
                        var previousTare = scale.State.TareGrams;
                        var previousStable = scale.State.Stable;
                        var previousProtocol = scale.Protocol;
                        var previousNetFlag = scale.State.NetFlag;
                        var previousZeroFlag = scale.State.ZeroFlag;
                        var previousLastUpdate = scale.State.LastUpdateUtc;

                        var payload = await ReadPacketAsync(stream, responseCts.Token);
                        ParsePacket(scale, payload);

                        var netChanged = Math.Abs(scale.State.NetGrams - previousNet) > _deadbandGrams;
                        var tareChanged = Math.Abs(scale.State.TareGrams - previousTare) > _deadbandGrams;
                        var stableChanged = previousStable != scale.State.Stable;
                        var protocolChanged = previousProtocol != scale.Protocol;
                        var netFlagChanged = previousNetFlag != scale.State.NetFlag;
                        var zeroFlagChanged = previousZeroFlag != scale.State.ZeroFlag;
                        var firstUpdate = previousLastUpdate == default;

                        if (netChanged || tareChanged || stableChanged || protocolChanged || netFlagChanged || zeroFlagChanged || firstUpdate)
                        {
                            ScaleUpdated?.Invoke(scale);
                        }

                        await Task.Delay(_pollInterval, token);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // нормальное завершение
                    break;
                }
                catch (OperationCanceledException ex)
                {
                    // This is an I/O timeout from responseCts, not app shutdown.
                    _ = ex;
                    LogOfflineOnce(scale,
                        $"[{DateTime.Now:HH:mm:ss}] Timeout from scale \"{scale.Name}\" ({scale.Ip}:{scale.Port}). Reconnecting...");
                }
                catch (Exception ex)
                {
                    LogOfflineOnce(scale,
                        $"[{DateTime.Now:HH:mm:ss}] Ошибка опроса весов \"{scale.Name}\" ({scale.Ip}:{scale.Port}): {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    try { stream?.Close(); } catch { }
                    try { client?.Close(); } catch { }
                }

                if (token.IsCancellationRequested)
                    break;

                try
                {
                    await Task.Delay(_reconnectDelay, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Формирует запрос CMD_GET_MASSA (0x23) по протоколу 100.
        /// Заголовок: F8 55 CE, длина: 0x0001, тело: 0x23, CRC по телу.
        /// </summary>
        private byte[] BuildGetMassaRequest()
        {
            byte[] header = { 0xF8, 0x55, 0xCE };
            byte[] len = { 0x01, 0x00 };
            byte cmd = 0x23;

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

        private async Task<byte[]> ReadPacketAsync(NetworkStream stream, CancellationToken token)
        {
            // читаем заголовок
            var header = await ReadExactAsync(stream, 3, token);
            if (header[0] != 0xF8 || header[1] != 0x55 || header[2] != 0xCE)
                throw new IOException("Invalid header");

            // читаем длину (little-endian)
            var lenBytes = await ReadExactAsync(stream, 2, token);
            ushort payloadLen = (ushort)(lenBytes[0] | (lenBytes[1] << 8));
            if (payloadLen == 0)
                throw new IOException("Invalid payload length");

            // читаем тело + CRC
            var payloadWithCrc = await ReadExactAsync(stream, payloadLen + 2, token);

            var payload = new byte[payloadLen];
            Buffer.BlockCopy(payloadWithCrc, 0, payload, 0, payloadLen);

            ushort receivedCrc = (ushort)(payloadWithCrc[payloadLen] | (payloadWithCrc[payloadLen + 1] << 8));
            ushort computedCrc = ComputeCrc16(payload);

            if (receivedCrc != computedCrc)
                throw new IOException("CRC mismatch");

            return payload;
        }

        private async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken token)
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

        private void ParsePacket(Scale scale, byte[] payload)
        {
            if (payload.Length < 9)
                throw new IOException("Payload too short");
            if (payload[0] != 0x24)
                throw new IOException("Unexpected command");

            if (scale.Protocol == ScaleProtocol.Unknown)
            {
                scale.Protocol = payload.Length >= 13
                    ? ScaleProtocol.WithTare
                    : ScaleProtocol.WithoutTare;
            }

            int offset = 1;

            int rawMass = BitConverter.ToInt32(payload, offset);
            byte division = payload[offset + 4];
            bool stable = payload[offset + 5] == 1;
            bool netFlag = payload[offset + 6] == 1;
            bool zeroFlag = payload[offset + 7] == 1;

            double factor = DivisionToFactor(division);
            double measured = rawMass * factor;
            double tareValue = 0.0;

            if (_deadbandGrams > 0 && Math.Abs(measured - scale.State.NetGrams) <= _deadbandGrams)
            {
                measured = scale.State.NetGrams;
            }

            if (scale.Protocol == ScaleProtocol.WithTare && payload.Length >= offset + 8 + 4)
            {
                int rawTare = BitConverter.ToInt32(payload, offset + 8);
                tareValue = rawTare * factor;
            }

            scale.State.NetGrams = measured;
            scale.State.TareGrams = tareValue;
            scale.State.Stable = stable;
            scale.State.NetFlag = netFlag;
            scale.State.ZeroFlag = zeroFlag;
            scale.State.LastUpdateUtc = DateTime.UtcNow;
        }

        private double DivisionToFactor(byte division)
        {
            return division switch
            {
                0 => 0.1,
                1 => 1.0,
                2 => 10.0,
                3 => 100.0,
                4 => 1000.0,
                _ => 1.0
            };
        }

        private async Task ConnectWithTimeoutAsync(
            TcpClient client,
            string host,
            int port,
            TimeSpan timeout,
            CancellationToken token)
        {
            var connectTask = client.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(timeout, token));
            if (completed != connectTask)
            {
                client.Close();
                throw new TimeoutException();
            }

            await connectTask;
        }

        /// <summary>
        /// CRC16 по алгоритму из мануала Massa-K (начальное значение 0, полином 0x1021).
        /// </summary>
        private ushort ComputeCrc16(IReadOnlyList<byte> data)
        {
            ushort crc = 0;

            for (int k = 0; k < data.Count; k++)
            {
                ushort a = 0;
                ushort temp = (ushort)((crc >> 8) << 8);

                for (int bits = 0; bits < 8; bits++)
                {
                    if (((temp ^ a) & 0x8000) != 0)
                    {
                        a = (ushort)(((a << 1) ^ 0x1021) & 0xFFFF);
                    }
                    else
                    {
                        a = (ushort)((a << 1) & 0xFFFF);
                    }

                    temp = (ushort)((temp << 1) & 0xFFFF);
                }

                crc = (ushort)(a ^ ((crc << 8) & 0xFFFF) ^ (data[k] & 0xFF));
            }

            return crc;
        }

        private void MarkScaleOnline(Guid scaleId)
        {
            lock (_offlineLock)
            {
                _offlineLogged.Remove(scaleId);
            }
        }

        private void LogOfflineOnce(Scale scale, string message)
        {
            bool shouldLog;
            lock (_offlineLock)
            {
                shouldLog = _offlineLogged.Add(scale.Id);
            }

            if (shouldLog)
            {
                LogMessage?.Invoke(message);
            }
        }
    }
}
