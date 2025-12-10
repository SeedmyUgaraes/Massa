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
    /// В этом стартере реализован только каркас. Логику протокола можно перенести из существующей консольной программы.
    /// </summary>
    public class MassaKClient
    {
        private readonly IList<Scale> _scales;
        private readonly TimeSpan _pollInterval;
        private readonly TimeSpan _connectTimeout;
        private readonly TimeSpan _offlineThreshold;
        private readonly TimeSpan _reconnectDelay;

        private readonly Dictionary<Guid, Task> _tasks = new();
        private readonly Dictionary<Guid, CancellationTokenSource> _tokens = new();

        public event Action<Scale>? ScaleUpdated;

        public MassaKClient(
            IList<Scale> scales,
            TimeSpan pollInterval,
            TimeSpan connectTimeout,
            TimeSpan offlineThreshold,
            TimeSpan reconnectDelay)
        {
            _scales = scales;
            _pollInterval = pollInterval;
            _connectTimeout = connectTimeout;
            _offlineThreshold = offlineThreshold;
            _reconnectDelay = reconnectDelay;
        }

        public void Start()
        {
            foreach (var scale in _scales)
            {
                if (_tasks.ContainsKey(scale.Id))
                    continue;

                var cts = new CancellationTokenSource();
                _tokens[scale.Id] = cts;
                _tasks[scale.Id] = Task.Run(() => PollLoopAsync(scale, cts.Token));
            }
        }

        public async Task StopAsync()
        {
            foreach (var kv in _tokens)
            {
                kv.Value.Cancel();
            }

            await Task.WhenAll(_tasks.Values);
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

                    while (!token.IsCancellationRequested)
                    {
                        await stream.WriteAsync(BuildGetMassaRequest(), 0, 8, token);

                        var payload = await ReadPacketAsync(stream, token);
                        ParsePacket(scale, payload);
                        ScaleUpdated?.Invoke(scale);

                        await Task.Delay(_pollInterval, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                }
                finally
                {
                    try { stream?.Close(); } catch { }
                    try { client?.Close(); } catch { }
                }

                if (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(_reconnectDelay, token);
                    }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

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
            var header = await ReadExactAsync(stream, 3, token);
            if (header[0] != 0xF8 || header[1] != 0x55 || header[2] != 0xCE)
                throw new IOException("Invalid header");

            var lenBuf = await ReadExactAsync(stream, 2, token);
            int payloadLen = BitConverter.ToInt16(lenBuf, 0);

            var payloadCrc = await ReadExactAsync(stream, payloadLen + 2, token);
            var payload = new byte[payloadLen];
            Buffer.BlockCopy(payloadCrc, 0, payload, 0, payloadLen);

            ushort expectedCrc = ComputeCrc16(payload);
            ushort actualCrc = (ushort)(payloadCrc[payloadLen] | (payloadCrc[payloadLen + 1] << 8));
            if (expectedCrc != actualCrc)
                throw new IOException("CRC mismatch");

            return payload;
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

            if (scale.Protocol == ScaleProtocol.WithTare && payload.Length >= offset + 9 + 4)
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

        private async Task ConnectWithTimeoutAsync(TcpClient client, string host, int port, TimeSpan timeout, CancellationToken token)
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

        private ushort ComputeCrc16(IReadOnlyList<byte> data)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < data.Count; i++)
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
    }
}
