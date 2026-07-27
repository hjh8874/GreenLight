using UnityEngine;

namespace CityFlow.View
{
    [DisallowMultipleComponent]
    public sealed class GridCellView : MonoBehaviour
    {
        [SerializeField] private Transform ground;

        private GameObject decorationInstance;

        public Vector2Int Coordinate { get; private set; }
        public Transform Ground => ground;
        public GameObject DecorationInstance => decorationInstance;
        public bool HasDecoration => decorationInstance != null;

        public void Initialize(Vector2Int coordinate)
        {
            Coordinate = coordinate;
        }

        public void SetDecoration(GameObject instance)
        {
            if (instance == null)
            {
                Debug.LogWarning("[GridCellView] Cannot assign a null decoration.", this);
                return;
            }

            if (decorationInstance != null && decorationInstance != instance)
            {
                Destroy(decorationInstance);
            }

            decorationInstance = instance;
            decorationInstance.transform.SetParent(transform, false);
        }

        public void RemoveDecoration()
        {
            if (decorationInstance == null)
            {
                return;
            }

            Destroy(decorationInstance);
            decorationInstance = null;
        }
    }
}
