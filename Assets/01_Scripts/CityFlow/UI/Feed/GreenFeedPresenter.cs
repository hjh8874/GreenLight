using CityFlow.Feed;
using UnityEngine;

namespace CityFlow.UI.Feed
{
    public sealed class GreenFeedPresenter : MonoBehaviour
    {
        [SerializeField] private CitizenFeedService feedService;
        [SerializeField] private GreenFeedPanelController panelController;
        [SerializeField] private bool clearPreviewPostsAtRuntime = true;

        private bool subscribed;

        private void OnEnable()
        {
            Subscribe();
        }

        private void Start()
        {
            if (panelController == null)
            {
                panelController = GetComponent<GreenFeedPanelController>();
            }

            if (clearPreviewPostsAtRuntime && panelController != null)
            {
                panelController.ClearPosts();
            }

            if (feedService != null && feedService.Settings != null && panelController != null)
            {
                panelController.SetMaximumPosts(feedService.Settings.MaximumVisiblePosts);
            }

            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        public void Configure(
            CitizenFeedService targetFeedService,
            GreenFeedPanelController targetPanelController)
        {
            Unsubscribe();
            feedService = targetFeedService;
            panelController = targetPanelController;
            Subscribe();
        }

        private void Subscribe()
        {
            if (subscribed || feedService == null)
            {
                return;
            }

            feedService.PostGenerated += OnPostGenerated;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || feedService == null)
            {
                return;
            }

            feedService.PostGenerated -= OnPostGenerated;
            subscribed = false;
        }

        private void OnPostGenerated(CitizenFeedPost post)
        {
            if (panelController == null)
            {
                return;
            }

            panelController.AddPost(
                post.AuthorName,
                post.RoleLabel,
                post.Message,
                post.Timestamp,
                post.AvatarInitial,
                post.AccentColor,
                post.HasLocation,
                post.Tile);
        }

        // Unity setup: The Green SNS baker connects the service and panel references.
    }
}
