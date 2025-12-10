using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MassaKWeightOverlay
{
    /// <summary>
    /// Возможные варианты протокола CMD_ACK_MASSA:
    /// - Unknown: ещё не определили.
    /// - WithoutTare: «старый» формат без поля тарного веса.
    /// - WithTare: «новый» формат с тарным весом в ответе.
    /// </summary>
    enum ScaleProtocol
    {
        Unknown,
        WithoutTare,
        WithTare
    }

    /// <summary>
    /// Описывает одну конфигурацию весового узла:
    /// IP-адрес, порт TCP и номер OSD-слоя на камере Hikvision.
    /// </summary>
    class ScaleConfig
    {
        public string Ip { get; set; }
        public int Port { get; set; }
        public int OverlayId { get; set; }

        /// <summary>
        /// Уникальный ключ для словаря – "IP:Port".
        /// </summary>
        public string Key => $"{Ip}:{Port}";
    }

    /// <summary>
    /// Хранит результаты последнего опроса:
    /// - Net: масса нетто (граммы).
    /// - Tare: масса тары (граммы).
    /// - Stable: флаг «стабилизировано».
    /// - LastData: время последнего удачного чтения.
    /// - Protocol: определённый формат ответа.
    /// </summary>
    class ScaleState
    {
        public double Net = 0.0;
        public double Tare = 0.0;
        public bool Stable = false;
        public DateTime LastData = DateTime.MinValue;
        public ScaleProtocol Protocol = ScaleProtocol.Unknown;
    }

    class Program
    {
        //==========================================================================
        // Константы для подключения к Hikvision-камере через ISAPI OSD
        //==========================================================================
        private const string CAM_IP = "192.168.0.64";
        private const string CAM_USER = "admin";
        private const string CAM_PASS = "Haris929";

        //==========================================================================
        // Позиционирование текста: X одна для всех, Y зависит от OverlayId
        //==========================================================================
        private const int OSD_POS_X = 500;
        private const int OSD_BASE_POS_Y = 50;
        private const int OSD_LINE_HEIGHT = 30;

        //==========================================================================
        // Таймауты для TCP и интервалы перезапроса
        //==========================================================================
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan LoopDelay = TimeSpan.FromMilliseconds(200);

        //==========================================================================
        // Перечень трёх весовых узлов: два по бинарному протоколу, один по ASCII
        //==========================================================================
        private static readonly ScaleConfig[] scales = new[]
        {
            new ScaleConfig { Ip = "192.168.0.80", Port = 5000, OverlayId = 1 },
            new ScaleConfig { Ip = "192.168.0.81", Port = 5001, OverlayId = 2 },
            new ScaleConfig { Ip = "192.168.0.82", Port = 5001, OverlayId = 3 },
        };

        //==========================================================================
        // Словарь состояний: ключ – "IP:Port"
        //==========================================================================
        private static readonly Dictionary<string, ScaleState> states =
            new Dictionary<string, ScaleState>();

        /// <summary>
        /// Точка входа: запускает асинхронный основной метод.
        /// </summary>
        static void Main(string[] args)
        {
            MainAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Главный метод приложения:
        /// - инициализирует состояние
        /// - запускает фоновый опрос каждого узла
        /// - в цикле обновляет консоль и OSD-слои
        /// </summary>
        static async Task MainAsync()
        {
            // 1) Инициализация словаря с пустыми состояниями
            foreach (var cfg in scales)
                states[cfg.Key] = new ScaleState();

            // 2) Настройка HTTP-клиента для камеры (Basic Auth)
            var handler = new HttpClientHandler
            {
                PreAuthenticate = true,
                Credentials = new NetworkCredential(CAM_USER, CAM_PASS)
            };
            using (var httpClient = new HttpClient(handler))
            {
                // 3) Запуск параллельных тасков на опрос весов
                foreach (var cfg in scales)
                    _ = Task.Run(() => QueryWeightLoopTcp(cfg));

                Console.WriteLine("Press Ctrl+C to exit.");

                // 4) Основной цикл: для каждого узла – консоль + OSD
                while (true)
                {
                    Console.Clear();
                    foreach (var cfg in scales)
                    {
                        var st = states[cfg.Key];
                        bool online = (DateTime.Now - st.LastData).TotalSeconds < 10;

                        // Формируем строку OSD
                        string netStr = (st.Net / 1000.0).ToString("F3") + " kg";
                        string tareStr = (st.Tare / 1000.0).ToString("F3") + " kg";
                        string status = online ? "Online" : "Offline";

                        string osdText = online
                            ? $"N:{netStr} T:{tareStr} {(st.Stable ? "[OK]" : "[..]")}"
                            : "Scale Offline";

                        // Вывод в консоль
                        Console.WriteLine($"Scale {cfg.Ip} (OSD {cfg.OverlayId}) - {status}");
                        Console.WriteLine($"  {osdText}");
                        Console.WriteLine($"  Protocol: {st.Protocol}");
                        Console.WriteLine();

                        // Отправка текста на камеру
                        int posY = OSD_BASE_POS_Y + (cfg.OverlayId - 1) * OSD_LINE_HEIGHT;
                        await SendOverlayText(cfg.OverlayId, OSD_POS_X, posY, osdText, httpClient);
                    }

                    await Task.Delay(500);
                }
            }
        }

        /// <summary>
        /// Бесконечный цикл опроса по TCP:
        /// - отправка CMD_GET_MASSA
        /// - первичная детекция формата ответа
        /// - разбор и сохранение в states
        /// </summary>
        static async Task QueryWeightLoopTcp(ScaleConfig cfg)
        {
            TcpClient client = null;
            NetworkStream stream = null;

            while (true)
            {
                // 1) Подключение, если нужно
                if (client?.Connected != true)
                {
                    SafeClose(stream, client);
                    client = new TcpClient();
                    try
                    {
                        await ConnectWithTimeout(client, cfg.Ip, cfg.Port, ConnectTimeout);
                        stream = client.GetStream();
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Connected to {cfg.Ip}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Connect failed {cfg.Ip}: {ex.Message}");
                        await Task.Delay(ReconnectDelay);
                        continue;
                    }
                }

                try
                {
                    // 2) Отправка запроса массы
                    using (var cts = new CancellationTokenSource(OperationTimeout))
                    {
                        byte[] request = BuildGetMassaRequest(0x23);
                        await stream.WriteAsync(request, 0, request.Length, cts.Token);

                        // 3) Чтение префикса и длины
                        byte[] hdr = await ReadExactAsync(stream, 3, cts.Token);
                        byte[] lenBuf = await ReadExactAsync(stream, 2, cts.Token);
                        int payloadLen = BitConverter.ToInt16(lenBuf, 0);

                        // 4) Детекция протокола при первом ответе
                        var st = states[cfg.Key];
                        if (st.Protocol == ScaleProtocol.Unknown)
                        {
                            st.Protocol = payloadLen >= 13
                                ? ScaleProtocol.WithTare
                                : ScaleProtocol.WithoutTare;
                            Console.WriteLine(
                                $"[{DateTime.Now:HH:mm:ss}] {cfg.Ip} protocol: {st.Protocol}");
                        }

                        // 5) Чтение тела + CRC
                        byte[] payloadCrc = await ReadExactAsync(stream, payloadLen + 2, cts.Token);

                        // 6) Сбор всего буфера и валидация
                        byte[] buf = new byte[3 + 2 + payloadLen + 2];
                        Buffer.BlockCopy(hdr, 0, buf, 0, 3);
                        Buffer.BlockCopy(lenBuf, 0, buf, 3, 2);
                        Buffer.BlockCopy(payloadCrc, 0, buf, 5, payloadLen + 2);

                        if (buf[0] == 0xF8 && buf[1] == 0x55 && buf[2] == 0xCE && buf[5] == 0x24)
                        {
                            // 7) Парсинг полей mass, division, stable
                            int offset = 6;  // 3 header + 2 len + 1 cmd
                            int rawMass = BitConverter.ToInt32(buf, offset);
                            byte division = buf[offset + 4];
                            bool stable = buf[offset + 5] == 1;
                            double factor = DivisionToFactor(division);
                            double measured = rawMass * factor;
                            double tareValue = 0.0;

                            // 8) В случае WithTare читаем поле rawTare
                            if (st.Protocol == ScaleProtocol.WithTare)
                            {
                                int tareOffset = offset + 4 + 1 + 1 + 1 + 1;
                                int rawTare = BitConverter.ToInt32(buf, tareOffset);
                                tareValue = rawTare * factor;
                            }

                            // 9) Сохраняем результаты
                            st.Net = measured;
                            st.Tare = tareValue;
                            st.Stable = stable;
                            st.LastData = DateTime.Now;
                        }
                    }

                    await Task.Delay(LoopDelay);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Timeout {cfg.Ip}, reconnecting...");
                    SafeClose(stream, client);
                    client = null;
                    stream = null;
                    await Task.Delay(ReconnectDelay);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Error {cfg.Ip}: {ex.Message}");
                    SafeClose(stream, client);
                    client = null;
                    stream = null;
                    await Task.Delay(ReconnectDelay);
                }
            }
        }

        /// <summary>
        /// Собирает пакет CMD_GET_MASSA (0x23):
        /// [F8 55 CE] [Len=0001] [Cmd] [CRC16]
        /// </summary>
        static byte[] BuildGetMassaRequest(byte cmd)
        {
            byte[] header = { 0xF8, 0x55, 0xCE };
            byte[] len = { 0x01, 0x00 };
            var buf = new byte[header.Length + len.Length + 1 + 2];

            Buffer.BlockCopy(header, 0, buf, 0, header.Length);
            Buffer.BlockCopy(len, 0, buf, header.Length, len.Length);

            int idx = header.Length + len.Length;
            buf[idx] = cmd;

            ushort crc = ComputeCRC(new byte[] { cmd });
            buf[idx + 1] = (byte)(crc & 0xFF);
            buf[idx + 2] = (byte)(crc >> 8);

            return buf;
        }

        /// <summary>
        /// Множитель грамм по коду division:
        /// 0→0.1g, 1→1g, 2→10g, 3→100g, 4→1000g
        /// </summary>
        static double DivisionToFactor(byte division)
        {
            switch (division)
            {
                case 0: return 0.1;
                case 1: return 1.0;
                case 2: return 10.0;
                case 3: return 100.0;
                case 4: return 1000.0;
                default: return 1.0;
            }
        }

        /// <summary>
        /// Ждёт ровно count байт или бросает IOException
        /// </summary>
        static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken token)
        {
            var buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = await stream.ReadAsync(buffer, offset, count - offset, token);
                if (read == 0) throw new IOException("Connection closed");
                offset += read;
            }
            return buffer;
        }

        /// <summary>
        /// ConnectAsync с таймаутом, иначе бросает TimeoutException
        /// </summary>
        static async Task ConnectWithTimeout(TcpClient client, string host, int port, TimeSpan timeout)
        {
            var connectTask = client.ConnectAsync(host, port);
            if (await Task.WhenAny(connectTask, Task.Delay(timeout)) != connectTask)
            {
                client.Close();
                throw new TimeoutException("Connect timeout");
            }
            await connectTask;
        }

        /// <summary>
        /// Закрывает NetworkStream и TcpClient, игнорируя ошибки
        /// </summary>
        static void SafeClose(NetworkStream stream, TcpClient client)
        {
            try { stream?.Close(); } catch { }
            try { client?.Close(); } catch { }
        }

        /// <summary>
        /// CRC-16-CCITT (polynomial 0x1021), seed 0xFFFF
        /// </summary>
        static ushort ComputeCRC(byte[] data)
        {
            ushort crc = 0xFFFF;
            foreach (byte b in data)
            {
                crc ^= (ushort)(b << 8);
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 0x8000) != 0)
                        crc = (ushort)((crc << 1) ^ 0x1021);
                    else
                        crc <<= 1;
                }
            }
            return crc;
        }

        /// <summary>
        /// Формирует XML и шлёт PUT-запрос для обновления OSD-текста на камере
        /// </summary>
        static async Task SendOverlayText(int overlayId, int posX, int posY, string text, HttpClient client)
        {
            var url = $"http://{CAM_IP}/ISAPI/System/Video/inputs/channels/1/overlays/text/{overlayId}";
            string xml =
                "<TextOverlay xmlns=\"http://www.hikvision.com/ver20/XMLSchema\" version=\"2.0\">\r\n" +
                $"  <id>{overlayId}</id>\r\n" +
                "  <enabled>true</enabled>\r\n" +
                $"  <positionX>{posX}</positionX>\r\n" +
                $"  <positionY>{posY}</positionY>\r\n" +
                $"  <displayText>{text}</displayText>\r\n" +
                "  <directAngle/>\r\n" +
                "</TextOverlay>";

            var content = new StringContent(xml, Encoding.UTF8, "application/xml");
            var resp = await client.PutAsync(url, content);

            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"OSD update {overlayId} failed: {resp.StatusCode}");
                try { Console.WriteLine(await resp.Content.ReadAsStringAsync()); } catch { }
            }
        }
    }
}
