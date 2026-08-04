using CityFlow.Bootstrap;
using CityFlow.Content;
using CityFlow.Contracts;
using CityFlow.Gameplay.Research;
using CityFlow.UI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public class ResearchUnlockCatalogTests
{
    [Test]
    public void BalanceCatalog_CategoriesMatchRuntimeCatalog()
    {
        const string runtimePath =
            "Assets/05_ScriptableObjects/Resources/CityFlow/ResearchCatalog.asset";
        const string balancePath =
            "Assets/05_ScriptableObjects/Balance/Editor/ResearchCatalog_Balance.asset";
        ResearchCatalogSO runtimeCatalog =
            AssetDatabase.LoadAssetAtPath<ResearchCatalogSO>(runtimePath);
        ResearchCatalogSO balanceCatalog =
            AssetDatabase.LoadAssetAtPath<ResearchCatalogSO>(balancePath);

        Assert.NotNull(runtimeCatalog, $"Missing runtime catalog: {runtimePath}");
        Assert.NotNull(balanceCatalog, $"Missing balance catalog: {balancePath}");

        var runtimeEntries = runtimeCatalog.ValidEntries();
        var balanceEntries = balanceCatalog.ValidEntries();
        Assert.AreEqual(
            runtimeEntries.Count,
            balanceEntries.Count,
            "Balance publishing copies the whole catalog, so both catalogs must contain the same entries.");

        for (int index = 0; index < runtimeEntries.Count; index++)
        {
            Assert.AreEqual(
                runtimeEntries[index].researchId,
                balanceEntries[index].researchId,
                $"Research entry order differs at index {index}.");
            Assert.AreEqual(
                runtimeEntries[index].category,
                balanceEntries[index].category,
                $"Publishing balance values would overwrite the category for {runtimeEntries[index].researchId}.");
            Assert.AreEqual(
                runtimeEntries[index].worldGridStageId ?? string.Empty,
                balanceEntries[index].worldGridStageId ?? string.Empty,
                $"Publishing balance values would overwrite the expansion reward for {runtimeEntries[index].researchId}.");
            if (runtimeEntries[index].category ==
                ResearchCategory.Expansion)
            {
                StringAssert.DoesNotContain(
                    "x",
                    runtimeEntries[index].displayName.ToLowerInvariant());
                StringAssert.DoesNotContain(
                    "×",
                    runtimeEntries[index].displayName);
            }
        }
    }

    [Test]
    public void ExistingYesterdayArrivalsHeader_IsHidden()
    {
        var owner = new GameObject("panel");
        var serviceOwner = new GameObject("research");
        var headerObject = new GameObject(
            "YesterdayArrivals",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        headerObject.transform.SetParent(owner.transform, false);
        try
        {
            CityFlowServices services =
                CreateServicesWithReadyResearch(
                    serviceOwner,
                    out ResearchCatalogSO catalog);
            var controller =
                owner.AddComponent<ResearchPanelController>();
            SetPrivate(controller, "catalog", catalog);
            SetPrivate(
                controller,
                "yesterdayArrivalsText",
                headerObject.GetComponent<TMP_Text>());

            controller.Initialize(services);

            Assert.IsFalse(headerObject.activeSelf);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(serviceOwner);
        }
    }

    [Test]
    public void ExistingUpgradeButton_BecomesUnlockAndOpensCatalog()
    {
        var owner = new GameObject("panel");
        var serviceOwner = new GameObject("research");
        try
        {
            Button upgradeButton =
                CreateButton(owner.transform, "Upgrade", "Upgrade");
            CityFlowServices services =
                CreateServicesWithReadyResearch(
                    serviceOwner,
                    out ResearchCatalogSO catalog);

            var controller =
                owner.AddComponent<ResearchPanelController>();
            SetPrivate(controller, "catalog", catalog);
            controller.Initialize(services);

            Assert.AreEqual(
                "Unlock",
                upgradeButton.name);
            Assert.AreEqual(
                "해금",
                upgradeButton
                    .GetComponentInChildren<TMP_Text>().text);
            AssertReadable(
                upgradeButton.GetComponentInChildren<TMP_Text>().color);
            TMP_Text[] categoryLabels = owner.transform
                .Find("CategoryTabs")
                .GetComponentsInChildren<TMP_Text>(true);
            Assert.That(
                categoryLabels,
                Has.All.Matches<TMP_Text>(
                    label => IsReadable(label.color)));
            Assert.That(
                categoryLabels,
                Has.Some.Matches<TMP_Text>(
                    label => label.text == "개척"));
            Assert.IsFalse(controller.IsCatalogVisibleForTest);
            Assert.IsFalse(
                controller.RowsForTest[0].Instance.activeSelf);

            upgradeButton.onClick.Invoke();

            Assert.IsTrue(controller.IsCatalogVisibleForTest);
            Assert.AreEqual(
                "닫기",
                upgradeButton
                    .GetComponentInChildren<TMP_Text>().text);
            Assert.IsTrue(
                controller.RowsForTest[0].Instance.activeSelf);

            upgradeButton.onClick.Invoke();

            Assert.IsFalse(controller.IsCatalogVisibleForTest);
            Assert.AreEqual(
                "해금",
                upgradeButton
                    .GetComponentInChildren<TMP_Text>().text);
            Assert.IsFalse(
                controller.RowsForTest[0].Instance.activeSelf);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(serviceOwner);
        }
    }

    [Test]
    public void ReadyResearch_EnablesResearchAvailableCard()
    {
        var owner = new GameObject("panel");
        var serviceOwner = new GameObject("research");
        try
        {
            Button upgradeButton =
                CreateButton(owner.transform, "Upgrade", "Upgrade");
            CityFlowServices services =
                CreateServicesWithReadyResearch(
                    serviceOwner,
                    out ResearchCatalogSO catalog);

            var controller =
                owner.AddComponent<ResearchPanelController>();
            SetPrivate(controller, "catalog", catalog);
            controller.Initialize(services);
            upgradeButton.onClick.Invoke();

            ResearchPanelController.Row row =
                controller.RowsForTest[0];
            Assert.IsTrue(row.IsReady);
            Assert.IsTrue(
                row.Instance.GetComponent<Button>().interactable);
            Assert.AreEqual("연구 가능", row.StateText.text);
            AssertReadable(row.NameText.color);
            AssertReadable(row.ProgressText.color);
            AssertReadable(row.StateText.color);
            Assert.NotNull(row.Instance.GetComponent<Outline>());
            Assert.NotNull(row.AccentImage);
            Assert.NotNull(row.StateBadgeImage);
            Assert.NotNull(row.CategoryText);
            Assert.AreEqual("상업", row.CategoryText.text);
            Assert.IsFalse(row.CategoryText.gameObject.activeSelf);
            Assert.That(
                row.Instance
                    .GetComponent<RectTransform>()
                    .rect.height,
                Is.EqualTo(88f).Within(0.01f));
            Transform laneHeaders = owner.transform.Find("ResearchLaneHeaders");
            Assert.NotNull(laneHeaders);
            Assert.IsTrue(laneHeaders.gameObject.activeSelf);
            TMP_Text[] laneLabels =
                laneHeaders.GetComponentsInChildren<TMP_Text>(true);
            Assert.AreEqual("상업", laneLabels[0].text);
            Assert.AreEqual("인프라", laneLabels[1].text);
            Assert.AreEqual("공공", laneLabels[2].text);
            Assert.That(row.NameText.rectTransform.rect.height, Is.GreaterThan(0f));
            Assert.That(row.ProgressText.rectTransform.rect.height, Is.GreaterThan(0f));
            Assert.That(row.StateText.rectTransform.rect.height, Is.GreaterThan(0f));
            Color cardColor =
                row.Instance.GetComponent<Image>().color;
            Assert.That(
                Mathf.Max(
                    cardColor.r,
                    cardColor.g,
                    cardColor.b),
                Is.GreaterThan(0.4f));

            Button[] categoryButtons = owner.transform
                .Find("CategoryTabs")
                .GetComponentsInChildren<Button>(true);
            categoryButtons[1].onClick.Invoke();
            RectTransform filteredCard =
                row.Instance.GetComponent<RectTransform>();
            Assert.IsFalse(laneHeaders.gameObject.activeSelf);
            Assert.That(
                filteredCard.rect.width,
                Is.EqualTo(520f).Within(0.01f));
            Assert.That(
                filteredCard.rect.height,
                Is.EqualTo(88f).Within(0.01f));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(serviceOwner);
        }
    }

    [Test]
    public void ResearchTree_FlowsFromTopToBottom()
    {
        var owner = new GameObject("panel", typeof(RectTransform));
        var serviceOwner = new GameObject("research");
        try
        {
            Button unlockButton =
                CreateButton(owner.transform, "Upgrade", "Upgrade");
            CityFlowServices services =
                CreateServicesWithReadyResearch(
                    serviceOwner,
                    out ResearchCatalogSO catalog);
            var controller =
                owner.AddComponent<ResearchPanelController>();
            SetPrivate(controller, "catalog", catalog);

            controller.Initialize(services);
            unlockButton.onClick.Invoke();

            Vector2 parentPosition = controller.RowsForTest[0].Instance
                .GetComponent<RectTransform>().anchoredPosition;
            Vector2 childPosition = controller.RowsForTest[1].Instance
                .GetComponent<RectTransform>().anchoredPosition;
            Vector2 infrastructurePosition = controller.RowsForTest[2].Instance
                .GetComponent<RectTransform>().anchoredPosition;
            Assert.That(
                childPosition.x,
                Is.EqualTo(parentPosition.x).Within(0.01f));
            Assert.That(childPosition.y, Is.LessThan(parentPosition.y));
            Assert.That(
                infrastructurePosition.y,
                Is.EqualTo(parentPosition.y).Within(0.01f));
            Assert.That(infrastructurePosition.x, Is.GreaterThan(parentPosition.x));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(serviceOwner);
        }
    }

    [Test]
    public void ExpansionCategory_PreservesPreviousThreeColumnOverview()
    {
        var owner = new GameObject("panel", typeof(RectTransform));
        var serviceOwner = new GameObject("research");
        ResearchCatalogSO catalog = null;
        try
        {
            Button unlockButton =
                CreateButton(owner.transform, "Upgrade", "Upgrade");
            CityFlowServices services =
                CreateServicesWithExpansionResearch(
                    serviceOwner,
                    out catalog);
            var controller =
                owner.AddComponent<ResearchPanelController>();
            SetPrivate(controller, "catalog", catalog);

            controller.Initialize(services);
            unlockButton.onClick.Invoke();

            Assert.That(
                controller.GetComponent<RectTransform>().rect.width,
                Is.EqualTo(756f).Within(0.01f));
            Assert.That(
                controller.RowsForTest,
                Has.None.Matches<ResearchPanelController.Row>(
                    row => row.Entry.category ==
                           ResearchCategory.Expansion &&
                           row.Instance.activeSelf));

            Button expansionButton = null;
            Button[] categoryButtons = owner.transform
                .Find("CategoryTabs")
                .GetComponentsInChildren<Button>(true);
            for (int index = 0; index < categoryButtons.Length; index++)
            {
                TMP_Text label = categoryButtons[index]
                    .GetComponentInChildren<TMP_Text>(true);
                if (label != null && label.text == "개척")
                {
                    expansionButton = categoryButtons[index];
                    break;
                }
            }

            Assert.NotNull(expansionButton);
            expansionButton.onClick.Invoke();

            int visibleExpansionRows = 0;
            ResearchPanelController.Row currentExpansion = null;
            for (int index = 0;
                 index < controller.RowsForTest.Count;
                 index++)
            {
                ResearchPanelController.Row row =
                    controller.RowsForTest[index];
                if (row.Entry.category == ResearchCategory.Expansion &&
                    row.Instance.activeSelf)
                {
                    visibleExpansionRows++;
                    currentExpansion = row;
                }
                else
                {
                    Assert.IsFalse(row.Instance.activeSelf);
                }
            }

            Assert.AreEqual(1, visibleExpansionRows);
            Assert.NotNull(currentExpansion);
            string firstExpansionId =
                currentExpansion.Entry.researchId;
            Assert.That(
                currentExpansion.Instance
                    .GetComponent<RectTransform>().rect.width,
                Is.EqualTo(520f).Within(0.01f));
            Rect panelRect = controller
                .GetComponent<RectTransform>().rect;
            Assert.That(panelRect.width, Is.EqualTo(756f).Within(0.01f));

            currentExpansion.Instance
                .GetComponent<Button>().onClick.Invoke();

            ResearchPanelController.Row nextExpansion = null;
            for (int index = 0;
                 index < controller.RowsForTest.Count;
                 index++)
            {
                ResearchPanelController.Row row =
                    controller.RowsForTest[index];
                if (row.Entry.category == ResearchCategory.Expansion &&
                    row.Instance.activeSelf)
                {
                    Assert.IsNull(nextExpansion);
                    nextExpansion = row;
                }
            }

            Assert.NotNull(nextExpansion);
            Assert.AreNotEqual(
                firstExpansionId,
                nextExpansion.Entry.researchId,
                "완료 후 같은 위치에 다음 개척 단계가 표시되어야 한다.");
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(serviceOwner);
            if (catalog != null)
            {
                Object.DestroyImmediate(catalog);
            }
        }
    }

    private static CityFlowServices CreateServicesWithExpansionResearch(
        GameObject serviceOwner,
        out ResearchCatalogSO catalog)
    {
        var services =
            new CityFlowServices(new SimEventHub(), null, null);
        var research =
            serviceOwner.AddComponent<ResearchUnlockService>();
        catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        var serialized = new UnityEditor.SerializedObject(catalog);
        var entries = serialized.FindProperty("entries");
        entries.arraySize = 12;

        ResearchCategory[] overviewCategories =
        {
            ResearchCategory.Commercial,
            ResearchCategory.Infrastructure,
            ResearchCategory.PublicService
        };
        for (int index = 0; index < overviewCategories.Length; index++)
        {
            var entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("researchId").stringValue =
                $"overview_{index}";
            entry.FindPropertyRelative("displayName").stringValue =
                $"Overview {index}";
            entry.FindPropertyRelative("category").enumValueIndex =
                (int)overviewCategories[index];
            entry.FindPropertyRelative("threshold").intValue = 0;
        }

        for (int index = 0; index < 9; index++)
        {
            var entry = entries.GetArrayElementAtIndex(index + 3);
            entry.FindPropertyRelative("researchId").stringValue =
                $"expansion_{index}";
            entry.FindPropertyRelative("displayName").stringValue =
                $"Expansion {index}";
            entry.FindPropertyRelative("category").enumValueIndex =
                (int)ResearchCategory.Expansion;
            entry.FindPropertyRelative("threshold").intValue = 0;
            entry.FindPropertyRelative("worldGridStageId").stringValue =
                $"stage_{index}";
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();

        SetPrivate(research, "catalog", catalog);
        research.inputsOverrideForTest = () =>
            new ResearchConditionInputs(0, 0, null);
        research.Initialize(services);
        return services;
    }

    private static CityFlowServices CreateServicesWithReadyResearch(
        GameObject serviceOwner,
        out ResearchCatalogSO catalog)
    {
        var services =
            new CityFlowServices(new SimEventHub(), null, null);
        var research =
            serviceOwner.AddComponent<ResearchUnlockService>();
        catalog = ScriptableObject.CreateInstance<ResearchCatalogSO>();
        var serialized = new UnityEditor.SerializedObject(catalog);
        var entries = serialized.FindProperty("entries");
        entries.InsertArrayElementAtIndex(0);
        var entry = entries.GetArrayElementAtIndex(0);
        entry.FindPropertyRelative("researchId").stringValue =
            "research_ready";
        entry.FindPropertyRelative("displayName").stringValue =
            "테스트 건물";
        entry.FindPropertyRelative("conditionKind").enumValueIndex =
            (int)ResearchConditionKind.DailyArrivals;
        entry.FindPropertyRelative("threshold").intValue = 1;
        entries.InsertArrayElementAtIndex(1);
        var childEntry = entries.GetArrayElementAtIndex(1);
        childEntry.FindPropertyRelative("researchId").stringValue =
            "research_child";
        childEntry.FindPropertyRelative("displayName").stringValue =
            "Child Research";
        childEntry.FindPropertyRelative("prerequisiteId").stringValue =
            "research_ready";
        entries.InsertArrayElementAtIndex(2);
        var infrastructureEntry = entries.GetArrayElementAtIndex(2);
        infrastructureEntry.FindPropertyRelative("researchId").stringValue =
            "research_infrastructure";
        infrastructureEntry.FindPropertyRelative("displayName").stringValue =
            "Infrastructure Research";
        infrastructureEntry.FindPropertyRelative("prerequisiteId").stringValue =
            string.Empty;
        infrastructureEntry.FindPropertyRelative("category").enumValueIndex =
            (int)ResearchCategory.Infrastructure;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        SetPrivate(research, "catalog", catalog);
        research.inputsOverrideForTest = () =>
            new ResearchConditionInputs(1, 0, null);
        research.Initialize(services);
        return services;
    }

    private static Button CreateButton(
        Transform parent,
        string name,
        string text)
    {
        var buttonObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        var labelObject = new GameObject(
            "Label",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
        TMP_Text label = labelObject.GetComponent<TMP_Text>();
        label.text = text;
        label.color = new Color(0.04f, 0.05f, 0.05f, 1f);
        return buttonObject.GetComponent<Button>();
    }

    private static void AssertReadable(Color color)
    {
        Assert.IsTrue(
            IsReadable(color),
            $"Expected a readable light text color, but was {color}.");
    }

    private static bool IsReadable(Color color) =>
        color.a > 0.9f &&
        Mathf.Max(color.r, color.g, color.b) > 0.75f;

    private static void SetPrivate(
        object target,
        string field,
        object value) =>
        target.GetType().GetField(
                field,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic)
            .SetValue(target, value);
}
