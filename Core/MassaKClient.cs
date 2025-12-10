using System;
using System.Collections.Generic;
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
            // Здесь сейчас только заготовка.
            // Сюда можно перенести код из текущей консольной программы:
            // подключение к TCP, отправка CMD_GET_MASSA, разбор ответа, автоопределение протокола.
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // TODO: реализовать подключение и обмен по протоколу 100
                    await Task.Delay(_pollInterval, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Логирование ошибки и задержка перед переподключением
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
        }

        // Дополнительно можно здесь реализовать методы CRC16 и парсинг пакета,
        // либо вынести их в отдельный утилитный класс.
    }
}
