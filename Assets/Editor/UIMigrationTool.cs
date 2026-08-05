using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class UIMigrationTool : EditorWindow
{
    private GameObject sourceUI;
    private GameObject targetUI;

    [MenuItem("CityFlow/UI/Migrate UI Components")]
    public static void ShowWindow()
    {
        GetWindow<UIMigrationTool>("UI 마이그레이션");
    }

    void OnGUI()
    {
        GUILayout.Label("UI 컴포넌트 이식 도구", EditorStyles.boldLabel);
        sourceUI = (GameObject)EditorGUILayout.ObjectField("Source UI (기존 씬 UI)", sourceUI, typeof(GameObject), true);
        targetUI = (GameObject)EditorGUILayout.ObjectField("Target UI (새로운 Polished UI)", targetUI, typeof(GameObject), true);

        if (GUILayout.Button("마이그레이션 실행"))
        {
            if (sourceUI != null && targetUI != null)
            {
                Migrate(sourceUI, targetUI);
            }
            else
            {
                Debug.LogError("Source와 Target을 모두 지정해주세요.");
            }
        }
    }

    private void Migrate(GameObject srcRoot, GameObject dstRoot)
    {
        Transform[] srcTransforms = srcRoot.GetComponentsInChildren<Transform>(true);
        int copiedCount = 0;
        List<Component> copiedComponents = new List<Component>();

        // 1. Copy MonoBehaviours (only CityFlow ones)
        foreach (Transform srcT in srcTransforms)
        {
            MonoBehaviour[] mbps = srcT.GetComponents<MonoBehaviour>();
            foreach (var mb in mbps)
            {
                if (mb == null) continue;
                string ns = mb.GetType().Namespace;
                
                // Copy CityFlow scripts
                if (ns != null && ns.StartsWith("CityFlow"))
                {
                    Transform dstT = FindEquivalentObject(srcT, srcRoot.transform, dstRoot.transform);
                    if (dstT != null)
                    {
                        Component existing = dstT.GetComponent(mb.GetType());
                        if (existing != null) 
                        {
                            ComponentUtility.CopyComponent(mb);
                            ComponentUtility.PasteComponentValues(existing);
                            copiedComponents.Add(existing);
                        }
                        else
                        {
                            ComponentUtility.CopyComponent(mb);
                            ComponentUtility.PasteComponentAsNew(dstT.gameObject);
                            copiedComponents.Add(dstT.GetComponent(mb.GetType()));
                        }
                        copiedCount++;
                    }
                    else
                    {
                        Debug.LogWarning($"[Migration] '{srcT.name}'를 타겟 UI에서 찾을 수 없어 {mb.GetType().Name} 복사 실패.");
                    }
                }
            }
        }

        Debug.Log($"[Migration] 총 {copiedCount}개의 CityFlow 스크립트 복사 완료.");

        // 2. Remap References in Copied Components
        int remapCount = 0;
        foreach (Component c in copiedComponents)
        {
            if (c == null) continue;
            SerializedObject so = new SerializedObject(c);
            SerializedProperty sp = so.GetIterator();
            bool enterChildren = true;
            bool modified = false;

            while (sp.NextVisible(enterChildren))
            {
                enterChildren = true; // ALWAYS enter children to catch arrays, UnityEvents, etc!
                if (sp.propertyType == SerializedPropertyType.ObjectReference && sp.objectReferenceValue != null)
                {
                    Object refObj = sp.objectReferenceValue;
                    
                    if (refObj is Component refComp)
                    {
                        if (IsChildOf(refComp.transform, srcRoot.transform))
                        {
                            Transform dstT = FindEquivalentObject(refComp.transform, srcRoot.transform, dstRoot.transform);
                            if (dstT != null)
                            {
                                Component dstComp = dstT.GetComponent(refComp.GetType());
                                if (dstComp != null)
                                {
                                    sp.objectReferenceValue = dstComp;
                                    modified = true;
                                    remapCount++;
                                }
                            }
                        }
                    }
                    else if (refObj is GameObject refGo)
                    {
                        if (IsChildOf(refGo.transform, srcRoot.transform))
                        {
                            Transform dstT = FindEquivalentObject(refGo.transform, srcRoot.transform, dstRoot.transform);
                            if (dstT != null)
                            {
                                sp.objectReferenceValue = dstT.gameObject;
                                modified = true;
                                remapCount++;
                            }
                        }
                    }
                }
            }
            if (modified)
            {
                so.ApplyModifiedProperties();
            }
        }

        Debug.Log($"[Migration] 총 {remapCount}개의 내부 참조 리매핑 완료.");
        Debug.Log("[Migration] 마이그레이션이 성공적으로 완료되었습니다!");
    }

    private Transform FindEquivalentObject(Transform oldObj, Transform oldRoot, Transform newRoot)
    {
        Transform[] allInNew = newRoot.GetComponentsInChildren<Transform>(true);
        List<Transform> nameMatches = new List<Transform>();
        foreach(var t in allInNew) 
        {
            if (t.name == oldObj.name) nameMatches.Add(t);
        }

        if (nameMatches.Count == 0) return null;
        if (nameMatches.Count == 1) return nameMatches[0];

        // 2. If multiple, try matching the parent's name
        if (oldObj.parent != null)
        {
            foreach(var t in nameMatches)
            {
                if (t.parent != null && t.parent.name == oldObj.parent.name) return t;
            }
        }

        return nameMatches[0];
    }

    private bool IsChildOf(Transform child, Transform parent)
    {
        Transform curr = child;
        while (curr != null)
        {
            if (curr == parent) return true;
            curr = curr.parent;
        }
        return false;
    }
}
