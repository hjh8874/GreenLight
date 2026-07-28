using CityFlow.Bootstrap;
using CityFlow.Content.Transit;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.UI.Controllers
{
    [RequireComponent(typeof(BusRoute))]
    public sealed class SchoolBusRouteView :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        [SerializeField]
        private BusRoute busRoute;

        [SerializeField]
        private bool useXYPlane = true;

        [SerializeField]
        private Color busColor =
            new Color(1f, 0.82f, 0.12f, 1f);

        [SerializeField]
        private Vector2 visualSize =
            new Vector2(0.5f, 0.28f);

        [SerializeField]
        private float visualDepth = -0.24f;

        private GameObject busVisual;
        private Texture2D busTexture;
        private Sprite busSprite;
        private bool isSubscribed;
        private CityFlowServices services;

        public void Initialize(CityFlowServices cityFlowServices)
        {
            services = cityFlowServices;
        }

        private void Awake()
        {
            if (busRoute == null)
            {
                busRoute = GetComponent<BusRoute>();
            }

            EnsureVisual();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();

            if (busVisual != null)
            {
                busVisual.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();

            if (busSprite != null)
            {
                Destroy(busSprite);
            }

            if (busTexture != null)
            {
                Destroy(busTexture);
            }
        }

        private void Subscribe()
        {
            if (isSubscribed || busRoute == null)
            {
                return;
            }

            busRoute.TileChanged += OnTileChanged;
            busRoute.RouteUnavailable += OnRouteUnavailable;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed || busRoute == null)
            {
                return;
            }

            busRoute.TileChanged -= OnTileChanged;
            busRoute.RouteUnavailable -= OnRouteUnavailable;
            isSubscribed = false;
        }

        private void OnTileChanged(Vector2Int tile)
        {
            EnsureVisual();

            if (busVisual == null)
            {
                return;
            }

            IWorldCoordinateSpace coordinateSpace =
                services?.WorldCoordinates;
            if (coordinateSpace != null)
            {
                busVisual.transform.position =
                    coordinateSpace.GridToWorld(
                        tile,
                        Mathf.Abs(visualDepth));
                busVisual.transform.rotation =
                    coordinateSpace.CoordinateRotation;
                busVisual.transform.localScale = new Vector3(
                    visualSize.x,
                    visualSize.y,
                    1f);
            }
            else if (useXYPlane)
            {
                Vector3 position = GridUtil.GridToWorld(tile);
                position.z = visualDepth;
                busVisual.transform.position = position;
                busVisual.transform.rotation = Quaternion.identity;
                busVisual.transform.localScale = new Vector3(
                    visualSize.x,
                    visualSize.y,
                    1f);
            }
            else
            {
                busVisual.transform.position = new Vector3(
                    tile.x + 0.5f,
                    Mathf.Abs(visualDepth),
                    tile.y + 0.5f
                );
                busVisual.transform.rotation =
                    Quaternion.Euler(90f, 0f, 0f);
                busVisual.transform.localScale = new Vector3(
                    visualSize.x,
                    visualSize.y,
                    1f);
            }

            busVisual.SetActive(true);
        }

        private void OnRouteUnavailable()
        {
            if (busVisual != null)
            {
                busVisual.SetActive(false);
            }
        }

        private void EnsureVisual()
        {
            if (busVisual != null)
            {
                return;
            }

            busTexture = new Texture2D(
                1,
                1,
                TextureFormat.RGBA32,
                false
            )
            {
                name = "Runtime School Bus Texture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            busTexture.SetPixel(0, 0, Color.white);
            busTexture.Apply(false, false);

            busSprite = Sprite.Create(
                busTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f
            );
            busSprite.name = "Runtime School Bus Sprite";

            busVisual = new GameObject("SchoolBusVisual");
            busVisual.transform.SetParent(transform, false);

            SpriteRenderer renderer =
                busVisual.AddComponent<SpriteRenderer>();
            renderer.sprite = busSprite;
            renderer.color = busColor;
            renderer.sortingOrder = 120;

            busVisual.SetActive(false);
        }
    }
}
