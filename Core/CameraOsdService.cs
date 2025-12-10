using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MassaKWin.Core
{
    /// <summary>
    /// Periodically updates OSD overlays on configured cameras using scale data.
    /// </summary>
    public class CameraOsdService : IDisposable
    {
        private readonly IReadOnlyCollection<Camera> _cameras;
        private readonly ScaleManager _scaleManager;
        private readonly TimeSpan _pollInterval;
        private readonly ConcurrentDictionary<Guid, string> _statusByCamera = new();
        private CancellationTokenSource? _cts;
        private Task? _worker;

        public event Action<string>? LogMessage;

        public CameraOsdService(IReadOnlyCollection<Camera> cameras, ScaleManager scaleManager, TimeSpan pollInterval)
        {
            _cameras = cameras;
            _scaleManager = scaleManager;
            _pollInterval = pollInterval;
        }

        public void Start()
        {
            if (_worker != null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _worker = Task.Run(() => RunAsync(_cts.Token));
        }

        public async Task StopAsync()
        {
            if (_cts == null || _worker == null)
            {
                return;
            }

            _cts.Cancel();
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                _worker = null;
            }
        }

        public string? GetCameraStatus(Guid cameraId)
        {
            return _statusByCamera.TryGetValue(cameraId, out var status) ? status : null;
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                foreach (var camera in _cameras)
                {
                    await UpdateCameraAsync(camera, cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Service is stopping
                }
            }
        }

        private async Task UpdateCameraAsync(Camera camera, CancellationToken cancellationToken)
        {
            if (camera.Bindings == null || camera.Bindings.Count == 0)
            {
                _statusByCamera[camera.Id] = "Нет привязок";
                return;
            }

            try
            {
                using var client = new HikvisionOsdClient(camera.Username, camera.Password);

                foreach (var binding in camera.Bindings.Where(b => b.Enabled))
                {
                    if (binding.Scale == null)
                    {
                        continue;
                    }

                    var weightKg = binding.Scale.State.NetGrams / 1000.0;
                    var text = $"{binding.Scale.Name}: {weightKg:F3} кг";

                    var posX = binding.AutoPosition ? camera.BasePosX : binding.PositionX;
                    var posY = binding.AutoPosition
                        ? camera.BasePosY + (Math.Max(0, binding.OverlayId - 1) * camera.LineHeight)
                        : binding.PositionY;

                    await client.SendOverlayTextAsync(
                        camera.Ip,
                        binding.OverlayId,
                        posX,
                        posY,
                        text,
                        cancellationToken).ConfigureAwait(false);
                }

                _statusByCamera[camera.Id] = "OSD обновляется";
            }
            catch (Exception ex)
            {
                var message = $"Ошибка обновления OSD для {camera.Name}: {ex.Message}";
                _statusByCamera[camera.Id] = message;
                LogMessage?.Invoke(message);
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
