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
        [SerializeField] private Vector2 shownPosition = new Vector2(-10f, 0f);
        [SerializeField] private Vector2 hiddenPosition = new Vector2(354f, 0f);
        [SerializeField, Min(0.05f)] private float animationDuration = 0.22f;
        [SerializeField, Min(0f)] private float closeDelay = 0.12f;

        [Header("Feed")]
        [SerializeField] private ScrollRect feedScrollRect;
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private GreenFeedPostView postTemplate;
        [SerializeField, Min(1)] private int maximumPosts = 50;

        private Coroutine animationRoutine;
        private Coroutine closeRoutine;
        private bool isPointerInside;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            SetOpenImmediate(false);
        }

        private void OnDisable()
        {
            isPointerInside = false;
            GreenFeedInputGuard.Release(this);
        }

        public void Configure(
            RectTransform targetPanel,
            CanvasGroup targetCanvasGroup,
            ScrollRect targetScrollRect,
            RectTransform targetContentRoot,
            GreenFeedPostView targetPostTemplate,
            Vector2 targetShownPosition,
            Vector2 targetHiddenPosition)
        {
            panelRect = targetPanel;
            panelCanvasGroup = targetCanvasGroup;
            feedScrollRect = targetScrollRect;
            contentRoot = targetContentRoot;
            postTemplate = targetPostTemplate;
            shownPosition = targetShownPosition;
            hiddenPosition = targetHiddenPosition;
        }

        public void NotifyPointerEntered()
        {
            isPointerInside = true;
            StopCloseRoutine();
            GreenFeedInputGuard.SetPointerCaptured(this, true);
            SetOpen(true);
        }

        public void NotifyPointerExited()
        {
            isPointerInside = false;
            StopCloseRoutine();
            closeRoutine = StartCoroutine(CloseAfterDelay());
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
            Color accentColor)
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

        private IEnumerator CloseAfterDelay()
        {
            if (closeDelay > 0f)
            {
                yield return new WaitForSecondsRealtime(closeDelay);
            }

            closeRoutine = null;
            if (isPointerInside)
            {
                yield break;
            }

            SetOpen(false);
            GreenFeedInputGuard.Release(this);
        }

        private void SetOpen(bool shouldOpen)
        {
            if (panelRect == null || panelCanvasGroup == null)
            {
                return;
            }

            if (animationRoutine != null)
            {
                StopCoroutine(animationRoutine);
            }

            animationRoutine = StartCoroutine(AnimatePanel(shouldOpen));
        }

        private IEnumerator AnimatePanel(bool shouldOpen)
        {
            Vector2 startPosition = panelRect.anchoredPosition;
            Vector2 targetPosition = shouldOpen ? shownPosition : hiddenPosition;
            float startAlpha = panelCanvasGroup.alpha;
            float targetAlpha = shouldOpen ? 1f : 0f;
            float elapsed = 0f;

            if (shouldOpen)
            {
                panelCanvasGroup.interactable = true;
                panelCanvasGroup.blocksRaycasts = true;
            }

            while (elapsed < animationDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / animationDuration);
                float easedTime = normalizedTime * normalizedTime * (3f - 2f * normalizedTime);
                panelRect.anchoredPosition = Vector2.LerpUnclamped(startPosition, targetPosition, easedTime);
                panelCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, easedTime);
                yield return null;
            }

            panelRect.anchoredPosition = targetPosition;
            panelCanvasGroup.alpha = targetAlpha;
            panelCanvasGroup.interactable = shouldOpen;
            panelCanvasGroup.blocksRaycasts = shouldOpen;
            IsOpen = shouldOpen;
            animationRoutine = null;
        }

        private void SetOpenImmediate(bool shouldOpen)
        {
            if (panelRect == null || panelCanvasGroup == null)
            {
                return;
            }

            panelRect.anchoredPosition = shouldOpen ? shownPosition : hiddenPosition;
            panelCanvasGroup.alpha = shouldOpen ? 1f : 0f;
            panelCanvasGroup.interactable = shouldOpen;
            panelCanvasGroup.blocksRaycasts = shouldOpen;
            IsOpen = shouldOpen;
        }

        private void StopCloseRoutine()
        {
            if (closeRoutine == null)
            {
                return;
            }

            StopCoroutine(closeRoutine);
            closeRoutine = null;
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
