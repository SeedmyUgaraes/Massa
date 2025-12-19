using System;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Xml.Linq;

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
            var url = $"http://{cameraIp}:{port}/ISAPI/System/Video/inputs/channels/1/overlays/text/{overlayId}";
            var xml = $@"<TextOverlay>
  <id>{overlayId}</id>
  <enabled>true</enabled>
  <positionX>{posX}</positionX>
  <positionY>{posY}</positionY>
  <displayText>{System.Security.SecurityElement.Escape(text)}</displayText>
  <directAngle>0</directAngle>
</TextOverlay>";

            using var content = new StringContent(xml, Encoding.UTF8, "application/xml");
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
            var url = $"http://{cameraIp}:{port}/ISAPI/System/Video/inputs/channels/1/overlays/text/{overlayId}";
            var xml = $@"<TextOverlay>
  <id>{overlayId}</id>
  <enabled>false</enabled>
  <positionX>0</positionX>
  <positionY>0</positionY>
  <displayText></displayText>
  <directAngle>0</directAngle>
</TextOverlay>";

            using var content = new StringContent(xml, Encoding.UTF8, "application/xml");
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
    }
}
