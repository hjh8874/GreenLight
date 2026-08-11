using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI.Feed
{
    public sealed class GreenFeedPanelController : MonoBehaviour
    {
        private const float TickerOutlineWidth = 0.22f;
        private const string TickerOutlineKeyword = "OUTLINE_ON";

        [Header("Panel")]
        [SerializeField] private RectTransform panelRect;
        [SerializeField] private CanvasGroup panelCanvasGroup;
        [SerializeField, Min(0.05f)] private float animationDuration = 0.18f;
        // 닫혀 있을 땐 오브젝트째 끈다 — 화면 중앙을 가리는 물건이라 알파 0으로
        // 남겨두면 레이캐스트·레이아웃 비용이 계속 돈다.
        [SerializeField, Range(0.8f, 1f)] private float closedScale = 0.94f;

        [Header("Ticker")]
        // 상시 노출되는 한 줄. 패널은 이 위로 펼쳐지므로 티커를 덮지 않는다 —
        // 그래서 "티커 다시 클릭"이 항상 닫기로 동작한다.
        [SerializeField] private GreenFeedPostView tickerView;

        [Header("Feed")]
        [SerializeField] private ScrollRect feedScrollRect;
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private GreenFeedPostView postTemplate;
        [SerializeField, Min(1)] private int maximumPosts = 50;

        [Header("Legacy")]
        // 리베이킹 전 씬은 릴레이의 clickAction이 None으로 로드된다. 그 씬에서
        // 예전처럼 호버로 열리게 하는 지연 닫기 시간이다.
        [SerializeField, Min(0f)] private float legacyCloseDelay = 0.12f;

        private Coroutine animationRoutine;
        private Coroutine legacyCloseRoutine;
        private bool legacyPointerInside;
        // IsOpen은 애니메이션이 끝나야 갱신된다. 연타에도 토글이 맞으려면
        // 목표 상태를 즉시 기억해야 한다.
        private bool openTarget;

        public bool IsOpen { get; private set; }
        public GreenFeedPostView TickerView => tickerView;

        public void RebindTicker(GreenFeedPostView targetTickerView)
        {
            if (targetTickerView == null)
            {
                return;
            }

            tickerView = targetTickerView;
            ConfigureTickerPresentation();
        }

        private void Awake()
        {
            ConfigureTickerPresentation();
            SetOpenImmediate(false);
        }

        private void ConfigureTickerPresentation()
        {
            if (tickerView == null)
            {
                return;
            }

            RectTransform tickerRect =
                tickerView.GetComponent<RectTransform>();
            RectTransform topBar = FindTopBar();
            if (topBar != null && tickerRect.parent != topBar)
            {
                tickerRect.SetParent(topBar, false);
            }

            float topBarHeight = topBar != null && topBar.rect.height > 0f
                ? topBar.rect.height
                : HudTopBarLayout.TopBarHeight;
            tickerRect.anchorMin = new Vector2(0.5f, 0.5f);
            tickerRect.anchorMax = new Vector2(1f, 0.5f);
            tickerRect.pivot = new Vector2(0.5f, 0.5f);
            tickerRect.offsetMin = new Vector2(
                HudTopBarLayout.HarvestButtonWidth * 0.5f +
                HudTopBarLayout.HorizontalGap,
                -(topBarHeight * 0.5f - HudTopBarLayout.VerticalInset));
            tickerRect.offsetMax = new Vector2(
                -HudTopBarLayout.HorizontalGap,
                topBarHeight * 0.5f - HudTopBarLayout.VerticalInset);

            Image background = tickerView.GetComponent<Image>();
            if (background != null)
            {
                Color color = background.color;
                color.a = 0.52f;
                background.color = color;
            }

            TMP_Text[] texts =
                tickerView.GetComponentsInChildren<TMP_Text>(true);
            for (int index = 0; index < texts.Length; index++)
            {
                texts[index].color = Color.white;
                texts[index].fontWeight = FontWeight.SemiBold;
                AddTickerTextOutline(texts[index]);
            }
        }

        public void AttachTickerToTopBar(RectTransform topBar)
        {
            if (tickerView == null || topBar == null)
            {
                return;
            }

            RectTransform tickerRect =
                tickerView.GetComponent<RectTransform>();
            if (tickerRect.parent != topBar)
            {
                tickerRect.SetParent(topBar, false);
            }

            ConfigureTickerPresentation();
        }

        private RectTransform FindTopBar()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                return null;
            }

            RectTransform[] rects =
                canvas.GetComponentsInChildren<RectTransform>(true);
            for (int index = 0; index < rects.Length; index++)
            {
                if (rects[index].name == "HUD_TopBar")
                {
                    return rects[index];
                }
            }

            return null;
        }

        private static void AddTickerTextOutline(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            if (text.font == null || text.fontSharedMaterial == null)
            {
                return;
            }

            Material outlineMaterial = text.fontMaterial;
            if (outlineMaterial == null)
            {
                return;
            }

            Color outlineColor = Color.black;
            outlineMaterial.EnableKeyword(TickerOutlineKeyword);
            outlineMaterial.SetColor("_OutlineColor", outlineColor);
            outlineMaterial.SetFloat("_OutlineWidth", TickerOutlineWidth);
            text.UpdateMeshPadding();
            text.SetMaterialDirty();
        }

        private void OnDisable()
        {
            GreenFeedInputGuard.Release(this);
        }

        public void Configure(
            RectTransform targetPanel,
            CanvasGroup targetCanvasGroup,
            ScrollRect targetScrollRect,
            RectTransform targetContentRoot,
            GreenFeedPostView targetPostTemplate,
            GreenFeedPostView targetTickerView)
        {
            panelRect = targetPanel;
            panelCanvasGroup = targetCanvasGroup;
            feedScrollRect = targetScrollRect;
            contentRoot = targetContentRoot;
            postTemplate = targetPostTemplate;
            tickerView = targetTickerView;
        }

        /// <summary>
        /// 리베이킹 전 씬을 위한 하위 호환 경로. 릴레이의 clickAction이 직렬화상
        /// None으로 로드되는 씬에서만 호출되며, 예전처럼 호버로 패널이 열린다.
        /// 베이커가 명시적 Toggle/Close/Locate/Passive를 넣은 씬은 이 경로를 타지 않는다.
        /// </summary>
        public void NotifyLegacyHoverEntered()
        {
            legacyPointerInside = true;
            StopLegacyCloseRoutine();
            GreenFeedInputGuard.SetPointerCaptured(this, true);
            SetOpen(true);
        }

        public void NotifyLegacyHoverExited()
        {
            legacyPointerInside = false;
            StopLegacyCloseRoutine();
            if (!isActiveAndEnabled)
            {
                CloseFromLegacyHover();
                return;
            }

            legacyCloseRoutine = StartCoroutine(LegacyCloseAfterDelay());
        }

        private IEnumerator LegacyCloseAfterDelay()
        {
            if (legacyCloseDelay > 0f)
            {
                yield return new WaitForSecondsRealtime(legacyCloseDelay);
            }

            legacyCloseRoutine = null;
            // 유예 중에 다시 들어왔으면 닫지 않는다 — 티커와 패널 사이를 지날 때
            // 깜빡이는 것을 막는 원래 동작이다.
            if (legacyPointerInside) yield break;

            CloseFromLegacyHover();
        }

        private void CloseFromLegacyHover()
        {
            SetOpen(false);
            GreenFeedInputGuard.Release(this);
        }

        private void StopLegacyCloseRoutine()
        {
            if (legacyCloseRoutine == null) return;
            StopCoroutine(legacyCloseRoutine);
            legacyCloseRoutine = null;
        }

        // 티커 버튼이 부르는 진입점 — 베이커가 지속 리스너로 연결한다.
        public void Toggle()
        {
            SetOpen(!openTarget);
        }

        public void Close()
        {
            SetOpen(false);
        }

        // 호버는 이제 개폐와 무관하다. 피드 위에서 휠을 굴렸을 때 게임 카메라가
        // 같이 줌되지 않도록 입력 가드만 잡는다.
        public void NotifyPointerEntered()
        {
            GreenFeedInputGuard.SetPointerCaptured(this, true);
        }

        public void NotifyPointerExited()
        {
            GreenFeedInputGuard.Release(this);
        }

        public void SetMaximumPosts(int value)
        {
            maximumPosts = Mathf.Max(1, value);
            TrimOldPosts();
        }

        public GreenFeedPostView AddPost(
            string authorName,
            string occupation,
            string message,
            string timestamp,
            string avatarInitial,
            Color accentColor,
            bool hasLocation,
            Vector2Int tile)
        {
            if (postTemplate == null || contentRoot == null)
            {
                Debug.LogWarning("[GreenFeed] A post could not be added because the baked template is missing.");
                return null;
            }

            GreenFeedPostView post = Instantiate(postTemplate, contentRoot);
            post.gameObject.name = $"FeedPost_{authorName}";
            post.gameObject.SetActive(true);
            post.transform.SetSiblingIndex(Mathf.Min(1, contentRoot.childCount - 1));
            post.Bind(authorName, occupation, message, timestamp, avatarInitial, accentColor);
            if (hasLocation)
            {
                post.BindTile(tile);
            }

            // 티커에는 좌표를 넣지 않는다 — 티커 클릭은 패널 열기를 유지한다.
            // 두 동작을 겹치면 무엇이 일어날지 예측할 수 없다.
            if (tickerView != null)
            {
                tickerView.Bind(
                    authorName,
                    occupation,
                    message,
                    timestamp,
                    avatarInitial,
                    accentColor);
            }

            TrimOldPosts();
            StartCoroutine(ScrollToNewest());
            return post;
        }

        public void ClearPosts()
        {
            if (contentRoot == null)
            {
                return;
            }

            List<GameObject> postsToRemove = new List<GameObject>();
            for (int i = 0; i < contentRoot.childCount; i++)
            {
                Transform child = contentRoot.GetChild(i);
                if ((postTemplate == null || child.gameObject != postTemplate.gameObject) &&
                    child.GetComponent<GreenFeedPostView>() != null)
                {
                    postsToRemove.Add(child.gameObject);
                }
            }

            foreach (GameObject post in postsToRemove)
            {
                Destroy(post);
            }
        }

        private void SetOpen(bool shouldOpen)
        {
            if (panelRect == null || panelCanvasGroup == null)
            {
                return;
            }

            openTarget = shouldOpen;
            if (animationRoutine != null)
            {
                StopCoroutine(animationRoutine);
            }

            animationRoutine = StartCoroutine(AnimatePanel(shouldOpen));
        }

        // 자리를 옮기지 않는다 — 씬에서 잡아둔 위치가 곧 표시 위치다.
        // 살짝 커지며 페이드인하는 것만으로 "떴다"는 느낌은 충분하다.
        private IEnumerator AnimatePanel(bool shouldOpen)
        {
            if (shouldOpen)
            {
                panelRect.gameObject.SetActive(true);
                panelCanvasGroup.interactable = true;
                panelCanvasGroup.blocksRaycasts = true;
            }

            float startAlpha = panelCanvasGroup.alpha;
            float targetAlpha = shouldOpen ? 1f : 0f;
            float startScale = panelRect.localScale.x;
            float targetScale = shouldOpen ? 1f : closedScale;
            float elapsed = 0f;

            while (elapsed < animationDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / animationDuration);
                float easedTime = normalizedTime * normalizedTime * (3f - 2f * normalizedTime);
                panelCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, easedTime);
                float scale = Mathf.Lerp(startScale, targetScale, easedTime);
                panelRect.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }

            ApplyOpenState(shouldOpen);
            animationRoutine = null;
        }

        private void SetOpenImmediate(bool shouldOpen)
        {
            if (panelRect == null || panelCanvasGroup == null)
            {
                return;
            }

            ApplyOpenState(shouldOpen);
        }

        private void ApplyOpenState(bool shouldOpen)
        {
            panelCanvasGroup.alpha = shouldOpen ? 1f : 0f;
            // 패널 위에 커서를 둔 채 닫으면 OnPointerExit이 오지 않는다 —
            // 비활성화 직전에 직접 풀지 않으면 휠 가드가 영구히 잡힌다.
            if (!shouldOpen)
            {
                GreenFeedInputGuard.Release(this);
            }

            panelCanvasGroup.interactable = shouldOpen;
            panelCanvasGroup.blocksRaycasts = shouldOpen;
            float scale = shouldOpen ? 1f : closedScale;
            panelRect.localScale = new Vector3(scale, scale, 1f);
            panelRect.gameObject.SetActive(shouldOpen);
            IsOpen = shouldOpen;
            openTarget = shouldOpen;
        }

        private void TrimOldPosts()
        {
            if (contentRoot == null)
            {
                return;
            }

            List<GreenFeedPostView> activePosts = new List<GreenFeedPostView>();
            for (int i = 0; i < contentRoot.childCount; i++)
            {
                GreenFeedPostView post = contentRoot.GetChild(i).GetComponent<GreenFeedPostView>();
                if (post != null && post != postTemplate && post.gameObject.activeSelf)
                {
                    activePosts.Add(post);
                }
            }

            for (int i = maximumPosts; i < activePosts.Count; i++)
            {
                Destroy(activePosts[i].gameObject);
            }
        }

        private IEnumerator ScrollToNewest()
        {
            yield return null;
            if (feedScrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                feedScrollRect.verticalNormalizedPosition = 1f;
            }
        }

        // Unity setup: Bake through Tools > GreenLight > UI > Bake Green SNS Feed.
    }
}
