using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace GreenLight.Radio.Runtime
{
    public sealed class RadioWebPlayerLocalServer : IDisposable
    {
        private const string HtmlFileName = "radio-player.html";
        private const string JsFileName = "radio-player.js";
        private const string CssFileName = "radio-player.css";

        private readonly string rootDirectory;
        private readonly int preferredPort;

        private TcpListener listener;
        private Thread listenerThread;
        private volatile bool isRunning;

        public string PlayerUrl { get; private set; }

        public RadioWebPlayerLocalServer(string rootDirectory, int preferredPort)
        {
            this.rootDirectory = rootDirectory;
            this.preferredPort = Mathf.Clamp(preferredPort, 1024, 65535);
        }

        public bool Start()
        {
            if (isRunning)
            {
                return true;
            }

            if (!Directory.Exists(rootDirectory))
            {
                Debug.LogWarning($"[RadioWebPlayerLocalServer] Player directory was not found: {rootDirectory}");
                return false;
            }

            try
            {
                listener = CreateStartedListener();
                isRunning = true;

                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                PlayerUrl = $"http://127.0.0.1:{port}/{HtmlFileName}";

                listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "RadioWebPlayerLocalServer"
                };
                listenerThread.Start();

                Debug.Log($"[RadioWebPlayerLocalServer] Serving radio player at {PlayerUrl}");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[RadioWebPlayerLocalServer] Failed to start local server: {exception.Message}");
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            isRunning = false;

            try
            {
                listener?.Stop();
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            listener = null;
            PlayerUrl = string.Empty;
        }

        public void Dispose()
        {
            Stop();
        }

        private TcpListener CreateStartedListener()
        {
            try
            {
                TcpListener preferredListener = new TcpListener(IPAddress.Loopback, preferredPort);
                preferredListener.Start();
                return preferredListener;
            }
            catch (SocketException)
            {
                TcpListener fallbackListener = new TcpListener(IPAddress.Loopback, 0);
                fallbackListener.Start();
                return fallbackListener;
            }
        }

        private void ListenLoop()
        {
            while (isRunning)
            {
                try
                {
                    using (TcpClient client = listener.AcceptTcpClient())
                    {
                        HandleClient(client);
                    }
                }
                catch (SocketException)
                {
                    if (isRunning)
                    {
                        Debug.LogWarning("[RadioWebPlayerLocalServer] Socket error while accepting a request.");
                    }
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[RadioWebPlayerLocalServer] Request failed: {exception.Message}");
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            using (NetworkStream stream = client.GetStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
            {
                string requestLine = reader.ReadLine();

                if (string.IsNullOrWhiteSpace(requestLine))
                {
                    WriteResponse(stream, 400, "Bad Request", "text/plain", Encoding.UTF8.GetBytes("Bad Request"));
                    return;
                }

                while (!string.IsNullOrEmpty(reader.ReadLine()))
                {
                }

                string fileName = ResolveFileName(requestLine);

                if (string.IsNullOrWhiteSpace(fileName))
                {
                    WriteResponse(stream, 404, "Not Found", "text/plain", Encoding.UTF8.GetBytes("Not Found"));
                    return;
                }

                string filePath = Path.Combine(rootDirectory, fileName);

                if (!File.Exists(filePath))
                {
                    WriteResponse(stream, 404, "Not Found", "text/plain", Encoding.UTF8.GetBytes("Not Found"));
                    return;
                }

                WriteResponse(stream, 200, "OK", GetContentType(fileName), File.ReadAllBytes(filePath));
            }
        }

        private static string ResolveFileName(string requestLine)
        {
            string[] parts = requestLine.Split(' ');

            if (parts.Length < 2 || parts[0] != "GET")
            {
                return string.Empty;
            }

            string path = Uri.UnescapeDataString(parts[1].Split('?')[0]).TrimStart('/');

            if (string.IsNullOrWhiteSpace(path))
            {
                return HtmlFileName;
            }

            switch (path)
            {
                case HtmlFileName:
                    return HtmlFileName;
                case JsFileName:
                    return JsFileName;
                case CssFileName:
                    return CssFileName;
                default:
                    return string.Empty;
            }
        }

        private static string GetContentType(string fileName)
        {
            switch (Path.GetExtension(fileName).ToLowerInvariant())
            {
                case ".html":
                    return "text/html; charset=utf-8";
                case ".js":
                    return "application/javascript; charset=utf-8";
                case ".css":
                    return "text/css; charset=utf-8";
                default:
                    return "application/octet-stream";
            }
        }

        private static void WriteResponse(NetworkStream stream, int statusCode, string statusText, string contentType, byte[] body)
        {
            string header =
                $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                $"Content-Type: {contentType}\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Cache-Control: no-store\r\n" +
                "Access-Control-Allow-Origin: *\r\n" +
                "Connection: close\r\n\r\n";

            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(body, 0, body.Length);
        }

        // Unity setup: RadioGreeWebViewPlayer starts this server automatically when the real WebView player initializes.
    }
}
