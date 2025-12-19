using System;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Concurrent;
using System.Net.Http.Headers;

namespace MassaKWin.Core
{
    /// <summary>
    /// Клиент для отправки OSD-текста на камеры Hikvision через ISAPI.
    /// В стартере реализован базовый PUT-запрос.
    /// </summary>
    public class HikvisionOsdClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private const int DefaultVideoWidth = 1920;
        private const int DefaultVideoHeight = 1080;
        private const int DefaultChannelId = 1;
        private static readonly TimeSpan NormalizedCacheDuration = TimeSpan.FromMinutes(10);
        private static readonly (int Width, int Height) DefaultNormalizedSize = (704, 576);
        private readonly ConcurrentDictionary<string, (int Width, int Height, DateTime CachedAtUtc)> _normalizedSizeCache;

        public HikvisionOsdClient(string username, string password)
        {
            var handler = new HttpClientHandler
            {
                PreAuthenticate = true,
                Credentials = new NetworkCredential(username, password)
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(3)
            };
            _normalizedSizeCache = new ConcurrentDictionary<string, (int Width, int Height, DateTime CachedAtUtc)>();
        }

        public async Task SendOverlayTextAsync(
            string cameraIp,
            int port,
            int overlayId,
            int posX,
            int posY,
            string text,
            CancellationToken cancellationToken = default)
        {
            var url = $"http://{cameraIp}:{port}/ISAPI/System/Video/inputs/channels/{DefaultChannelId}/overlays/text/{overlayId}";
            var xml = BuildTextOverlayXml(overlayId, posX, posY, text, true);

            using var content = new StringContent(xml, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/xml")
            {
                CharSet = "utf-8"
            };
            using var response = await _httpClient.PutAsync(url, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Hikvision OSD error: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
            }
        }

        public async Task ClearOverlayAsync(
            string cameraIp,
            int port,
            int overlayId,
            CancellationToken cancellationToken = default)
        {
            var url = $"http://{cameraIp}:{port}/ISAPI/System/Video/inputs/channels/{DefaultChannelId}/overlays/text/{overlayId}";
            var xml = BuildTextOverlayXml(overlayId, 0, 0, string.Empty, false);

            using var content = new StringContent(xml, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/xml")
            {
                CharSet = "utf-8"
            };
            using var response = await _httpClient.PutAsync(url, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Hikvision OSD error: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
            }
        }

        public async Task<(int Width, int Height)> GetVideoResolutionAsync(
            string cameraIp,
            int port,
            CancellationToken cancellationToken = default)
        {
            var endpoints = new[] { "/ISAPI/Streaming/channels/101", "/ISAPI/Streaming/channels/1" };

            foreach (var endpoint in endpoints)
            {
                try
                {
                    var url = $"http://{cameraIp}:{port}{endpoint}";
                    using var response = await _httpClient.GetAsync(url, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                        continue;

                    var xml = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (TryParseResolution(xml, out var width, out var height))
                    {
                        return (width, height);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // ignore and try next endpoint
                }
            }

            return (DefaultVideoWidth, DefaultVideoHeight);
        }

        private static bool TryParseResolution(string xml, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (string.IsNullOrWhiteSpace(xml))
                return false;

            try
            {
                var doc = XDocument.Parse(xml);
                var widthElement = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "videoResolutionWidth");
                var heightElement = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "videoResolutionHeight");

                if (widthElement == null || heightElement == null)
                    return false;

                if (!int.TryParse(widthElement.Value, out width))
                    return false;

                if (!int.TryParse(heightElement.Value, out height))
                    return false;

                if (width <= 0 || height <= 0)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }

        public async Task<(int Width, int Height)> GetNormalizedScreenSizeAsync(
            string cameraIp,
            int port,
            CancellationToken cancellationToken = default)
        {
            var cacheKey = $"{cameraIp}:{port}";
            if (_normalizedSizeCache.TryGetValue(cacheKey, out var cached) &&
                DateTime.UtcNow - cached.CachedAtUtc < NormalizedCacheDuration)
            {
                return (cached.Width, cached.Height);
            }

            var url = $"http://{cameraIp}:{port}/ISAPI/System/Video/inputs/channels/{DefaultChannelId}/overlays";
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _normalizedSizeCache[cacheKey] = (DefaultNormalizedSize.Width, DefaultNormalizedSize.Height, DateTime.UtcNow);
                return DefaultNormalizedSize;
            }

            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            if (TryParseNormalizedSize(xml, out var width, out var height))
            {
                _normalizedSizeCache[cacheKey] = (width, height, DateTime.UtcNow);
                return (width, height);
            }

            _normalizedSizeCache[cacheKey] = (DefaultNormalizedSize.Width, DefaultNormalizedSize.Height, DateTime.UtcNow);
            return DefaultNormalizedSize;
        }

        private static string BuildTextOverlayXml(int overlayId, int posX, int posY, string? text, bool enabled)
        {
            var escapedText = System.Security.SecurityElement.Escape(text ?? string.Empty) ?? string.Empty;
            var enabledText = enabled ? "true" : "false";

            return $@"<TextOverlay>
  <id>{overlayId}</id>
  <enabled>{enabledText}</enabled>
  <positionX>{posX}</positionX>
  <positionY>{posY}</positionY>
  <displayText>{escapedText}</displayText>
  <directAngle></directAngle>
</TextOverlay>";
        }

        private static bool TryParseNormalizedSize(string xml, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (string.IsNullOrWhiteSpace(xml))
                return false;

            try
            {
                var doc = XDocument.Parse(xml);
                var widthElement = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "normalizedScreenWidth");
                var heightElement = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "normalizedScreenHeight");

                if (widthElement == null || heightElement == null)
                    return false;

                if (!int.TryParse(widthElement.Value, out width))
                    return false;

                if (!int.TryParse(heightElement.Value, out height))
                    return false;

                if (width <= 0 || height <= 0)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
