using System;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MassaKWin.Core
{
    /// <summary>
    /// Клиент для отправки OSD-текста на камеры Hikvision через ISAPI.
    /// В стартере реализован базовый PUT-запрос.
    /// </summary>
    public class HikvisionOsdClient : IDisposable
    {
        private readonly HttpClient _httpClient;

        public HikvisionOsdClient(string username, string password)
        {
            var handler = new HttpClientHandler
            {
                PreAuthenticate = true,
                Credentials = new NetworkCredential(username, password)
            };
            _httpClient = new HttpClient(handler);
        }

        public async Task SendOverlayTextAsync(
            string cameraIp,
            int overlayId,
            int posX,
            int posY,
            string text,
            CancellationToken cancellationToken = default)
        {
            var url = $"http://{cameraIp}/ISAPI/System/Video/inputs/channels/1/overlays/text/{overlayId}";
            var xml = $@"
<TextOverlay>
  <id>{overlayId}</id>
  <enabled>true</enabled>
  <positionX>{posX}</positionX>
  <positionY>{posY}</positionY>
  <displayText>{System.Security.SecurityElement.Escape(text)}</displayText>
  <directAngle/>
</TextOverlay>";

            using var content = new StringContent(xml, Encoding.UTF8, "application/xml");
            using var response = await _httpClient.PutAsync(url, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Hikvision OSD error: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
