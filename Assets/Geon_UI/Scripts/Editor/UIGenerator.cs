using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CityFlow.UI.Geon;

namespace CityFlow.UI.Geon.Editor
{
    public static class UIGenerator
    {
        [MenuItem("Geon Tools/🚀 UI 프로토타입 생성하기")]
        public static void GenerateUI()
        {
            // 1. Canvas 생성 (모든 UI의 근간)
            GameObject canvasObj = new GameObject("UI_MainCanvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();

            // 2. HUD_TopBar 생성 (상단 대시보드)
            GameObject topBarObj = CreatePanel("HUD_TopBar", canvasObj.transform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1));
            topBarObj.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 60); // 가로 꽉 채우고, 높이 60
            topBarObj.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
            topBarObj.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.8f);

            // HUDDashboard 스크립트 부착
            HUDDashboard hudScript = topBarObj.AddComponent<HUDDashboard>();

            // 텍스트 및 게이지바 생성 (위치나 디자인은 유저님이 나중에 피그마 보고 수정 가능)
            TextMeshProUGUI timeText = CreateText("TimeText", topBarObj.transform);
            TextMeshProUGUI vehicleCountText = CreateText("VehicleCountText", topBarObj.transform);
            TextMeshProUGUI coinText = CreateText("CoinText", topBarObj.transform);
            TextMeshProUGUI efficiencyText = CreateText("EfficiencyText", topBarObj.transform);
            
            // Slider (혼잡도 바) 뼈대
            GameObject sliderObj = new GameObject("CongestionBar");
            sliderObj.transform.SetParent(topBarObj.transform, false);
            Slider congestionBar = sliderObj.AddComponent<Slider>();
            
            // FlowBurst 이펙트 (빈 이미지)
            GameObject burstEffectObj = new GameObject("FlowBurstEffect");
            burstEffectObj.transform.SetParent(topBarObj.transform, false);
            burstEffectObj.AddComponent<Image>();
            burstEffectObj.SetActive(false);

            // [마법의 핵심] 스크립트 구멍에 자동 바인딩 (드래그 앤 드롭 자동화)
            SerializedObject serializedObject = new SerializedObject(hudScript);
            serializedObject.FindProperty("timeText").objectReferenceValue = timeText;
            serializedObject.FindProperty("vehicleCountText").objectReferenceValue = vehicleCountText;
            serializedObject.FindProperty("coinText").objectReferenceValue = coinText;
            serializedObject.FindProperty("efficiencyText").objectReferenceValue = efficiencyText;
            serializedObject.FindProperty("congestionBar").objectReferenceValue = congestionBar;
            serializedObject.FindProperty("flowBurstEffect").objectReferenceValue = burstEffectObj;
            serializedObject.ApplyModifiedProperties();

            // 3. Dock_Right 생성 (우하단 네비게이션 도크)
            GameObject dockRightObj = CreatePanel("Dock_Right", canvasObj.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0));
            dockRightObj.GetComponent<RectTransform>().sizeDelta = new Vector2(60, 240); // 얇고 긴 바
            dockRightObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(-20, 20); // 구석에서 살짝 띄움
            
            VerticalLayoutGroup layoutGroup = dockRightObj.AddComponent<VerticalLayoutGroup>();
            layoutGroup.childAlignment = TextAnchor.UpperCenter;
            layoutGroup.childControlHeight = true;
            layoutGroup.childControlWidth = true;

            // 4. SubPanels_Right 생성 (세부 메뉴 패널 컨테이너)
            GameObject subPanelsObj = new GameObject("SubPanels_Right");
            subPanelsObj.transform.SetParent(canvasObj.transform, false);
            RectTransform subRect = subPanelsObj.AddComponent<RectTransform>();
            subRect.anchorMin = new Vector2(1, 0); // Bottom Right
            subRect.anchorMax = new Vector2(1, 0);
            subRect.pivot = new Vector2(1, 0);
            subRect.anchoredPosition = new Vector2(-100, 20); // 도크 왼쪽에 띄움
            subRect.sizeDelta = new Vector2(300, 400);

            // 5. AnalysisCard_BottomLeft 생성 (좌하단 상세 분석창 - 450x290 고정 크기 규약 준수)
            GameObject analysisCardObj = CreatePanel("AnalysisCard_BottomLeft", canvasObj.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0));
            analysisCardObj.GetComponent<RectTransform>().sizeDelta = new Vector2(450, 290);
            analysisCardObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(20, 20);
            analysisCardObj.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 0.9f);

            // 작업 완료 후 캔버스 포커스
            Selection.activeGameObject = canvasObj;
            Debug.Log("🚀 [성공] UI 프로토타입 뼈대가 씬에 성공적으로 생성 및 연결되었습니다!");
        }

        private static GameObject CreatePanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            obj.AddComponent<Image>().color = new Color(0.5f, 0.5f, 0.5f, 0.5f); // 식별용 회색 반투명
            return obj;
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            TextMeshProUGUI txt = obj.AddComponent<TextMeshProUGUI>();
            txt.text = name;
            txt.color = Color.white;
            txt.fontSize = 24;
            return txt;
        }
    }
}
