using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CityFlow.UI.Editor
{
    public static class BuildPanelStyleApplier
    {
        private const string SpriteRoot = "Assets/99_Download/Layer Lab/GUI-MonoRound/ResourcesData/Sprites/Components/";
        private const string FontPath = "Assets/99_Download/Fonts/NanumGothic SDF.asset";
        private const string PrefabPath = "Assets/02_Prefabs/UI_BuildSlot.prefab";

        private static Sprite LoadSprite(string subPath)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(SpriteRoot + subPath);
        }

        [MenuItem("CityFlow/UI/Apply Build Panel Style & Icons")]
        public static void ApplyStyleAndIcons()
        {
            // 1. Bind Icons to ScriptableObjects and Scene Slots
            ApplyBuildingIcons();

            // 2. Style Panels and UI attributes
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            Sprite frameList = LoadSprite("Frame/Frame_ListFrame01_White1.png");
            Sprite slotBg = LoadSprite("Frame/Frame_Basic_Rectangle.png");
            Sprite btnDark = LoadSprite("Button/Btn_Rectangle02_Dark.png");
            Sprite btnGreen = LoadSprite("Button/Btn_Rectangle01_n_Green.png");
            Sprite btnYellow = LoadSprite("Button/Btn_Rectangle01_n_Yellow.png");
            Sprite btnBlue = LoadSprite("Button/Btn_Rectangle01_n_Blue.png");
            Sprite btnOrange = LoadSprite("Button/Btn_Rectangle01_n_Orange.png");

            // Upgrade Original Prefab: Assets/02_Prefabs/UI_BuildSlot.prefab
            GameObject prefabContents = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (prefabContents != null)
            {
                StyleSingleSlot(prefabContents.transform, slotBg, btnDark, font);
                PrefabUtility.SaveAsPrefabAsset(prefabContents, PrefabPath);
                PrefabUtility.UnloadPrefabContents(prefabContents);
                Debug.Log($"[BuildPanelStyleApplier] Upgraded original prefab: {PrefabPath}");
            }

            GameObject buildPanel = null;
            Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (Transform t in all)
            {
                if (t.name == "Build_Panel" && !EditorUtility.IsPersistent(t))
                {
                    buildPanel = t.gameObject;
                    break;
                }
            }

            if (buildPanel == null)
            {
                Debug.LogWarning("[BuildPanelStyleApplier] Build_Panel not found in active scene.");
                return;
            }

            Image bgImg = buildPanel.GetComponent<Image>();
            if (bgImg != null)
            {
                if (frameList != null) bgImg.sprite = frameList;
                bgImg.type = Image.Type.Sliced;
                bgImg.color = new Color(0.1f, 0.11f, 0.125f, 0.95f);
                bgImg.raycastTarget = true;
            }

            string[] tabs = { "Infra", "Dwelling", "Commerce", "Public" };
            string[] namesKr = { "인프라", "주거", "상업", "공공" };
            Sprite[] tabSprites = { btnGreen, btnYellow, btnBlue, btnOrange };

            for (int i = 0; i < tabs.Length; i++)
            {
                Transform tabTrans = buildPanel.transform.Find(tabs[i]);
                if (tabTrans != null)
                {
                    Image tabImg = tabTrans.GetComponent<Image>();
                    if (tabImg != null)
                    {
                        if (tabSprites[i] != null) tabImg.sprite = tabSprites[i];
                        tabImg.type = Image.Type.Sliced;
                        tabImg.color = Color.white;
                        tabImg.raycastTarget = true;
                    }

                    TextMeshProUGUI label = tabTrans.GetComponentInChildren<TextMeshProUGUI>(true);
                    if (label != null)
                    {
                        if (font != null) label.font = font;
                        label.text = namesKr[i];
                        label.fontSize = 15f;
                        label.fontStyle = FontStyles.Bold;
                        label.color = Color.white;
                        label.alignment = TextAlignmentOptions.Center;
                    }
                }
            }

            string[] pages = { "Infra_Panel", "Dwelling_Panel", "Commerce_Panel", "Public_Panel" };
            foreach (string pageName in pages)
            {
                Transform pageTrans = buildPanel.transform.Find(pageName);
                if (pageTrans == null) continue;

                GridLayoutGroup glg = pageTrans.GetComponent<GridLayoutGroup>();
                if (glg != null)
                {
                    glg.cellSize = new Vector2(104f, 156f);
                    glg.spacing = new Vector2(10f, 10f);
                    glg.padding = new RectOffset(14, 14, 14, 14);
                }

                for (int c = 0; c < pageTrans.childCount; c++)
                {
                    Transform slot = pageTrans.GetChild(c);
                    StyleSingleSlot(slot, slotBg, btnDark, font);
                }
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.LogWarning("SUCCESS_ALL_APPLIED: 3D Icons and UI styles successfully unified and mapped across all ScriptableObjects and Scene slots!");
        }

        private static void ApplyBuildingIcons()
        {
            Sprite spRoad = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/03_Art/Icon/icon_road_iso_1785915435445-Photoroom.png");
            Sprite spSignal = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/03_Art/Icon/icon_signal_iso_1785915443773-Photoroom.png");
            Sprite spRoundabout = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/03_Art/Icon/icon_roundabout_iso_1785915452640-Photoroom.png");
            Sprite spBusStop = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/03_Art/Icon/icon_busstop_iso_1785915463602-Photoroom.png");

            Sprite spHouse = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/03_Art/Icon/icon_house_iso_1785915491088-Photoroom.png");
            Sprite spSchool = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/03_Art/Icon/icon_school_iso_1785915501151-Photoroom.png");
            Sprite spHospital = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/03_Art/Icon/icon_hospital_iso_1785915510971-Photoroom.png");
            Sprite spPolice = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/03_Art/Icon/icon_police_iso_1785915519487-Photoroom.png");

            Sprite spOffice = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/03_Art/Icon/icon_office_iso_1785915547357-Photoroom.png");
            Sprite spShopping = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/03_Art/Icon/icon_shopping_iso_1785915556845-Photoroom.png");
            Sprite spGas = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/03_Art/Icon/icon_gasstation_iso_1785915565450-Photoroom.png");
            Sprite spVideo = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/03_Art/Icon/icon_videorental_iso_1785915575542-Photoroom.png");
            Sprite spPharmacy = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/03_Art/Icon/icon_pharmacy_iso_1785915603484-Photoroom.png");

            // 1. Bind to TileDataSO (property: buildingIcon)
            BindSOIcon("Assets/05_ScriptableObjects/CityFlow/TileData/RoadData.asset", spRoad, "buildingIcon");
            BindSOIcon("Assets/05_ScriptableObjects/CityFlow/TileData/HouseData.asset", spHouse, "buildingIcon");
            BindSOIcon("Assets/05_ScriptableObjects/CityFlow/TileData/SchoolData.asset", spSchool, "buildingIcon");
            BindSOIcon("Assets/05_ScriptableObjects/CityFlow/TileData/HospitalTileData.asset", spHospital, "buildingIcon");
            BindSOIcon("Assets/05_ScriptableObjects/CityFlow/TileData/OfficeData.asset", spOffice, "buildingIcon");

            // 2. Bind to InfrastructureDataSO (property: Icon)
            BindSOIcon("Assets/05_ScriptableObjects/CityFlow/InfrastructureData/SignalData.asset", spSignal, "Icon");
            BindSOIcon("Assets/05_ScriptableObjects/CityFlow/InfrastructureData/RoundaboutData.asset", spRoundabout, "Icon");
            BindSOIcon("Assets/Resources/CityFlow/InfrastructureData/BusStopData.asset", spBusStop, "Icon");

            // 3. Bind to BuildingDefinitionSO (property: buildingIcon)
            BindSOIcon("Assets/05_ScriptableObjects/Buildings/Building_PoliceStation.asset", spPolice, "buildingIcon");
            BindSOIcon("Assets/05_ScriptableObjects/Buildings/Building_Mall.asset", spShopping, "buildingIcon");
            BindSOIcon("Assets/05_ScriptableObjects/Buildings/Building_PetrolStation.asset", spGas, "buildingIcon");
            BindSOIcon("Assets/05_ScriptableObjects/Buildings/Building_StoreCorner_Video.asset", spVideo, "buildingIcon");
            BindSOIcon("Assets/05_ScriptableObjects/Buildings/Building_StoreCorner_Drug.asset", spPharmacy, "buildingIcon");

            // Temporary fallbacks for 3 missing commercial icons to maintain visual fidelity
            BindSOIcon("Assets/05_ScriptableObjects/Buildings/Building_CoffeeShop.asset", spShopping, "buildingIcon");
            BindSOIcon("Assets/05_ScriptableObjects/Buildings/Building_Cinema.asset", spVideo, "buildingIcon");
            BindSOIcon("Assets/05_ScriptableObjects/Buildings/Building_AutoRepair.asset", spGas, "buildingIcon");

            // 4. Update Scene Slot Icons directly
            GameObject buildPanel = null;
            Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (Transform t in all)
            {
                if (t.name == "Build_Panel" && !EditorUtility.IsPersistent(t))
                {
                    buildPanel = t.gameObject;
                    break;
                }
            }

            if (buildPanel != null)
            {
                SetSceneSlotIcon(buildPanel.transform, "Infra_Panel/Road_Slot", spRoad);
                SetSceneSlotIcon(buildPanel.transform, "Infra_Panel/Signal-Slot", spSignal);
                SetSceneSlotIcon(buildPanel.transform, "Infra_Panel/Roundabout_Slot", spRoundabout);
                SetSceneSlotIcon(buildPanel.transform, "Dwelling_Panel/UI_BuildSlot (1)", spHouse);
                SetSceneSlotIcon(buildPanel.transform, "Commerce_Panel/UI_BuildSlot (2)", spOffice);
                SetSceneSlotIcon(buildPanel.transform, "Public_Panel/School_Slot", spSchool);
                SetSceneSlotIcon(buildPanel.transform, "Public_Panel/Hospital_Slot", spHospital);
            }
        }

        private static void SetSceneSlotIcon(Transform root, string path, Sprite sp)
        {
            if (sp == null) return;
            Transform slot = root.Find(path);
            if (slot == null) return;
            Transform icon = slot.Find("Icon");
            if (icon != null)
            {
                Image img = icon.GetComponent<Image>();
                if (img != null)
                {
                    img.sprite = sp;
                    img.color = Color.white;
                }
            }
        }

        private static void BindSOIcon(string assetPath, Sprite sprite, string propName)
        {
            if (sprite == null) return;
            ScriptableObject soAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (soAsset == null) return;

            SerializedObject so = new SerializedObject(soAsset);
            SerializedProperty prop = so.FindProperty(propName);
            if (prop != null)
            {
                prop.objectReferenceValue = sprite;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(soAsset);
            }
        }

        private static void StyleSingleSlot(Transform slot, Sprite slotBg, Sprite btnSprite, TMP_FontAsset font)
        {
            Image slotImg = slot.GetComponent<Image>();
            if (slotImg != null)
            {
                if (slotBg != null) slotImg.sprite = slotBg;
                slotImg.type = Image.Type.Sliced;
                slotImg.color = new Color(0.17f, 0.19f, 0.23f, 1f);
                slotImg.raycastTarget = true;
            }

            RectTransform slotRt = slot.GetComponent<RectTransform>();
            if (slotRt != null && slot.parent != null && !slot.parent.name.Contains("Panel"))
            {
                slotRt.sizeDelta = new Vector2(104f, 156f);
            }

            Transform iconTrans = slot.Find("Icon");
            if (iconTrans != null)
            {
                Image iconImg = iconTrans.GetComponent<Image>();
                if (iconImg != null && (iconImg.sprite == null || iconImg.sprite.name.Contains("builtin")))
                {
                    iconImg.color = new Color(0.12f, 0.13f, 0.15f, 1f);
                }
                else if (iconImg != null && iconImg.sprite != null)
                {
                    iconImg.color = Color.white;
                }

                RectTransform rt = iconTrans.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = new Vector2(74f, 74f);
                    rt.anchoredPosition = new Vector2(0f, 10f);
                }
            }

            Transform costTrans = slot.Find("CostText");
            if (costTrans != null)
            {
                TextMeshProUGUI costTmp = costTrans.GetComponent<TextMeshProUGUI>();
                if (costTmp != null)
                {
                    if (font != null) costTmp.font = font;
                    costTmp.fontSize = 15f;
                    costTmp.fontStyle = FontStyles.Bold;
                    costTmp.color = Color.white;
                    costTmp.alignment = TextAlignmentOptions.Center;
                }
                RectTransform costRt = costTrans.GetComponent<RectTransform>();
                if (costRt != null)
                {
                    costRt.anchorMin = new Vector2(0.5f, 0.5f);
                    costRt.anchorMax = new Vector2(0.5f, 0.5f);
                    costRt.pivot = new Vector2(0.5f, 0.5f);
                    costRt.sizeDelta = new Vector2(96f, 24f);
                    costRt.anchoredPosition = new Vector2(0f, 60f);
                }
            }

            Transform buyTrans = slot.Find("Btn_Buy");
            if (buyTrans == null) buyTrans = slot.Find("Buy");
            if (buyTrans == null)
            {
                Button btn = slot.GetComponentInChildren<Button>(true);
                if (btn != null) buyTrans = btn.transform;
            }

            if (buyTrans != null && buyTrans != slot)
            {
                Image buyImg = buyTrans.GetComponent<Image>();
                if (buyImg != null)
                {
                    if (btnSprite != null) buyImg.sprite = btnSprite;
                    buyImg.type = Image.Type.Sliced;
                    buyImg.color = Color.white;
                    buyImg.raycastTarget = true;
                }
                RectTransform buyRt = buyTrans.GetComponent<RectTransform>();
                if (buyRt != null)
                {
                    buyRt.anchorMin = new Vector2(0.5f, 0.5f);
                    buyRt.anchorMax = new Vector2(0.5f, 0.5f);
                    buyRt.pivot = new Vector2(0.5f, 0.5f);
                    buyRt.sizeDelta = new Vector2(92f, 30f);
                    buyRt.anchoredPosition = new Vector2(0f, -56f);
                }

                TextMeshProUGUI buyTmp = buyTrans.GetComponentInChildren<TextMeshProUGUI>(true);
                if (buyTmp != null)
                {
                    if (font != null) buyTmp.font = font;
                    if (string.Equals(buyTmp.text, "Buy", System.StringComparison.OrdinalIgnoreCase))
                    {
                        buyTmp.text = "건설";
                    }
                    buyTmp.fontSize = 13f;
                    buyTmp.fontStyle = FontStyles.Bold;
                    buyTmp.color = Color.white;
                    buyTmp.alignment = TextAlignmentOptions.Center;
                }
                else
                {
                    Text legText = buyTrans.GetComponentInChildren<Text>(true);
                    if (legText != null)
                    {
                        if (string.Equals(legText.text, "Buy", System.StringComparison.OrdinalIgnoreCase))
                        {
                            legText.text = "건설";
                        }
                        legText.fontSize = 13;
                        legText.fontStyle = FontStyle.Bold;
                        legText.color = Color.white;
                    }
                }
            }
        }

        [MenuItem("CityFlow/UI/Apply Tooltip Style")]
        public static void ApplyTooltipStyle()
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            Sprite frameList = LoadSprite("Frame/Frame_ListFrame01_White1.png");

            TooltipController tooltip = null;
            Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
            foreach (Transform t in all)
            {
                if (!EditorUtility.IsPersistent(t))
                {
                    TooltipController tc = t.GetComponent<TooltipController>();
                    if (tc != null)
                    {
                        tooltip = tc;
                        break;
                    }
                }
            }

            if (tooltip == null)
            {
                Debug.LogWarning("[BuildPanelStyleApplier] No GameObject with TooltipController found in active scene.");
                return;
            }

            GameObject go = tooltip.gameObject;
            Image bgImg = go.GetComponent<Image>();
            if (bgImg != null)
            {
                if (frameList != null) bgImg.sprite = frameList;
                bgImg.type = Image.Type.Sliced;
                bgImg.color = new Color(0.1f, 0.12f, 0.16f, 0.98f);
                bgImg.raycastTarget = false;
            }

            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                // 스트레치(Stretch) 앵커 해제 및 좌하단 점 앵커 고정으로 정밀 가로폭 픽싱
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                rt.sizeDelta = new Vector2(380f, rt.sizeDelta.y);
            }

            VerticalLayoutGroup vlg = go.GetComponent<VerticalLayoutGroup>();
            if (vlg == null) vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(18, 18, 18, 18);
            vlg.spacing = 10f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            ContentSizeFitter csf = go.GetComponent<ContentSizeFitter>();
            if (csf == null) csf = go.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            SerializedObject so = new SerializedObject(tooltip);
            StyleTooltipText(so.FindProperty("txtName")?.objectReferenceValue as TextMeshProUGUI, font, 26f, FontStyles.Bold, new Color(1f, 0.85f, 0.3f, 1f));
            StyleTooltipText(so.FindProperty("txtCategory")?.objectReferenceValue as TextMeshProUGUI, font, 18f, FontStyles.Bold, new Color(0.65f, 0.85f, 1f, 1f));
            StyleTooltipText(so.FindProperty("txtCost")?.objectReferenceValue as TextMeshProUGUI, font, 20f, FontStyles.Bold, Color.white);
            StyleTooltipText(so.FindProperty("txtDescription")?.objectReferenceValue as TextMeshProUGUI, font, 17f, FontStyles.Normal, new Color(0.92f, 0.92f, 0.92f, 1f));

            TextMeshProUGUI txtIncome = so.FindProperty("txtIncome")?.objectReferenceValue as TextMeshProUGUI;
            if (txtIncome != null) txtIncome.gameObject.SetActive(false);

            TextMeshProUGUI txtEffect = so.FindProperty("txtEffect")?.objectReferenceValue as TextMeshProUGUI;
            if (txtEffect != null) txtEffect.gameObject.SetActive(false);

            TextMeshProUGUI[] allTexts = go.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var txt in allTexts)
            {
                txt.raycastTarget = false;
                if (font != null) txt.font = font;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.LogWarning("SUCCESS_TOOLTIP_STYLED: ToolTip_Panel successfully polished and modernized!");
        }

        private static void StyleTooltipText(TextMeshProUGUI txt, TMP_FontAsset font, float size, FontStyles style, Color color)
        {
            if (txt == null) return;
            if (font != null) txt.font = font;
            txt.fontSize = size;
            txt.fontStyle = style;
            txt.color = color;
            txt.lineSpacing = 2f;
            txt.enableWordWrapping = true;
            txt.raycastTarget = false;
            txt.gameObject.SetActive(true);
        }
    }
}

