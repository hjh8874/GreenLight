using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace GreenLight.Radio.Runtime
{
    public sealed class RadioGreeWebViewPlayer : MonoBehaviour
    {
        private const string WebViewObjectTypeName = "Gree.UnityWebView.WebViewObject";
        private const string PlayerRelativePath = "06_Radio/Web/IFramePlayer/radio-player.html";
        private const string PlayerDirectoryRelativePath = "06_Radio/Web/IFramePlayer";

        private enum WebViewAnchor
        {
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        [Serializable]
        private sealed class WebPlayerMessage
        {
            public string eventName = string.Empty;
            public WebPlayerPayload payload = new WebPlayerPayload();
        }

        [Serializable]
        private sealed class WebPlayerPayload
        {
            public float dx = 0f;
            public float dy = 0f;
        }

        [Header("Radio")]
        [SerializeField] private RadioService radioService;
        [SerializeField] private bool initializeOnStart = true;
        [SerializeField] private bool visibleOnStart = true;

        [Header("Screen Placement")]
        [SerializeField] private WebViewAnchor anchor = WebViewAnchor.TopRight;
        [SerializeField] private Vector2Int playerSize = new Vector2Int(360, 220);
        [SerializeField] private Vector2Int screenPadding = new Vector2Int(24, 24);

        [Header("Player")]
        [Range(0, 100)]
        [SerializeField] private int volume = 80;

        [Header("Local Server")]
        [SerializeField] private bool servePlayerFromLocalhost = true;
        [SerializeField] private int localServerPort = 18496;

        [Header("Drag")]
        [SerializeField] private bool allowDragFromWebPlayer = true;

        private Component webViewObject;
        private Type webViewType;
        private RadioWebPlayerLocalServer localServer;
        private bool isInitialized;
        private string pendingVideoId;
        private string pendingDisplayName;
        private int lastScreenWidth;
        private int lastScreenHeight;
        private int currentPlayerLeft;
        private int currentPlayerTop;
        private bool isUsingDraggedPlacement;

        private void Awake()
        {
            if (radioService == null)
            {
                radioService = GetComponent<RadioService>();
            }
        }

        private void Start()
        {
            if (initializeOnStart)
            {
                InitializeWebView();
            }
        }

        private void Update()
        {
            if (!isInitialized)
            {
                return;
            }

            if (lastScreenWidth == Screen.width && lastScreenHeight == Screen.height)
            {
                return;
            }

            ApplyWebViewPlacement();
        }

        private void OnEnable()
        {
            BindRadioService();
        }

        private void OnDisable()
        {
            UnbindRadioService();
        }

        private void OnDestroy()
        {
            StopLocalServer();
        }

        public bool InitializeWebView()
        {
            if (isInitialized)
            {
                return true;
            }

            webViewType = FindWebViewObjectType();

            if (webViewType == null)
            {
                Debug.LogWarning("[RadioGreeWebViewPlayer] gree unity-webview is not imported yet. Import WebViewObject before using the real IFrame player.");
                return false;
            }

            GameObject webViewObjectRoot = new GameObject("RadioGreeWebViewObject");
            webViewObjectRoot.transform.SetParent(transform, false);
            webViewObject = webViewObjectRoot.AddComponent(webViewType);

            InvokeWebView(
                "Init",
                new Action<string>(OnJavaScriptMessage),
                new Action<string>(OnWebViewError),
                new Action<string>(OnWebViewHttpError),
                new Action<string>(OnWebViewLoaded),
                new Action<string>(OnWebViewStarted),
                new Action<string>(OnWebViewHooked),
                new Action<string>(OnWebViewCookies),
                false,
                true,
                string.Empty,
                0,
                0,
                true,
                0,
                true,
                true,
                false);

            ApplyWebViewPlacement();
            InvokeWebView("SetVisibility", visibleOnStart);
            InvokeWebView("LoadURL", GetPlayerUrl());
            isInitialized = true;
            Debug.Log("[RadioGreeWebViewPlayer] WebView initialized for radio IFrame player.");
            return true;
        }

        public void SetVisible(bool visible)
        {
            if (!EnsureInitialized())
            {
                return;
            }

            InvokeWebView("SetVisibility", visible);
        }

        public void SetVolume(int volume)
        {
            this.volume = Mathf.Clamp(volume, 0, 100);

            if (!EnsureInitialized())
            {
                return;
            }

            EvaluateRadioScript($"window.GreenLightRadio.setVolume({this.volume});");
        }

        private void OnPlayRequested(RadioPlaybackRequest request)
        {
            pendingVideoId = request.YoutubeVideoId;
            pendingDisplayName = request.DisplayName;

            if (!EnsureInitialized())
            {
                return;
            }

            string videoId = EscapeJavaScriptString(request.YoutubeVideoId);
            string displayName = EscapeJavaScriptString(request.DisplayName);
            EvaluateRadioScript($"window.GreenLightRadio.loadVideo('{videoId}', '{displayName}', true);");
        }

        private void OnPauseRequested()
        {
            if (!EnsureInitialized())
            {
                return;
            }

            EvaluateRadioScript("window.GreenLightRadio.pause();");
        }

        private void OnWebViewLoaded(string message)
        {
            Debug.Log($"[RadioGreeWebViewPlayer] Loaded: {message}");
            SetVolume(volume);

            if (!string.IsNullOrWhiteSpace(pendingVideoId))
            {
                string videoId = EscapeJavaScriptString(pendingVideoId);
                string displayName = EscapeJavaScriptString(pendingDisplayName);
                EvaluateRadioScript($"window.GreenLightRadio.loadVideo('{videoId}', '{displayName}', true);");
            }
        }

        private void OnJavaScriptMessage(string message)
        {
            if (TryHandleWebPlayerMessage(message))
            {
                return;
            }

            Debug.Log($"[RadioGreeWebViewPlayer] JS: {message}");
        }

        private void OnWebViewError(string message)
        {
            Debug.LogWarning($"[RadioGreeWebViewPlayer] Error: {message}");
        }

        private void OnWebViewHttpError(string message)
        {
            Debug.LogWarning($"[RadioGreeWebViewPlayer] HTTP Error: {message}");
        }

        private void OnWebViewStarted(string message)
        {
            Debug.Log($"[RadioGreeWebViewPlayer] Started: {message}");
        }

        private void OnWebViewHooked(string message)
        {
            Debug.Log($"[RadioGreeWebViewPlayer] Hooked: {message}");
        }

        private void OnWebViewCookies(string message)
        {
            Debug.Log($"[RadioGreeWebViewPlayer] Cookies: {message}");
        }

        private bool EnsureInitialized()
        {
            return isInitialized || InitializeWebView();
        }

        private void EvaluateRadioScript(string script)
        {
            InvokeWebView("EvaluateJS", script);
        }

        private void ApplyWebViewPlacement()
        {
            int width = Mathf.Clamp(playerSize.x, 160, Mathf.Max(160, Screen.width));
            int height = Mathf.Clamp(playerSize.y, 120, Mathf.Max(120, Screen.height));
            int paddingX = Mathf.Max(0, screenPadding.x);
            int paddingY = Mathf.Max(0, screenPadding.y);

            if (isUsingDraggedPlacement)
            {
                SetWebViewRect(currentPlayerLeft, currentPlayerTop, width, height);
                return;
            }

            int left = paddingX;
            int top = paddingY;

            switch (anchor)
            {
                case WebViewAnchor.TopLeft:
                    break;
                case WebViewAnchor.TopRight:
                    left = Mathf.Max(0, Screen.width - paddingX - width);
                    break;
                case WebViewAnchor.BottomLeft:
                    top = Mathf.Max(0, Screen.height - paddingY - height);
                    break;
                case WebViewAnchor.BottomRight:
                    left = Mathf.Max(0, Screen.width - paddingX - width);
                    top = Mathf.Max(0, Screen.height - paddingY - height);
                    break;
            }

            SetWebViewRect(left, top, width, height);
        }

        private void SetWebViewRect(int left, int top, int width, int height)
        {
            int clampedLeft = Mathf.Clamp(left, 0, Mathf.Max(0, Screen.width - width));
            int clampedTop = Mathf.Clamp(top, 0, Mathf.Max(0, Screen.height - height));
            int right = Mathf.Max(0, Screen.width - clampedLeft - width);
            int bottom = Mathf.Max(0, Screen.height - clampedTop - height);

            currentPlayerLeft = clampedLeft;
            currentPlayerTop = clampedTop;
            InvokeWebView("SetMargins", clampedLeft, clampedTop, right, bottom, false);
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
        }

        private bool TryHandleWebPlayerMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            WebPlayerMessage webPlayerMessage;

            try
            {
                webPlayerMessage = JsonUtility.FromJson<WebPlayerMessage>(message);
            }
            catch (ArgumentException)
            {
                return false;
            }

            if (webPlayerMessage == null || webPlayerMessage.eventName != "dragDelta" || webPlayerMessage.payload == null)
            {
                return false;
            }

            MoveWebViewByDelta(webPlayerMessage.payload.dx, webPlayerMessage.payload.dy);
            return true;
        }

        private void MoveWebViewByDelta(float deltaX, float deltaY)
        {
            if (!allowDragFromWebPlayer)
            {
                return;
            }

            int width = Mathf.Clamp(playerSize.x, 160, Mathf.Max(160, Screen.width));
            int height = Mathf.Clamp(playerSize.y, 120, Mathf.Max(120, Screen.height));
            int nextLeft = currentPlayerLeft + Mathf.RoundToInt(deltaX);
            int nextTop = currentPlayerTop + Mathf.RoundToInt(deltaY);

            isUsingDraggedPlacement = true;
            SetWebViewRect(nextLeft, nextTop, width, height);
        }

        private void BindRadioService()
        {
            if (radioService == null)
            {
                return;
            }

            radioService.PlayRequested -= OnPlayRequested;
            radioService.PauseRequested -= OnPauseRequested;
            radioService.PlayRequested += OnPlayRequested;
            radioService.PauseRequested += OnPauseRequested;
        }

        private void UnbindRadioService()
        {
            if (radioService == null)
            {
                return;
            }

            radioService.PlayRequested -= OnPlayRequested;
            radioService.PauseRequested -= OnPauseRequested;
        }

        private object InvokeWebView(string methodName, params object[] args)
        {
            if (webViewObject == null || webViewType == null)
            {
                return null;
            }

            MethodInfo method = webViewType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);

            if (method == null)
            {
                Debug.LogWarning($"[RadioGreeWebViewPlayer] WebViewObject.{methodName} was not found.");
                return null;
            }

            return method.Invoke(webViewObject, args);
        }

        private static Type FindWebViewObjectType()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(WebViewObjectTypeName);

                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private string GetPlayerUrl()
        {
            if (servePlayerFromLocalhost && TryStartLocalServer(out string playerUrl))
            {
                return playerUrl;
            }

            return GetPlayerFileUrl();
        }

        private bool TryStartLocalServer(out string playerUrl)
        {
            if (localServer == null)
            {
                string playerDirectory = Path.Combine(Application.dataPath, PlayerDirectoryRelativePath);
                localServer = new RadioWebPlayerLocalServer(playerDirectory, localServerPort);
            }

            if (localServer.Start())
            {
                playerUrl = localServer.PlayerUrl;
                return true;
            }

            playerUrl = string.Empty;
            return false;
        }

        private void StopLocalServer()
        {
            localServer?.Stop();
            localServer = null;
        }

        private static string GetPlayerFileUrl()
        {
            string assetPath = Path.Combine(Application.dataPath, PlayerRelativePath);

            if (File.Exists(assetPath))
            {
                return "file:///" + assetPath.Replace("\\", "/").Replace(" ", "%20");
            }

            string streamingPath = Path.Combine(Application.streamingAssetsPath, "Radio/IFramePlayer/radio-player.html");
            return "file:///" + streamingPath.Replace("\\", "/").Replace(" ", "%20");
        }

        private static string EscapeJavaScriptString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", string.Empty)
                .Replace("\n", "\\n");
        }

        // Unity setup: Import gree/unity-webview runtime files, then attach this beside RadioService.
    }
}
