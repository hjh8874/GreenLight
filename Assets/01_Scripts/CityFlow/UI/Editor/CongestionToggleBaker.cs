using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using CityFlow.UI;

namespace CityFlow.UI.Editor
{
    public static class CongestionToggleBaker
    {
        private const string PrefabPath = "Assets/03_Prefabs/UI/UI_CongestionToggle.prefab";

        [MenuItem("CityFlow/Bake UI/UI_CongestionToggle")]
        public static void Bake()
        {
            GameObject root = new GameObject("UI_CongestionToggle", typeof(RectTransform));
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(200f, 40f);
            
            Toggle toggle = root.AddComponent<Toggle>();
            
            GameObject bg = new GameObject("Background", typeof(RectTransform));
            bg.transform.SetParent(root.transform, false);
            Image bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            toggle.targetGraphic = bgImage;
            
            GameObject checkmark = new GameObject("Checkmark", typeof(RectTransform));
            checkmark.transform.SetParent(bg.transform, false);
            Image checkImage = checkmark.AddComponent<Image>();
            checkImage.color = Color.green;
            toggle.graphic = checkImage;
            
            GameObject label = new GameObject("Label", typeof(RectTransform));
            label.transform.SetParent(root.transform, false);
            Text text = label.AddComponent<Text>();
            text.text = "Traffic View";
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            
            CongestionTogglePanelController controller = root.AddComponent<CongestionTogglePanelController>();
            controller.Configure(toggle);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            GameObject.DestroyImmediate(root);

            Debug.Log($"[CongestionToggleBaker] Successfully baked prefab at {PrefabPath}");
        }
    }
}
