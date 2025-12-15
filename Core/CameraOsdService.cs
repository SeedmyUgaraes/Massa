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
        private readonly Dictionary<Guid, bool?> _lastOnlineStates;
        private readonly Dictionary<Guid, DateTime> _lastStatusChangeUtc;
        private readonly Dictionary<Guid, DateTime> _lastSuccessUtc;
        private readonly Dictionary<Guid, HikvisionOsdClient> _clients;
        private readonly Dictionary<Guid, Task> _tasks;
        private readonly Dictionary<Guid, CancellationTokenSource> _tokens;
        private readonly object _statusLock = new();
        private readonly TimeSpan _onlineTimeout = TimeSpan.FromSeconds(5);

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
            _lastOnlineStates = new Dictionary<Guid, bool?>();
            _lastStatusChangeUtc = new Dictionary<Guid, DateTime>();
            _lastSuccessUtc = new Dictionary<Guid, DateTime>();
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
                _lastOnlineStates.Clear();
                _lastStatusChangeUtc.Clear();
                _lastSuccessUtc.Clear();
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
                CheckCameraTimeout(camera);

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

                    UpdateCameraStatus(camera, true);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (HttpRequestException ex)
                {
                    HandleCameraError(camera, ex);
                }
                catch (InvalidOperationException ex)
                {
                    HandleCameraError(camera, ex);
                }
                catch (Exception ex)
                {
                    HandleCameraError(camera, ex);
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

        private void HandleCameraError(Camera camera, Exception ex)
        {
            var reason = $"{ex.GetType().Name}: {ex.Message}";
            UpdateCameraStatus(camera, false, reason);
        }

        private void CheckCameraTimeout(Camera camera)
        {
            bool shouldMarkOffline = false;

            lock (_statusLock)
            {
                if (_lastOnlineStates.TryGetValue(camera.Id, out var online) && online == true &&
                    _lastSuccessUtc.TryGetValue(camera.Id, out var lastSuccess))
                {
                    if (DateTime.UtcNow - lastSuccess > _onlineTimeout)
                        shouldMarkOffline = true;
                }
            }

            if (shouldMarkOffline)
            {
                UpdateCameraStatus(camera, false, "OSD update timeout");
            }
        }

        private void UpdateCameraStatus(Camera camera, bool online, string? reason = null)
        {
            bool? previous;

            lock (_statusLock)
            {
                previous = _lastOnlineStates.TryGetValue(camera.Id, out var prev) ? prev : null;
                _lastOnlineStates[camera.Id] = online;

                if (!previous.HasValue || previous.Value != online)
                {
                    _lastStatusChangeUtc[camera.Id] = DateTime.UtcNow;
                }

                if (online)
                {
                    _lastSuccessUtc[camera.Id] = DateTime.UtcNow;
                    _lastStatus[camera.Id] = "Online";
                }
                else
                {
                    var reasonText = string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason;
                    _lastStatus[camera.Id] = $"Offline: {reasonText}";
                }
            }

            if (!previous.HasValue || previous.Value != online)
            {
                var messageSuffix = online
                    ? "ONLINE"
                    : $"OFFLINE{(string.IsNullOrWhiteSpace(reason) ? string.Empty : $" (reason: {reason})")}";

                LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] Camera {camera.Name} ({camera.Ip}:{camera.Port}) {messageSuffix}");
            }
        }

        private string BuildOverlayText(Scale? scale)
        {
            if (scale == null || !scale.State.IsOnline(_scaleManager.OfflineThreshold))
            {
                return _settings.OverlayNoConnectionText;
            }

            var netKg = scale.State.NetGrams / 1000.0;
            var tareKg = scale.State.TareGrams / 1000.0;
            var status = scale.State.Stable ? "[S]" : $"[{_settings.OverlayUnstableText}]";

            return $"N {netKg:0.00}kg T {tareKg:0.00}kg {status}";
        }
    }
}
