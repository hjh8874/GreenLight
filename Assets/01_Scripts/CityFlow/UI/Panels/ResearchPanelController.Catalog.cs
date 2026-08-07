using System;
using System.Collections.Generic;
using CityFlow.Contracts;
using CityFlow.Gameplay.Research;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public sealed partial class ResearchPanelController
    {
        private readonly struct CategoryTab
        {
            public CategoryTab(ResearchCategory? category, Button button)
            {
                Category = category;
                Button = button;
            }

            public ResearchCategory? Category { get; }
            public Button Button { get; }
        }

        private readonly List<CategoryTab> categoryTabs = new();
        private readonly List<TMP_Text> laneHeaderLabels = new();
        private const float CategoryListWidth = 500f;
        private const float CategoryCellHeight = 108f;
        private const float CategoryRowGap = 12f;
        private const int GeneratedRoundedSpriteSize = 32;
        private const float GeneratedRoundedRadius = 10f;
        private const float ResearchIconBadgeSize = 46f;
        private const float ResearchIconTextLeft = 64f;
        [Header("Presentation")]
        [Tooltip("선택 사항입니다. 비워 두면 프로토타입 9-Slice 라운드 이미지를 자동 생성합니다.")]
        [SerializeField] private Sprite roundedSurfaceSprite;
        private Sprite resolvedRoundedSurfaceSprite;
        private Sprite generatedRoundedSurfaceSprite;
        private Texture2D generatedRoundedSurfaceTexture;
        private Button unlockMenuButton;
        private RectTransform categoryBar;
        private RectTransform laneHeaderBar;
        private RectTransform headerSurface;
        private TMP_Text catalogTitleText;
        private TMP_Text catalogSubtitleText;
        private TMP_Text activeResearchText;
        private ResearchCategory? selectedCategory;
        private bool catalogVisible;
        private bool catalogPresentationReady;

        internal Button UnlockMenuButtonForTest => unlockMenuButton;
        internal bool IsCatalogVisibleForTest => catalogVisible;

        private void EnsureCatalogPresentation()
        {
            // The unlock catalog replaces this legacy summary. Hide it even when
            // the rest of the catalog presentation has already been prepared.
            SetHeaderVisible(yesterdayArrivalsText, false);

            if (catalogPresentationReady)
            {
                BindUnlockMenuButton();
                return;
            }

            unlockMenuButton = FindMenuButton();
            TMP_Text styleSource = unlockMenuButton != null
                ? unlockMenuButton.GetComponentInChildren<TMP_Text>(true)
                : FindTemplateText();

            if (rowTemplate == null)
            {
                CreateFallbackCatalogView(styleSource);
                styleSource = FindTemplateText() ?? styleSource;
            }

            if (unlockMenuButton == null)
            {
                unlockMenuButton = CreateButton(
                    "Unlock",
                    transform,
                    "닫기",
                    styleSource,
                    new Color(0.08f, 0.43f, 0.36f, 1f));
            }

            unlockMenuButton.name = "Unlock";
            SetButtonLabel(unlockMenuButton, "닫기");
            ApplyRoundedSurface(
                unlockMenuButton.targetGraphic as Image);
            ApplySoftShadow(
                unlockMenuButton.targetGraphic as Graphic,
                0.26f,
                new Vector2(0f, -3f));
            PositionUnlockButton(unlockMenuButton);
            EnsureCategoryBar(styleSource);
            EnsureCatalogHeader(styleSource);
            BindUnlockMenuButton();
            catalogPresentationReady = true;
        }

        private void InitializeCatalogPresentation()
        {
            catalogVisible = true;
            selectedCategory = null;
            ApplyCatalogSelection();
        }

        private void ReleaseCatalogPresentation()
        {
            if (unlockMenuButton != null)
            {
                unlockMenuButton.onClick.RemoveListener(
                    CloseResearchPanel);
            }
        }

        private void BindUnlockMenuButton()
        {
            if (unlockMenuButton == null)
            {
                return;
            }

            unlockMenuButton.onClick.RemoveListener(
                CloseResearchPanel);
            unlockMenuButton.onClick.AddListener(
                CloseResearchPanel);
            unlockMenuButton.interactable = true;
        }

        private void CloseResearchPanel()
        {
            UIDockController dock = dockController != null
                ? dockController
                : FindAnyObjectByType<UIDockController>(
                    FindObjectsInactive.Include);
            if (dock != null)
            {
                dock.CloseAllPanels();
                return;
            }

            gameObject.SetActive(false);
        }

        private void SelectCategory(ResearchCategory? category)
        {
            selectedCategory = category;
            ApplyCatalogSelection();
        }

        private void RefreshCatalogPresentation()
        {
            if (!catalogPresentationReady)
            {
                return;
            }

            ApplyCatalogSelection();
        }

        private void ApplyCatalogSelection()
        {
            if (!catalogPresentationReady)
            {
                return;
            }

            if (categoryBar != null)
            {
                categoryBar.gameObject.SetActive(catalogVisible);
            }
            RefreshLaneHeaders();

            SetHeaderVisible(yesterdayArrivalsText, false);
            SetHeaderVisible(populationText, catalogVisible);
            SetHeaderVisible(unlockProgressText, catalogVisible);

            int visibleIndex = 0;
            bool expansionSelected =
                selectedCategory == ResearchCategory.Expansion;
            Row currentExpansion = expansionSelected
                ? FindCurrentExpansionRow()
                : null;
            for (int index = 0; index < rows.Count; index++)
            {
                Row row = rows[index];
                bool matches = selectedCategory.HasValue
                    ? row.Entry.category == selectedCategory.Value &&
                      (!expansionSelected || row == currentExpansion)
                    : row.Entry.category != ResearchCategory.Expansion;
                bool visible = catalogVisible && matches;
                row.Instance.SetActive(visible);
                if (!visible)
                {
                    continue;
                }

                RectTransform rect = GetRect(row.Instance);
                int column = selectedCategory.HasValue
                    ? 0
                    : GetOverallCategoryColumn(row.Entry.category);
                int line = expansionSelected
                    ? 0
                    : selectedCategory.HasValue
                    ? GetVerticalListIndex(row)
                    : GetOverallCategoryListIndex(row);
                RectTransform panel = GetComponent<RectTransform>();
                float panelWidth = panel != null
                    ? Mathf.Max(720f, panel.rect.width)
                    : 720f;
                float cardWidth = selectedCategory.HasValue
                    ? Mathf.Min(
                        CategoryListWidth,
                        Mathf.Max(CellWidth, panelWidth - PanelPadding * 2f))
                    : CellWidth;
                float cardHeight = selectedCategory.HasValue
                    ? CategoryCellHeight
                    : CellHeight;
                float rowGap = selectedCategory.HasValue
                    ? CategoryRowGap
                    : RowGap;
                float horizontalPosition = selectedCategory.HasValue
                    ? Mathf.Max(PanelPadding, (panelWidth - cardWidth) * 0.5f)
                    : PanelPadding + column * (CellWidth + ColumnGap);
                rect.sizeDelta = new Vector2(cardWidth, cardHeight);
                LayoutResearchCardContent(row, cardHeight);
                rect.anchoredPosition = new Vector2(
                    horizontalPosition,
                    -(HeaderHeight + PanelPadding) -
                    line * (cardHeight + rowGap));

                visibleIndex++;
            }

            ResizePanelToGrid();

            UpdateConnectorGeometry();
            UpdateConnectorVisibility();

            UpdateCategoryTabColors();
            UpdateUnlockButtonColor();
        }

        private Row FindCurrentExpansionRow()
        {
            Row lastCompleted = null;
            for (int index = 0; index < rows.Count; index++)
            {
                Row row = rows[index];
                if (row.Entry.category != ResearchCategory.Expansion)
                {
                    continue;
                }

                if (row.IsResearching)
                {
                    return row;
                }

                if (!row.IsUnlocked)
                {
                    return row;
                }

                lastCompleted = row;
            }

            return lastCompleted;
        }

        private void UpdateConnectorVisibility()
        {
            var rowById = new Dictionary<string, Row>(
                StringComparer.Ordinal);
            for (int index = 0; index < rows.Count; index++)
            {
                rowById[Normalize(rows[index].Entry.researchId)] =
                    rows[index];
            }

            int connectorIndex = 0;
            for (int index = 0; index < rows.Count; index++)
            {
                Row child = rows[index];
                string prerequisite = Normalize(
                    child.Entry.prerequisiteId);
                if (prerequisite.Length == 0 ||
                    !rowById.TryGetValue(prerequisite, out Row parent))
                {
                    continue;
                }

                if (connectorIndex >= connectors.Count)
                {
                    break;
                }

                connectors[connectorIndex++].SetActive(
                    catalogVisible &&
                    !selectedCategory.HasValue &&
                    parent.Instance.activeSelf &&
                    child.Instance.activeSelf);
            }

            while (connectorIndex < connectors.Count)
            {
                connectors[connectorIndex++].SetActive(false);
            }
        }

        private int GetVerticalListIndex(Row target)
        {
            int line = 0;
            int targetIndex = rows.IndexOf(target);
            for (int index = 0; index < rows.Count; index++)
            {
                Row candidate = rows[index];
                if (candidate == target ||
                    !selectedCategory.HasValue ||
                    candidate.Entry.category != selectedCategory.Value)
                {
                    continue;
                }

                bool comesBefore = candidate.Branch < target.Branch ||
                    (candidate.Branch == target.Branch &&
                     (candidate.Depth < target.Depth ||
                      (candidate.Depth == target.Depth &&
                       index < targetIndex)));
                if (comesBefore)
                {
                    line++;
                }
            }
            return line;
        }

        private static int GetOverallCategoryColumn(
            ResearchCategory category) =>
            category switch
            {
                ResearchCategory.Commercial => 0,
                ResearchCategory.Infrastructure => 1,
                ResearchCategory.PublicService => 2,
                _ => 2
            };

        private int GetOverallCategoryListIndex(Row target)
        {
            int line = 0;
            int targetIndex = rows.IndexOf(target);
            for (int index = 0; index < rows.Count; index++)
            {
                Row candidate = rows[index];
                if (candidate == target ||
                    candidate.Entry.category != target.Entry.category)
                {
                    continue;
                }

                bool comesBefore = candidate.Branch < target.Branch ||
                    (candidate.Branch == target.Branch &&
                     (candidate.Depth < target.Depth ||
                      (candidate.Depth == target.Depth &&
                       index < targetIndex)));
                if (comesBefore) line++;
            }
            return line;
        }

        private void RefreshLaneHeaders()
        {
            bool visible = catalogVisible && !selectedCategory.HasValue;
            if (laneHeaderBar == null)
            {
                Transform existing = transform.Find("ResearchLaneHeaders");
                if (existing != null)
                {
                    laneHeaderBar = existing as RectTransform;
                    TMP_Text[] existingLabels =
                        laneHeaderBar.GetComponentsInChildren<TMP_Text>(true);
                    for (int index = 0;
                         index < existingLabels.Length;
                         index++)
                    {
                        laneHeaderLabels.Add(existingLabels[index]);
                    }
                }
                else
                {
                    var headers = new GameObject(
                        "ResearchLaneHeaders",
                        typeof(RectTransform));
                    headers.transform.SetParent(transform, false);
                    laneHeaderBar = headers.GetComponent<RectTransform>();
                }
            }

            laneHeaderBar.gameObject.SetActive(visible);
            if (!visible) return;

            laneHeaderBar.anchorMin = new Vector2(0f, 1f);
            laneHeaderBar.anchorMax = new Vector2(0f, 1f);
            laneHeaderBar.pivot = new Vector2(0f, 1f);
            laneHeaderBar.anchoredPosition =
                new Vector2(PanelPadding, -140f);
            laneHeaderBar.sizeDelta = new Vector2(716f, 24f);

            const int categoryCount = 3;
            while (laneHeaderLabels.Count < categoryCount)
            {
                int index = laneHeaderLabels.Count;
                TMP_Text label = CreateText(
                    $"Lane_{index}",
                    laneHeaderBar,
                    string.Empty,
                    FindTemplateText(),
                    TextAlignmentOptions.Center,
                    12f);
                label.fontStyle = FontStyles.Bold;
                label.color = new Color(0.75f, 0.84f, 0.94f, 1f);
                laneHeaderLabels.Add(label);
            }

            string[] labels = { "상업", "인프라", "공공" };
            for (int category = 0;
                 category < laneHeaderLabels.Count;
                 category++)
            {
                TMP_Text label = laneHeaderLabels[category];
                bool categoryExists = category < categoryCount;
                label.gameObject.SetActive(categoryExists);
                if (!categoryExists) continue;

                label.text = labels[category];
                RectTransform rect = label.rectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition =
                    new Vector2(category * (CellWidth + ColumnGap), 0f);
                rect.sizeDelta = new Vector2(CellWidth, 24f);
            }
        }

        private void EnsureCategoryBar(TMP_Text styleSource)
        {
            Transform existing = transform.Find("CategoryTabs");
            if (existing != null)
            {
                categoryBar = existing as RectTransform;
            }
            else
            {
                var tabs = new GameObject(
                    "CategoryTabs",
                    typeof(RectTransform));
                tabs.transform.SetParent(transform, false);
                categoryBar = tabs.GetComponent<RectTransform>();
            }

            // Existing scene instances may carry old prefab offsets. Reapply
            // the anchor contract every time so runtime layout is deterministic.
            categoryBar.anchorMin = new Vector2(0f, 1f);
            categoryBar.anchorMax = new Vector2(0f, 1f);
            categoryBar.pivot = new Vector2(0f, 1f);
            categoryBar.anchoredPosition =
                new Vector2(PanelPadding, -102f);
            categoryBar.sizeDelta = new Vector2(630f, 34f);

            if (categoryTabs.Count > 0)
            {
                return;
            }

            CreateCategoryTab(null, "전체", styleSource, 0);
            CreateCategoryTab(
                ResearchCategory.Commercial,
                "상업",
                styleSource,
                1);
            CreateCategoryTab(
                ResearchCategory.Infrastructure,
                "인프라",
                styleSource,
                2);
            CreateCategoryTab(
                ResearchCategory.PublicService,
                "공공",
                styleSource,
                3);
            CreateCategoryTab(
                ResearchCategory.Expansion,
                "개척",
                styleSource,
                4);
        }

        private void CreateCategoryTab(
            ResearchCategory? category,
            string label,
            TMP_Text styleSource,
            int index)
        {
            Button button = CreateButton(
                $"Category_{label}",
                categoryBar,
                label,
                styleSource,
                new Color(0.14f, 0.16f, 0.19f, 1f));
            RectTransform rect =
                button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(index * 126f, 0f);
            rect.sizeDelta = new Vector2(116f, 32f);
            button.onClick.AddListener(
                () => SelectCategory(category));
            categoryTabs.Add(new CategoryTab(category, button));
        }

        private void CreateFallbackCatalogView(TMP_Text styleSource)
        {
            var rowsObject = new GameObject(
                "Rows",
                typeof(RectTransform));
            rowsObject.transform.SetParent(transform, false);
            RectTransform rowsRect =
                rowsObject.GetComponent<RectTransform>();
            rowsRect.anchorMin = Vector2.zero;
            rowsRect.anchorMax = Vector2.one;
            rowsRect.offsetMin = Vector2.zero;
            rowsRect.offsetMax = Vector2.zero;

            rowTemplate = new GameObject(
                "RowTemplate",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button),
                typeof(CanvasGroup));
            rowTemplate.transform.SetParent(rowsObject.transform, false);
            RectTransform templateRect =
                rowTemplate.GetComponent<RectTransform>();
            templateRect.sizeDelta =
                new Vector2(CellWidth, CellHeight);
            Image templateImage = rowTemplate.GetComponent<Image>();
            templateImage.color =
                new Color(0.21f, 0.22f, 0.26f, 1f);
            ApplyRoundedSurface(templateImage);
            rowTemplate.GetComponent<Button>().targetGraphic =
                templateImage;

            CreateCardText(
                rowTemplate.transform,
                "Name",
                "건물",
                styleSource,
                new Vector2(14f, -38f),
                new Vector2(-14f, -12f),
                TextAlignmentOptions.TopLeft,
                16f);
            CreateCardText(
                rowTemplate.transform,
                "Progress",
                "조건",
                styleSource,
                new Vector2(14f, -67f),
                new Vector2(-14f, -42f),
                TextAlignmentOptions.TopLeft,
                12f);
            CreateCardText(
                rowTemplate.transform,
                "State",
                "잠김",
                styleSource,
                new Vector2(14f, -91f),
                new Vector2(-14f, -70f),
                TextAlignmentOptions.BottomRight,
                12f);
            rowTemplate.SetActive(false);

            yesterdayArrivalsText = null;
            populationText = CreateHeaderText(
                "Population",
                "인구 0",
                styleSource);
            unlockProgressText = CreateHeaderText(
                "UnlockProgress",
                "해금 0/0",
                styleSource);
        }

        private TMP_Text CreateHeaderText(
            string name,
            string value,
            TMP_Text styleSource)
        {
            return CreateText(
                name,
                transform,
                value,
                styleSource,
                TextAlignmentOptions.TopLeft,
                13f);
        }

        private static TMP_Text CreateCardText(
            Transform parent,
            string name,
            string value,
            TMP_Text styleSource,
            Vector2 offsetMin,
            Vector2 offsetMax,
            TextAlignmentOptions alignment,
            float fontSize)
        {
            TMP_Text text = CreateText(
                name,
                parent,
                value,
                styleSource,
                alignment,
                fontSize);
            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return text;
        }

        private Button CreateButton(
            string name,
            Transform parent,
            string label,
            TMP_Text styleSource,
            Color color)
        {
            var buttonObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            Image image = buttonObject.GetComponent<Image>();
            image.color = color;
            ApplyRoundedSurface(image);
            ApplySoftShadow(
                image,
                0.20f,
                new Vector2(0f, -2f));
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.94f, 0.97f, 1f, 1f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.86f, 0.90f, 0.94f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.colorMultiplier = 1f;
            button.colors = colors;

            TMP_Text text = CreateText(
                "Label",
                buttonObject.transform,
                label,
                styleSource,
                TextAlignmentOptions.Center,
                14f);
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 4f);
            textRect.offsetMax = new Vector2(-10f, -4f);
            return button;
        }

        private static TMP_Text CreateText(
            string name,
            Transform parent,
            string value,
            TMP_Text styleSource,
            TextAlignmentOptions alignment,
            float fontSize)
        {
            var textObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            TMP_Text text = textObject.GetComponent<TMP_Text>();
            if (styleSource != null)
            {
                text.font = styleSource.font;
                text.fontSharedMaterial =
                    styleSource.fontSharedMaterial;
            }
            text.color = Color.white;
            text.text = value;
            text.fontSize = fontSize;
            text.enableAutoSizing = false;
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        private Button FindMenuButton()
        {
            Button[] buttons =
                GetComponentsInChildren<Button>(true);
            for (int index = 0; index < buttons.Length; index++)
            {
                string objectName = buttons[index].name.Trim();
                if (objectName.Equals(
                        "Upgrade",
                        StringComparison.OrdinalIgnoreCase) ||
                    objectName.Equals(
                        "Unlock",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return buttons[index];
                }
            }

            return null;
        }

        private TMP_Text FindTemplateText()
        {
            return rowTemplate != null
                ? rowTemplate.GetComponentInChildren<TMP_Text>(true)
                : null;
        }

        private static void SetButtonLabel(
            Button button,
            string label)
        {
            TMP_Text text =
                button?.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
                text.color = Color.white;
                text.fontStyle = FontStyles.Bold;
                text.fontSize = 15f;
            }
        }

        private static void PositionUnlockButton(Button button)
        {
            RectTransform rect =
                button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-18f, -16f);
            rect.sizeDelta = new Vector2(108f, 40f);
        }

        private static void SetHeaderVisible(
            TMP_Text header,
            bool visible)
        {
            if (header != null)
            {
                header.gameObject.SetActive(visible);
            }
        }

        private void UpdateCategoryTabColors()
        {
            for (int index = 0;
                 index < categoryTabs.Count;
                 index++)
            {
                CategoryTab tab = categoryTabs[index];
                bool selected = tab.Category == selectedCategory;
                Image image = tab.Button != null
                    ? tab.Button.targetGraphic as Image
                    : null;
                if (image != null)
                {
                    image.color = selected
                        ? new Color(0.10f, 0.60f, 0.47f, 1f)
                        : new Color(0.17f, 0.21f, 0.27f, 1f);
                }
            }
        }

        private void UpdateUnlockButtonColor()
        {
            SetButtonLabel(unlockMenuButton, "닫기");

            Image image = unlockMenuButton != null
                ? unlockMenuButton.targetGraphic as Image
                : null;
            if (image != null)
            {
                image.color = new Color(0.82f, 0.31f, 0.30f, 1f);
            }
        }

        private void EnsureCatalogHeader(TMP_Text styleSource)
        {
            Transform existing = transform.Find("CatalogHeader");
            if (existing != null)
            {
                headerSurface = existing as RectTransform;
            }
            else
            {
                var header = new GameObject(
                    "CatalogHeader",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                header.transform.SetParent(transform, false);
                headerSurface = header.GetComponent<RectTransform>();
            }

            Image headerImage = headerSurface.GetComponent<Image>() ??
                                headerSurface.gameObject.AddComponent<Image>();
            headerImage.color = new Color(0.10f, 0.15f, 0.19f, 0.98f);
            headerImage.raycastTarget = false;
            ApplyRoundedSurface(headerImage);
            ApplySoftShadow(
                headerImage,
                0.24f,
                new Vector2(0f, -3f));

            catalogTitleText = FindOrCreateHeaderText(
                "Title",
                "건물 해금 연구",
                styleSource,
                18f,
                FontStyles.Bold);
            catalogSubtitleText = FindOrCreateHeaderText(
                "Subtitle",
                "조건을 충족한 뒤 비용을 지불하면 건설 항목이 해금됩니다.",
                styleSource,
                11f,
                FontStyles.Normal);
            activeResearchText = FindOrCreateHeaderText(
                "ActiveResearch",
                "진행 중인 연구 없음",
                styleSource,
                12f,
                FontStyles.Normal);
        }

        private TMP_Text FindOrCreateHeaderText(
            string name,
            string value,
            TMP_Text styleSource,
            float fontSize,
            FontStyles fontStyle)
        {
            Transform existing = headerSurface.Find(name);
            TMP_Text text = existing != null
                ? existing.GetComponent<TMP_Text>()
                : CreateText(
                    name,
                    headerSurface,
                    value,
                    styleSource,
                    TextAlignmentOptions.TopLeft,
                    fontSize);
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            return text;
        }

        private void LayoutCatalogHeader(RectTransform panel)
        {
            if (headerSurface == null)
            {
                EnsureCatalogHeader(FindTemplateText());
            }

            headerSurface.SetParent(panel, false);
            headerSurface.SetAsFirstSibling();
            headerSurface.anchorMin = new Vector2(0f, 1f);
            headerSurface.anchorMax = new Vector2(1f, 1f);
            headerSurface.pivot = new Vector2(0.5f, 1f);
            headerSurface.offsetMin = new Vector2(12f, -94f);
            headerSurface.offsetMax = new Vector2(-12f, -10f);

            LayoutHeaderText(
                catalogTitleText,
                new Vector2(16f, -14f),
                new Vector2(360f, 26f),
                TextAlignmentOptions.TopLeft);
            LayoutHeaderText(
                catalogSubtitleText,
                new Vector2(16f, -42f),
                new Vector2(520f, 22f),
                TextAlignmentOptions.TopLeft);
            LayoutHeaderText(
                activeResearchText,
                new Vector2(318f, -69f),
                new Vector2(330f, 24f),
                TextAlignmentOptions.MidlineRight);

            if (populationText != null &&
                populationText.transform.parent != headerSurface)
            {
                populationText.rectTransform.SetParent(headerSurface, false);
            }
            if (unlockProgressText != null &&
                unlockProgressText.transform.parent != headerSurface)
            {
                unlockProgressText.rectTransform.SetParent(headerSurface, false);
            }
            LayoutHeaderText(
                populationText,
                new Vector2(16f, -69f),
                new Vector2(126f, 24f),
                TextAlignmentOptions.MidlineLeft);
            LayoutHeaderText(
                unlockProgressText,
                new Vector2(150f, -69f),
                new Vector2(142f, 24f),
                TextAlignmentOptions.MidlineLeft);

            if (categoryBar != null)
            {
                categoryBar.SetParent(panel, false);
                categoryBar.SetAsLastSibling();
                categoryBar.anchoredPosition =
                    new Vector2(PanelPadding, -102f);
            }
            if (unlockMenuButton != null)
            {
                unlockMenuButton.transform.SetAsLastSibling();
            }
        }

        private static void LayoutHeaderText(
            TMP_Text text,
            Vector2 position,
            Vector2 size,
            TextAlignmentOptions alignment)
        {
            if (text == null) return;
            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }

        private void ConfigureResearchCard(Row row)
        {
            RectTransform cardRect = GetRect(row.Instance);
            cardRect.sizeDelta = new Vector2(CellWidth, CellHeight);
            EnsureResearchIcon(row);
            LayoutResearchCardContent(row, CellHeight);

            Transform accent = row.Instance.transform.Find("Accent");
            if (accent == null)
            {
                var accentObject = new GameObject(
                    "Accent",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                accentObject.transform.SetParent(
                    row.Instance.transform,
                    false);
                accent = accentObject.transform;
            }
            row.AccentImage = accent.GetComponent<Image>();
            row.AccentImage.raycastTarget = false;
            RectTransform accentRect = accent as RectTransform;
            accentRect.anchorMin = new Vector2(0f, 1f);
            accentRect.anchorMax = new Vector2(1f, 1f);
            accentRect.pivot = new Vector2(0.5f, 1f);
            accentRect.offsetMin = new Vector2(0f, -4f);
            accentRect.offsetMax = Vector2.zero;

            Transform badge = row.Instance.transform.Find("Category");
            row.CategoryText = badge != null
                ? badge.GetComponent<TMP_Text>()
                : CreateText(
                    "Category",
                    row.Instance.transform,
                    GetCategoryLabel(row.Entry.category),
                    row.NameText,
                    TextAlignmentOptions.TopRight,
                    10.5f);
            RectTransform badgeRect = row.CategoryText.rectTransform;
            badgeRect.anchorMin = new Vector2(1f, 1f);
            badgeRect.anchorMax = new Vector2(1f, 1f);
            badgeRect.pivot = new Vector2(1f, 1f);
            badgeRect.anchoredPosition = new Vector2(-12f, -14f);
            badgeRect.sizeDelta = new Vector2(72f, 20f);
            row.CategoryText.gameObject.SetActive(false);

            Transform stateBadge = row.Instance.transform.Find("StateBadge");
            if (stateBadge == null)
            {
                var badgeObject = new GameObject(
                    "StateBadge",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                badgeObject.transform.SetParent(row.Instance.transform, false);
                stateBadge = badgeObject.transform;
            }
            row.StateBadgeImage = stateBadge.GetComponent<Image>();
            row.StateBadgeImage.raycastTarget = false;
            ApplyRoundedSurface(row.StateBadgeImage);
            if (row.StateText != null)
            {
                stateBadge.SetSiblingIndex(
                    Mathf.Max(0, row.StateText.transform.GetSiblingIndex()));
            }
            LayoutStateBadge(row, CellHeight);

            Shadow shadow = row.Instance.GetComponent<Shadow>() ??
                            row.Instance.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.32f);
            shadow.effectDistance = new Vector2(0f, -3f);
            shadow.useGraphicAlpha = true;

            Button button = row.Instance.GetComponent<Button>();
            if (button != null)
            {
                ColorBlock colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
                colors.pressedColor = new Color(0.86f, 0.90f, 0.94f, 1f);
                colors.disabledColor = new Color(0.78f, 0.80f, 0.84f, 1f);
                colors.colorMultiplier = 1.08f;
                colors.fadeDuration = 0.10f;
                button.colors = colors;
            }
        }

        private void EnsureResearchIcon(Row row)
        {
            Transform badge = row.Instance.transform.Find("BuildingIconBadge");
            if (badge == null)
            {
                var badgeObject = new GameObject(
                    "BuildingIconBadge",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                badgeObject.transform.SetParent(row.Instance.transform, false);
                badge = badgeObject.transform;
            }

            row.IconBadge = badge.gameObject;
            Image badgeImage = badge.GetComponent<Image>();
            badgeImage.color = new Color(0.10f, 0.14f, 0.19f, 0.88f);
            badgeImage.raycastTarget = false;
            ApplyRoundedSurface(badgeImage);

            RectTransform badgeRect = badge as RectTransform;
            badgeRect.anchorMin = new Vector2(0f, 1f);
            badgeRect.anchorMax = new Vector2(0f, 1f);
            badgeRect.pivot = new Vector2(0f, 1f);
            badgeRect.anchoredPosition = new Vector2(10f, -9f);
            badgeRect.sizeDelta = new Vector2(
                ResearchIconBadgeSize,
                ResearchIconBadgeSize);

            Transform icon = badge.Find("Icon");
            if (icon == null)
            {
                var iconObject = new GameObject(
                    "Icon",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                iconObject.transform.SetParent(badge, false);
                icon = iconObject.transform;
            }

            row.IconImage = icon.GetComponent<Image>();
            row.IconImage.raycastTarget = false;
            row.IconImage.preserveAspect = true;
            RectTransform iconRect = icon as RectTransform;
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = Vector2.one;
            iconRect.offsetMax = -Vector2.one;
            row.IconBadge.SetActive(false);
        }

        private void RefreshResearchIcons()
        {
            if (rows.Count == 0) return;

            SpecialBuildingBuildOption[] specialOptions =
                specialBuildings?.CreateBuildOptionSnapshot() ??
                Array.Empty<SpecialBuildingBuildOption>();
            BuildSlotController[] buildSlots =
                FindObjectsByType<BuildSlotController>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                Row row = rows[rowIndex];
                if (row?.IconImage == null || row.IconBadge == null)
                {
                    continue;
                }

                Sprite icon = ResolveResearchIcon(
                    row.Entry?.researchId,
                    specialOptions,
                    buildSlots);
                row.IconImage.sprite = icon;
                row.IconImage.color = Color.white;
                row.IconBadge.SetActive(icon != null);
                LayoutResearchCardContent(row, CellHeight);
            }
        }

        private static Sprite ResolveResearchIcon(
            string researchId,
            IReadOnlyList<SpecialBuildingBuildOption> specialOptions,
            IReadOnlyList<BuildSlotController> buildSlots)
        {
            if (string.IsNullOrWhiteSpace(researchId)) return null;
            string normalizedId = researchId.Trim();

            for (int index = 0; index < specialOptions.Count; index++)
            {
                SpecialBuildingBuildOption option = specialOptions[index];
                if (string.Equals(
                        option.RequiredResearchId,
                        normalizedId,
                        StringComparison.Ordinal) &&
                    option.Icon != null)
                {
                    return option.Icon;
                }
            }

            for (int index = 0; index < buildSlots.Count; index++)
            {
                BuildSlotController slot = buildSlots[index];
                if (slot == null || slot.TileData == null)
                {
                    continue;
                }

                if (string.Equals(
                        slot.TileData.RequiredResearchId,
                        normalizedId,
                        StringComparison.Ordinal) &&
                    slot.TileData.BuildingIcon != null)
                {
                    return slot.TileData.BuildingIcon;
                }
            }

            return null;
        }

        private static void LayoutResearchCardContent(
            Row row,
            float cardHeight)
        {
            float nameLeft = row.IconBadge != null &&
                             row.IconBadge.activeSelf
                ? ResearchIconTextLeft
                : 14f;
            float progressLeft = row.IconBadge != null &&
                                 row.IconBadge.activeSelf
                ? ResearchIconTextLeft
                : 14f;
            float nameTop = 12f;
            float nameBottom = 36f;
            float progressTop = 42f;
            float progressBottom = 70f;
            float stateTop = 76f;
            float stateBottom = cardHeight - 6f;
            LayoutCardLabel(
                row.NameText,
                new Vector2(nameLeft, -nameTop),
                new Vector2(-14f, -nameBottom),
                16f,
                TextAlignmentOptions.TopLeft);
            LayoutCardLabel(
                row.ProgressText,
                new Vector2(progressLeft, -progressTop),
                new Vector2(-14f, -progressBottom),
                13.5f,
                TextAlignmentOptions.TopLeft);
            if (row.ProgressText != null)
            {
                row.ProgressText.textWrappingMode = TextWrappingModes.Normal;
            }
            LayoutCardLabel(
                row.StateText,
                new Vector2(14f, -stateTop),
                new Vector2(-14f, -stateBottom),
                12.5f,
                TextAlignmentOptions.MidlineRight);
            LayoutStateBadge(row, cardHeight);
        }

        private static void LayoutStateBadge(Row row, float cardHeight)
        {
            if (row.StateBadgeImage == null) return;
            RectTransform rect = row.StateBadgeImage.rectTransform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            float top = 74f;
            rect.anchoredPosition = new Vector2(-10f, -top);
            rect.sizeDelta = new Vector2(174f, 26f);
        }

        private void ApplyRoundedSurface(Image image)
        {
            if (image == null) return;
            Sprite sprite = ResolveRoundedSurfaceSprite();
            if (sprite == null) return;

            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.preserveAspect = false;
            image.pixelsPerUnitMultiplier = 1f;
        }

        private static void ApplySoftShadow(
            Graphic graphic,
            float alpha,
            Vector2 distance)
        {
            if (graphic == null) return;

            Shadow shadow = graphic.GetComponent<Shadow>() ??
                            graphic.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.01f, 0.03f, 0.06f, alpha);
            shadow.effectDistance = distance;
            shadow.useGraphicAlpha = true;
        }

        private Sprite ResolveRoundedSurfaceSprite()
        {
            if (roundedSurfaceSprite != null)
            {
                return roundedSurfaceSprite;
            }
            if (resolvedRoundedSurfaceSprite != null)
            {
                return resolvedRoundedSurfaceSprite;
            }

            resolvedRoundedSurfaceSprite =
                CreateGeneratedRoundedSurfaceSprite();
            return resolvedRoundedSurfaceSprite;
        }

        private Sprite CreateGeneratedRoundedSurfaceSprite()
        {
            int size = GeneratedRoundedSpriteSize;
            float half = size * 0.5f;
            float inner = half - GeneratedRoundedRadius;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(
                        Mathf.Abs(x + 0.5f - half) - inner,
                        0f);
                    float dy = Mathf.Max(
                        Mathf.Abs(y + 0.5f - half) - inner,
                        0f);
                    float edgeDistance =
                        Mathf.Sqrt(dx * dx + dy * dy) -
                        GeneratedRoundedRadius;
                    byte alpha = (byte)Mathf.RoundToInt(
                        Mathf.Clamp01(0.5f - edgeDistance) * 255f);
                    pixels[y * size + x] =
                        new Color32(255, 255, 255, alpha);
                }
            }

            generatedRoundedSurfaceTexture = new Texture2D(
                size,
                size,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = "ResearchPanel_RoundedSurface",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            generatedRoundedSurfaceTexture.SetPixels32(pixels);
            generatedRoundedSurfaceTexture.Apply(false, true);

            generatedRoundedSurfaceSprite = Sprite.Create(
                generatedRoundedSurfaceTexture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(
                    GeneratedRoundedRadius,
                    GeneratedRoundedRadius,
                    GeneratedRoundedRadius,
                    GeneratedRoundedRadius));
            generatedRoundedSurfaceSprite.name =
                "ResearchPanel_RoundedSurface";
            generatedRoundedSurfaceSprite.hideFlags =
                HideFlags.HideAndDontSave;
            return generatedRoundedSurfaceSprite;
        }

        private void ReleaseCatalogStyleResources()
        {
            if (generatedRoundedSurfaceSprite != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(generatedRoundedSurfaceSprite);
                }
                else
                {
                    DestroyImmediate(generatedRoundedSurfaceSprite);
                }
                generatedRoundedSurfaceSprite = null;
            }
            if (generatedRoundedSurfaceTexture != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(generatedRoundedSurfaceTexture);
                }
                else
                {
                    DestroyImmediate(generatedRoundedSurfaceTexture);
                }
                generatedRoundedSurfaceTexture = null;
            }
            resolvedRoundedSurfaceSprite = null;
        }

        private static void LayoutCardLabel(
            TMP_Text text,
            Vector2 offsetMin,
            Vector2 offsetMax,
            float fontSize,
            TextAlignmentOptions alignment)
        {
            if (text == null) return;
            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            // With both Y anchors at the top, offsetMin is the bottom edge and
            // offsetMax is the top edge. Call sites describe the more readable
            // top-left and bottom-right bounds, so convert them here instead of
            // creating a negative-height text rectangle.
            rect.offsetMin = new Vector2(offsetMin.x, offsetMax.y);
            rect.offsetMax = new Vector2(offsetMax.x, offsetMin.y);
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }

        private void RefreshResearchCardVisual(Row row)
        {
            Color accent = row.IsUnlocked
                ? new Color(0.30f, 0.88f, 0.52f, 1f)
                : row.IsResearching
                    ? new Color(0.25f, 0.70f, 1f, 1f)
                    : row.IsReady
                        ? new Color(1f, 0.78f, 0.20f, 1f)
                        : new Color(0.52f, 0.59f, 0.68f, 1f);
            if (row.AccentImage != null)
            {
                row.AccentImage.color = accent;
            }
            if (row.CategoryText != null)
            {
                row.CategoryText.text = GetCategoryLabel(row.Entry.category);
                row.CategoryText.color = new Color(
                    accent.r,
                    accent.g,
                    accent.b,
                    0.94f);
            }
            if (row.StateBadgeImage != null)
            {
                row.StateBadgeImage.color = new Color(
                    accent.r,
                    accent.g,
                    accent.b,
                    row.IsUnlocked || row.IsReady || row.IsResearching
                        ? 0.28f
                        : 0.18f);
            }
        }

        private void RefreshCatalogSummary(
            int unlockedCount,
            int population)
        {
            if (populationText != null)
            {
                populationText.text = $"인구  {population:N0}";
                populationText.color = new Color(0.86f, 0.91f, 0.97f, 1f);
            }
            if (unlockProgressText != null)
            {
                unlockProgressText.text = $"해금  {unlockedCount}/{rows.Count}";
                unlockProgressText.color = new Color(0.86f, 0.91f, 0.97f, 1f);
            }
            if (activeResearchText == null) return;

            string activeId = research?.ActiveResearchId;
            if (string.IsNullOrEmpty(activeId))
            {
                activeResearchText.text = "진행 중인 연구 없음";
                activeResearchText.color = new Color(0.65f, 0.71f, 0.78f, 1f);
                return;
            }

            Row activeRow = null;
            for (int index = 0; index < rows.Count; index++)
            {
                if (Normalize(rows[index].Entry.researchId) ==
                    Normalize(activeId))
                {
                    activeRow = rows[index];
                    break;
                }
            }
            string displayName = activeRow?.Entry.displayName ?? activeId;
            int remaining = research.GetRemainingResearchHours(activeId);
            activeResearchText.text =
                $"진행 중  {displayName} · {remaining}시간";
            activeResearchText.color = new Color(0.45f, 0.82f, 1f, 1f);
        }

        private static string GetCategoryLabel(
            ResearchCategory category) =>
            category switch
            {
                ResearchCategory.Commercial => "상업",
                ResearchCategory.Infrastructure => "인프라",
                ResearchCategory.PublicService => "공공",
                ResearchCategory.Expansion => "개척",
                _ => "기타"
            };
    }
}
