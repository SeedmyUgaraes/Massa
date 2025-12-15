using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MassaKWin.Core
{
    public class CameraOsdService
    {
        private readonly IList<Camera> _cameras;
        private readonly ScaleManager _scaleManager;
        private readonly TimeSpan _updateInterval;
        private readonly GlobalSettings _settings;
        private readonly WeightUnit _weightUnit;
        private readonly int _decimals;
        private readonly Dictionary<Guid, string> _lastStatus;
        private readonly Dictionary<Guid, HikvisionOsdClient> _clients;
        private readonly Dictionary<Guid, Task> _tasks;
        private readonly Dictionary<Guid, CancellationTokenSource> _tokens;
        private readonly object _statusLock = new();

        public event Action<string>? LogMessage;

        public CameraOsdService(
            IList<Camera> cameras,
            ScaleManager scaleManager,
            TimeSpan updateInterval,
            GlobalSettings settings,
            WeightUnit weightUnit,
            int decimals)
        {
            _cameras = cameras;
            _scaleManager = scaleManager;
            _updateInterval = updateInterval < TimeSpan.FromMilliseconds(100)
                ? TimeSpan.FromMilliseconds(100)
                : updateInterval;
            _settings = settings;
            _weightUnit = weightUnit;
            _decimals = decimals;

            _lastStatus = new Dictionary<Guid, string>();
            _clients = new Dictionary<Guid, HikvisionOsdClient>();
            _tasks = new Dictionary<Guid, Task>();
            _tokens = new Dictionary<Guid, CancellationTokenSource>();
        }

        public void Start()
        {
            foreach (var camera in _cameras)
            {
                if (_tasks.ContainsKey(camera.Id))
                    continue;

                var cts = new CancellationTokenSource();
                _tokens[camera.Id] = cts;
                _clients[camera.Id] = new HikvisionOsdClient(camera.Username, camera.Password);
                _tasks[camera.Id] = Task.Run(() => RunCameraLoopAsync(camera, cts.Token));
            }
        }

        public async Task StopAsync()
        {
            foreach (var kvp in _tokens)
            {
                kvp.Value.Cancel();
            }

            try
            {
                await Task.WhenAll(_tasks.Values);
            }
            catch (OperationCanceledException)
            {
                // ignored
            }

            foreach (var kvp in _clients)
            {
                kvp.Value.Dispose();
            }

            foreach (var kvp in _tokens)
            {
                kvp.Value.Dispose();
            }

            _tasks.Clear();
            _tokens.Clear();
            _clients.Clear();

            lock (_statusLock)
            {
                _lastStatus.Clear();
            }
        }

        public string? GetCameraStatus(Guid cameraId)
        {
            lock (_statusLock)
            {
                return _lastStatus.TryGetValue(cameraId, out var status) ? status : null;
            }
        }

        private async Task RunCameraLoopAsync(Camera camera, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!_clients.TryGetValue(camera.Id, out var client))
                        throw new InvalidOperationException("Client not initialized for camera.");

                    for (int i = 0; i < camera.Bindings.Count; i++)
                    {
                        var binding = camera.Bindings[i];
                        if (!binding.Enabled)
                            continue;

                        int positionX = binding.AutoPosition ? camera.BasePosX : binding.PositionX;
                        int positionY = binding.AutoPosition
                            ? camera.BasePosY + i * camera.LineHeight
                            : binding.PositionY;

                        var scale = binding.Scale ?? _scaleManager.Scales.FirstOrDefault(s => s.Id == binding.Id);
                        string text = BuildOverlayText(scale);

                        await client.SendOverlayTextAsync(
                            camera.Ip,
                            binding.OverlayId,
                            positionX,
                            positionY,
                            text,
                            token);
                    }

                    lock (_statusLock)
                    {
                        _lastStatus[camera.Id] = "Last update OK";
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (HttpRequestException ex)
                {
                    LogError(camera, ex);
                }
                catch (InvalidOperationException ex)
                {
                    LogError(camera, ex);
                }
                catch (Exception ex)
                {
                    LogError(camera, ex);
                }

                try
                {
                    await Task.Delay(_updateInterval, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void LogError(Camera camera, Exception ex)
        {
            LogMessage?.Invoke(
                $"[{DateTime.Now:HH:mm:ss}] Ошибка OSD для камеры \"{camera.Name}\" ({camera.Ip}): {ex.GetType().Name}: {ex.Message}");

            lock (_statusLock)
            {
                _lastStatus[camera.Id] = $"Error: {ex.Message}";
            }
        }

        private string BuildOverlayText(Scale? scale)
        {
            if (scale == null || !scale.State.IsOnline(_scaleManager.OfflineThreshold))
            {
                return _settings.OverlayNoConnectionText;
            }

            var net = WeightFormatter.FormatWeight(scale.State.NetGrams, _weightUnit, _decimals);
            var tare = WeightFormatter.FormatWeight(scale.State.TareGrams, _weightUnit, _decimals);
            var status = scale.State.Stable ? "S" : _settings.OverlayUnstableText;
            var unit = _weightUnit == WeightUnit.Kg ? "kg" : "g";

            return _settings.OverlayTextTemplate
                .Replace("{net}", net)
                .Replace("{tare}", tare)
                .Replace("{unit}", unit)
                .Replace("{status}", status);
        }
    }
}
