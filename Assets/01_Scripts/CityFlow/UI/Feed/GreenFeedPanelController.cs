using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI.Feed
{
    public sealed class GreenFeedPanelController : MonoBehaviour
    {
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

        private Coroutine animationRoutine;
        // IsOpen은 애니메이션이 끝나야 갱신된다. 연타에도 토글이 맞으려면
        // 목표 상태를 즉시 기억해야 한다.
        private bool openTarget;

        public bool IsOpen { get; private set; }
        public GreenFeedPostView TickerView => tickerView;

        private void Awake()
        {
            SetOpenImmediate(false);
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
