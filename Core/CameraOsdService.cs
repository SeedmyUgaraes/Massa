using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MassaKWin.Core
{
    public class CameraOsdService
    {
        private const int MaxOverlayId = 4;
        private const int OverlayMarginLeft = 16;
        private const int OverlayMarginBottom = 64;
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
        private readonly ConcurrentDictionary<(Guid CameraId, int OverlayId), OverlayCacheEntry> _overlayCache;
        private readonly ConcurrentDictionary<Guid, bool> _dirtyScales;
        private readonly ConcurrentDictionary<Guid, bool> _dirtyCameras;
        private readonly ConcurrentDictionary<Guid, DateTime> _lastStatusChecksUtc;
        private readonly ConcurrentDictionary<(Guid CameraId, int OverlayId, string Axis), bool> _positionClampWarnings;
        private readonly ConcurrentDictionary<Guid, (int Width, int Height)> _resolutionCache;

        private static readonly TimeSpan MinOverlayUpdateInterval = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromMilliseconds(5000);

        public event Action<Guid, bool, string?>? CameraStatusChanged;

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
            _overlayCache = new ConcurrentDictionary<(Guid, int), OverlayCacheEntry>();
            _dirtyScales = new ConcurrentDictionary<Guid, bool>();
            _dirtyCameras = new ConcurrentDictionary<Guid, bool>();
            _lastStatusChecksUtc = new ConcurrentDictionary<Guid, DateTime>();
            _positionClampWarnings = new ConcurrentDictionary<(Guid, int, string), bool>();
            _resolutionCache = new ConcurrentDictionary<Guid, (int Width, int Height)>();
        }

        public void MarkScaleDirty(Guid scaleId)
        {
            _dirtyScales[scaleId] = true;
        }

        public void MarkCameraDirty(Guid cameraId)
        {
            _dirtyCameras[cameraId] = true;
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
            _overlayCache.Clear();
            _dirtyScales.Clear();
            _dirtyCameras.Clear();
            _lastStatusChecksUtc.Clear();
            _positionClampWarnings.Clear();
            _resolutionCache.Clear();

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
            var lastStatusCheck = DateTime.MinValue;

            while (!token.IsCancellationRequested)
            {
                CheckCameraTimeout(camera);

                if (DateTime.UtcNow - lastStatusCheck >= TimeSpan.FromSeconds(1))
                {
                    MarkDirtyScalesForCamera(camera);
                    lastStatusCheck = DateTime.UtcNow;
                }

                try
                {
                    if (!_clients.TryGetValue(camera.Id, out var client))
                        throw new InvalidOperationException("Client not initialized for camera.");

                    var dirtyCamera = _dirtyCameras.ContainsKey(camera.Id);
                    var keepCameraDirty = false;

                    var invalidBindings = camera.Bindings
                        .Where(binding => binding.Enabled && !IsOverlayIdValid(binding.OverlayId))
                        .ToList();

                    foreach (var binding in invalidBindings)
                    {
                        LogWarn($"OSD binding skipped: invalid OverlayId {binding.OverlayId} for camera \"{camera.Name}\" ({camera.Ip}). Binding scale: {binding.Scale?.Name ?? "unknown"}.");
                    }

                    var orderedBindings = camera.Bindings
                        .Where(binding => binding.Enabled && IsOverlayIdValid(binding.OverlayId))
                        .OrderBy(binding => binding.OverlayId)
                        .ToList();

                    for (int i = 0; i < orderedBindings.Count; i++)
                    {
                        var binding = orderedBindings[i];

                        var scale = binding.Scale ?? _scaleManager.Scales.FirstOrDefault(s => s.Id == binding.Id);
                        var cacheKey = (camera.Id, binding.OverlayId);
                        var lineIndex = i;

                        bool scaleDirty = scale != null && _dirtyScales.ContainsKey(scale.Id);
                        bool shouldProcess = dirtyCamera || scaleDirty;

                        if (!shouldProcess && _overlayCache.TryGetValue(cacheKey, out var cacheEntry))
                        {
                            if (DateTime.UtcNow - cacheEntry.LastSentUtc >= KeepAliveInterval)
                            {
                                shouldProcess = true;
                            }
                        }

                        if (!shouldProcess)
                            continue;

                        var newText = BuildOverlayText(scale);
                        var now = DateTime.UtcNow;

                        if (!ShouldSendOverlay(cacheKey, newText, now, out var retryLater))
                        {
                            if (retryLater)
                                keepCameraDirty = keepCameraDirty || dirtyCamera;

                            if (!retryLater && scale != null)
                                _dirtyScales.TryRemove(scale.Id, out _);

                            continue;
                        }

                        var (videoWidth, videoHeight) = await GetCameraResolutionAsync(camera, client, token);
                        var lineHeight = Math.Max(camera.LineHeight, 1);
                        int positionX = OverlayMarginLeft;
                        int positionY = (videoHeight - OverlayMarginBottom) - (lineIndex * lineHeight);

                        positionX = ClampPosition(camera, binding, positionX, "X", videoWidth);
                        positionY = ClampPosition(camera, binding, positionY, "Y", videoHeight - OverlayMarginBottom);

                        try
                        {
                            await client.SendOverlayTextAsync(
                                camera.Ip,
                                camera.Port,
                                binding.OverlayId,
                                positionX,
                                positionY,
                                newText,
                                token);
                        }
                        catch (InvalidOperationException ex) when (IsOsdBadRequest(ex))
                        {
                            LogOsdUpdateFailed(camera, binding, positionX, positionY, newText, ex);
                            continue;
                        }

                        _overlayCache[cacheKey] = new OverlayCacheEntry(newText, now);
                        if (scale != null)
                            _dirtyScales.TryRemove(scale.Id, out _);
                    }

                    if (!keepCameraDirty)
                    {
                        _dirtyCameras.TryRemove(camera.Id, out _);
                    }

                    UpdateCameraStatus(camera, true);
                }
                catch (OperationCanceledException)
                {
                    break;
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

        private void MarkDirtyScalesForCamera(Camera camera)
        {
            foreach (var binding in camera.Bindings)
            {
                if (!binding.Enabled)
                    continue;

                var scale = binding.Scale ?? _scaleManager.Scales.FirstOrDefault(s => s.Id == binding.Id);
                if (scale == null)
                    continue;

                var now = DateTime.UtcNow;
                if (_lastStatusChecksUtc.TryGetValue(scale.Id, out var lastCheck) && now - lastCheck < TimeSpan.FromSeconds(1))
                    continue;

                _lastStatusChecksUtc[scale.Id] = now;

                var online = scale.State.IsOnline(_scaleManager.OfflineThreshold);
                var lastOnline = scale.State.LastOnline;

                if (!lastOnline.HasValue || lastOnline.Value != online)
                {
                    _dirtyScales[scale.Id] = true;
                }
            }
        }

        private bool ShouldSendOverlay((Guid CameraId, int OverlayId) cacheKey, string newText, DateTime now, out bool retrySoon)
        {
            retrySoon = false;

            if (_overlayCache.TryGetValue(cacheKey, out var cacheEntry))
            {
                if (newText == cacheEntry.LastText && now - cacheEntry.LastSentUtc < KeepAliveInterval)
                {
                    return false;
                }

                if (now - cacheEntry.LastSentUtc < MinOverlayUpdateInterval)
                {
                    retrySoon = true;
                    return false;
                }
            }

            return true;
        }

        private void HandleCameraError(Camera camera, Exception ex)
        {
            var reason = $"{ex.GetType().Name}: {ex.Message}";
            UpdateCameraStatus(camera, false, reason);
        }

        private int ClampPosition(Camera camera, CameraScaleBinding binding, int value, string axis, int maxSize)
        {
            var maxValue = Math.Max(maxSize - 1, 0);
            var clamped = Math.Clamp(value, 0, maxValue);
            if (clamped == value)
                return clamped;

            var key = (camera.Id, binding.OverlayId, axis);
            if (_positionClampWarnings.TryAdd(key, true))
            {
                LogWarn($"OSD position clamped: camera \"{camera.Name}\" ({camera.Ip}), OverlayId {binding.OverlayId}, axis {axis}, value {value} -> {clamped} (max {maxValue}).");
            }

            return clamped;
        }

        private static bool IsOverlayIdValid(int overlayId)
        {
            return overlayId >= 1 && overlayId <= MaxOverlayId;
        }

        private static bool IsOsdBadRequest(InvalidOperationException ex)
        {
            if (TryParseOsdError(ex, out var statusCode, out var body))
            {
                if (statusCode == 400)
                    return true;
                if (!string.IsNullOrWhiteSpace(body) &&
                    (body.Contains("badParameters", StringComparison.OrdinalIgnoreCase) ||
                     body.Contains("Invalid XML Content", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return ex.Message.Contains("badParameters", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("Invalid XML Content", StringComparison.OrdinalIgnoreCase);
        }

        private void LogOsdUpdateFailed(
            Camera camera,
            CameraScaleBinding binding,
            int positionX,
            int positionY,
            string displayText,
            InvalidOperationException ex)
        {
            TryParseOsdError(ex, out var statusCode, out var body);
            var displayPreview = BuildDisplayPreview(displayText);
            var length = displayText?.Length ?? 0;
            var statusText = statusCode.HasValue ? statusCode.Value.ToString() : "unknown";
            var bodyText = string.IsNullOrWhiteSpace(body) ? "empty" : body;

            LogWarn(
                $"OSD update failed for camera \"{camera.Name}\" ({camera.Ip}), OverlayId {binding.OverlayId}, " +
                $"posX {positionX}, posY {positionY}, textLen {length}, textPreview \"{displayPreview}\", " +
                $"status {statusText}, body \"{bodyText}\".");
        }

        private static string BuildDisplayPreview(string? displayText)
        {
            if (string.IsNullOrEmpty(displayText))
                return string.Empty;

            var compact = displayText.Replace("\r", " ").Replace("\n", " ");
            return compact.Length <= 80 ? compact : compact.Substring(0, 80);
        }

        private static bool TryParseOsdError(InvalidOperationException ex, out int? statusCode, out string? body)
        {
            statusCode = null;
            body = null;

            var message = ex.Message ?? string.Empty;
            var prefix = "Hikvision OSD error:";
            var prefixIndex = message.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (prefixIndex < 0)
                return false;

            var bodyIndex = message.IndexOf("Body:", StringComparison.OrdinalIgnoreCase);
            var statusText = bodyIndex > -1
                ? message.Substring(prefixIndex + prefix.Length, bodyIndex - (prefixIndex + prefix.Length))
                : message.Substring(prefixIndex + prefix.Length);
            statusText = statusText.Trim();
            if (!string.IsNullOrWhiteSpace(statusText))
            {
                var parts = statusText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && int.TryParse(parts[0], out var parsedCode))
                    statusCode = parsedCode;
            }

            if (bodyIndex > -1)
            {
                body = message.Substring(bodyIndex + "Body:".Length).Trim();
            }

            return true;
        }

        private void LogWarn(string message)
        {
            LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] WARN {message}");
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
                _dirtyCameras[camera.Id] = true;
                CameraStatusChanged?.Invoke(camera.Id, online, reason);
            }
        }

        private string BuildOverlayText(Scale? scale)
        {
            if (scale == null || !scale.State.IsOnline(_scaleManager.OfflineThreshold))
            {
                return string.IsNullOrWhiteSpace(_settings.OverlayNoConnectionText)
                    ? "Scale Offline"
                    : _settings.OverlayNoConnectionText;
            }

            var unitText = _weightUnit == WeightUnit.Kg ? "kg" : "g";
            var status = scale.State.Stable ? "[OK]" : $"[{_settings.OverlayUnstableText}]";

            var template = string.IsNullOrWhiteSpace(_settings.OverlayTextTemplate)
                ? "N {net}{unit} T {tare}{unit} {status}"
                : _settings.OverlayTextTemplate;

            var netText = WeightFormatter.FormatWeight(scale.State.NetGrams, _weightUnit, _decimals);
            var tareText = WeightFormatter.FormatWeight(scale.State.TareGrams, _weightUnit, _decimals);

            return template
                .Replace("{net}", netText)
                .Replace("{tare}", tareText)
                .Replace("{unit}", unitText)
                .Replace("{status}", status);
        }

        private async Task<(int Width, int Height)> GetCameraResolutionAsync(
            Camera camera,
            HikvisionOsdClient client,
            CancellationToken token)
        {
            if (_resolutionCache.TryGetValue(camera.Id, out var cached))
                return cached;

            var resolution = await client.GetVideoResolutionAsync(camera.Ip, camera.Port, token);
            _resolutionCache[camera.Id] = resolution;
            return resolution;
        }

        private class OverlayCacheEntry
        {
            public OverlayCacheEntry(string lastText, DateTime lastSentUtc)
            {
                LastText = lastText;
                LastSentUtc = lastSentUtc;
            }

            public string LastText { get; }
            public DateTime LastSentUtc { get; }
        }
    }
}
