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
        private readonly Action<string>? _logMessage;
        private const int DefaultVideoWidth = 1920;
        private const int DefaultVideoHeight = 1080;
        private const int DefaultChannelId = 1;
        private const int OverlayListSize = 4;
        private const string HikvisionNamespace = "http://www.hikvision.com/ver20/XMLSchema";

        public HikvisionOsdClient(string username, string password, Action<string>? logMessage = null)
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
            _logMessage = logMessage;
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

            using var content = new StringContent(xml, Encoding.UTF8, "text/xml");
            using var response = await _httpClient.PutAsync(url, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (IsInvalidXmlResponse(response.StatusCode, body))
                {
                    await UpdateOverlayViaListAsync(cameraIp, port, overlayId, posX, posY, text, cancellationToken);
                    LogInfo($"OSD update via overlays (fallback), overlayId {overlayId}.");
                    return;
                }

                throw new InvalidOperationException($"Hikvision OSD error: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
            }

            LogInfo($"OSD update via text/{overlayId}.");
        }

        public async Task ClearOverlayAsync(
            string cameraIp,
            int port,
            int overlayId,
            CancellationToken cancellationToken = default)
        {
            var url = $"http://{cameraIp}:{port}/ISAPI/System/Video/inputs/channels/{DefaultChannelId}/overlays/text/{overlayId}";
            var xml = BuildTextOverlayXml(overlayId, 0, 0, string.Empty, false);

            using var content = new StringContent(xml, Encoding.UTF8, "text/xml");
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

        private static string BuildTextOverlayXml(int overlayId, int posX, int posY, string? text, bool enabled)
        {
            var escapedText = System.Security.SecurityElement.Escape(text ?? string.Empty) ?? string.Empty;
            var enabledText = enabled ? "true" : "false";

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<TextOverlay version=""2.0"" xmlns=""{HikvisionNamespace}"">
  <id>{overlayId}</id>
  <enabled>{enabledText}</enabled>
  <positionX>{posX}</positionX>
  <positionY>{posY}</positionY>
  <displayText>{escapedText}</displayText>
  <directAngle></directAngle>
</TextOverlay>";
        }

        private static bool IsInvalidXmlResponse(HttpStatusCode statusCode, string body)
        {
            if (statusCode != HttpStatusCode.BadRequest)
                return false;

            if (string.IsNullOrWhiteSpace(body))
                return false;

            return body.Contains("Invalid XML Content", StringComparison.OrdinalIgnoreCase) ||
                   body.Contains("badParameters", StringComparison.OrdinalIgnoreCase);
        }

        private async Task UpdateOverlayViaListAsync(
            string cameraIp,
            int port,
            int overlayId,
            int posX,
            int posY,
            string text,
            CancellationToken cancellationToken)
        {
            var url = $"http://{cameraIp}:{port}/ISAPI/System/Video/inputs/channels/{DefaultChannelId}/overlays";
            using var getResponse = await _httpClient.GetAsync(url, cancellationToken);
            if (!getResponse.IsSuccessStatusCode)
            {
                var body = await getResponse.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Hikvision OSD error: {(int)getResponse.StatusCode} {getResponse.ReasonPhrase}. Body: {body}");
            }

            var xml = await getResponse.Content.ReadAsStringAsync(cancellationToken);
            var doc = XDocument.Parse(xml);

            if (doc.Root == null)
                throw new InvalidOperationException("Hikvision OSD error: invalid overlays XML.");

            var ns = XNamespace.Get(HikvisionNamespace);
            ApplyNamespace(doc.Root, ns);
            doc.Root.SetAttributeValue("version", "2.0");

            var textOverlayList = doc.Root.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "TextOverlayList");
            if (textOverlayList == null)
            {
                textOverlayList = new XElement(ns + "TextOverlayList");
                doc.Root.Add(textOverlayList);
            }

            textOverlayList.SetAttributeValue("size", OverlayListSize);

            var overlay = textOverlayList.Elements()
                .FirstOrDefault(element =>
                {
                    if (element.Name.LocalName != "TextOverlay")
                        return false;

                    var idElement = element.Elements().FirstOrDefault(child => child.Name.LocalName == "id");
                    return idElement != null && int.TryParse(idElement.Value, out var idValue) && idValue == overlayId;
                });

            if (overlay == null)
            {
                overlay = new XElement(ns + "TextOverlay");
                textOverlayList.Add(overlay);
            }

            SetElementValue(overlay, ns, "id", overlayId.ToString());
            SetElementValue(overlay, ns, "enabled", "true");
            SetElementValue(overlay, ns, "positionX", posX.ToString());
            SetElementValue(overlay, ns, "positionY", posY.ToString());
            SetElementValue(overlay, ns, "displayText", text ?? string.Empty);
            SetElementValue(overlay, ns, "directAngle", string.Empty);

            doc.Declaration = new XDeclaration("1.0", "UTF-8", null);
            using var content = new StringContent(doc.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml");
            using var putResponse = await _httpClient.PutAsync(url, content, cancellationToken);
            if (!putResponse.IsSuccessStatusCode)
            {
                var body = await putResponse.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Hikvision OSD error: {(int)putResponse.StatusCode} {putResponse.ReasonPhrase}. Body: {body}");
            }
        }

        private static void ApplyNamespace(XElement element, XNamespace ns)
        {
            element.Name = ns + element.Name.LocalName;
            var namespaceAttributes = element.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).ToList();
            foreach (var attribute in namespaceAttributes)
            {
                attribute.Remove();
            }

            foreach (var child in element.Elements())
            {
                ApplyNamespace(child, ns);
            }
        }

        private static void SetElementValue(XElement parent, XNamespace ns, string elementName, string value)
        {
            var element = parent.Elements().FirstOrDefault(child => child.Name.LocalName == elementName);
            if (element == null)
            {
                element = new XElement(ns + elementName);
                parent.Add(element);
            }

            element.Value = value;
        }

        private void LogInfo(string message)
        {
            if (_logMessage == null)
                return;

            _logMessage($"[{DateTime.Now:HH:mm:ss}] {message}");
        }
    }
}
