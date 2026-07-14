using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.Content
{
    /// <summary>
    /// 상점에서 사용하는 전체 상품 목록입니다.
    ///
    /// 각 상품의 ShopItemDataSO를 등록하면,
    /// 상점 UI가 이 목록을 읽어서 버튼을 생성할 수 있습니다.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ShopCatalog",
        menuName = "CityFlow/Content/Shop Catalog"
    )]
    public class ShopCatalogSO : ScriptableObject
    {
        [Header("상점 상품 목록")]

        [Tooltip(
            "상점에서 표시할 모든 상품 데이터를 등록합니다."
        )]
        [SerializeField]
        private List<ShopItemDataSO> items =
            new List<ShopItemDataSO>();

        public IReadOnlyList<ShopItemDataSO> Items =>
            items;

        /// <summary>
        /// 상품 ID를 사용해
        /// 상점 상품 데이터를 찾습니다.
        /// </summary>
        public ShopItemDataSO GetItemById(
            string itemId
        )
        {
            if (string.IsNullOrWhiteSpace(
                itemId
            ))
            {
                return null;
            }

            for (int i = 0;
                 i < items.Count;
                 i++)
            {
                ShopItemDataSO item =
                    items[i];

                if (item == null)
                {
                    continue;
                }

                if (item.ItemId ==
                    itemId)
                {
                    return item;
                }
            }

            return null;
        }

        /// <summary>
        /// 특정 카테고리에 해당하는
        /// 상품들만 반환합니다.
        /// </summary>
        public List<ShopItemDataSO>
            GetItemsByCategory(
                ShopItemCategory category
            )
        {
            List<ShopItemDataSO> result =
                new List<ShopItemDataSO>();

            for (int i = 0;
                 i < items.Count;
                 i++)
            {
                ShopItemDataSO item =
                    items[i];

                if (item == null)
                {
                    continue;
                }

                if (item.Category ==
                    category)
                {
                    result.Add(item);
                }
            }

            return result;
        }

        /// <summary>
        /// 현재 조건에서 해금된 상품들만 반환합니다.
        /// </summary>
        public List<ShopItemDataSO>
            GetUnlockedItems(
                CityTier currentCityTier,
                int currentPopulation,
                long totalArrivals
            )
        {
            List<ShopItemDataSO> result =
                new List<ShopItemDataSO>();

            for (int i = 0;
                 i < items.Count;
                 i++)
            {
                ShopItemDataSO item =
                    items[i];

                if (item == null)
                {
                    continue;
                }

                if (item.IsUnlocked(
                    currentCityTier,
                    currentPopulation,
                    totalArrivals
                ))
                {
                    result.Add(item);
                }
            }

            return result;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            HashSet<string> registeredIds =
                new HashSet<string>();

            for (int i = 0;
                 i < items.Count;
                 i++)
            {
                ShopItemDataSO item =
                    items[i];

                if (item == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(
                    item.ItemId
                ))
                {
                    Debug.LogWarning(
                        $"[ShopCatalogSO] " +
                        $"{item.name}의 Item ID가 비어 있습니다.",
                        item
                    );

                    continue;
                }

                if (!registeredIds.Add(
                    item.ItemId
                ))
                {
                    Debug.LogWarning(
                        $"[ShopCatalogSO] " +
                        $"중복된 Item ID가 있습니다: " +
                        $"{item.ItemId}",
                        item
                    );
                }
            }
        }
#endif
    }
}