using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MassaKWin.Core
{
    public class AppConfig
    {
        public List<ScaleConfigDto> Scales { get; set; } = new();
        public List<CameraConfigDto> Cameras { get; set; } = new();
        public List<CameraBindingConfigDto> Bindings { get; set; } = new();
        public GlobalSettings Settings { get; set; } = new();
    }

    public class ScaleConfigDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public int Port { get; set; }
    }

    public class CameraConfigDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public int BasePosX { get; set; }
        public int BasePosY { get; set; }
        public int LineHeight { get; set; }
    }

    public class CameraBindingConfigDto
    {
        public Guid CameraId { get; set; }
        public Guid ScaleId { get; set; }
        public int OverlayId { get; set; }
        public bool Enabled { get; set; }
        public bool AutoPosition { get; set; }
        public int PositionX { get; set; }
        public int PositionY { get; set; }
    }

    public class ConfigStorage
    {
        private readonly string _configPath;
        private readonly JsonSerializerOptions _jsonOptions;

        public ConfigStorage(string? configFileName = null)
        {
            var fileName = string.IsNullOrWhiteSpace(configFileName) ? "config.json" : configFileName;
            _configPath = Path.Combine(AppContext.BaseDirectory, fileName);
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
        }

        public AppConfig Load()
        {
            if (!File.Exists(_configPath))
            {
                return new AppConfig();
            }

            try
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);
                return config ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        public void Save(AppConfig config)
        {
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            File.WriteAllText(_configPath, json);
        }

        public AppConfig CreateFromManagers(ScaleManager scaleManager, CameraManager cameraManager, GlobalSettings settings)
        {
            var appConfig = new AppConfig
            {
                Settings = settings
            };

            foreach (var scale in scaleManager.Scales)
            {
                appConfig.Scales.Add(new ScaleConfigDto
                {
                    Id = scale.Id,
                    Name = scale.Name,
                    Ip = scale.Ip,
                    Port = scale.Port
                });
            }

            foreach (var camera in cameraManager.Cameras)
            {
                appConfig.Cameras.Add(new CameraConfigDto
                {
                    Id = camera.Id,
                    Name = camera.Name,
                    Ip = camera.Ip,
                    Port = camera.Port,
                    Username = camera.Username,
                    Password = camera.Password,
                    BasePosX = camera.BasePosX,
                    BasePosY = camera.BasePosY,
                    LineHeight = camera.LineHeight
                });

                foreach (var binding in camera.Bindings)
                {
                    appConfig.Bindings.Add(new CameraBindingConfigDto
                    {
                        CameraId = camera.Id,
                        ScaleId = binding.Scale?.Id ?? Guid.Empty,
                        OverlayId = binding.OverlayId,
                        Enabled = binding.Enabled,
                        AutoPosition = binding.AutoPosition,
                        PositionX = binding.PositionX,
                        PositionY = binding.PositionY
                    });
                }
            }

            return appConfig;
        }

        public void ApplyToManagers(AppConfig config, ScaleManager scaleManager, CameraManager cameraManager)
        {
            scaleManager.Scales.Clear();
            cameraManager.Cameras.Clear();

            foreach (var scaleDto in config.Scales)
            {
                var scale = new Scale
                {
                    Id = scaleDto.Id,
                    Name = scaleDto.Name,
                    Ip = scaleDto.Ip,
                    Port = scaleDto.Port
                };

                scaleManager.AddScale(scale);
            }

            foreach (var cameraDto in config.Cameras)
            {
                var camera = new Camera
                {
                    Id = cameraDto.Id,
                    Name = cameraDto.Name,
                    Ip = cameraDto.Ip,
                    Port = cameraDto.Port,
                    Username = cameraDto.Username,
                    Password = cameraDto.Password,
                    BasePosX = cameraDto.BasePosX,
                    BasePosY = cameraDto.BasePosY,
                    LineHeight = cameraDto.LineHeight
                };

                cameraManager.AddCamera(camera);
            }

            foreach (var bindingDto in config.Bindings)
            {
                Camera? camera = null;
                foreach (var c in cameraManager.Cameras)
                {
                    if (c.Id == bindingDto.CameraId)
                    {
                        camera = c;
                        break;
                    }
                }

                Scale? scale = null;
                foreach (var s in scaleManager.Scales)
                {
                    if (s.Id == bindingDto.ScaleId)
                    {
                        scale = s;
                        break;
                    }
                }

                if (camera != null && scale != null)
                {
                    var binding = new CameraScaleBinding
                    {
                        Camera = camera,
                        Scale = scale,
                        OverlayId = bindingDto.OverlayId,
                        Enabled = bindingDto.Enabled,
                        AutoPosition = bindingDto.AutoPosition,
                        PositionX = bindingDto.PositionX,
                        PositionY = bindingDto.PositionY
                    };

                    camera.Bindings.Add(binding);
                }
            }
        }
    }
}
