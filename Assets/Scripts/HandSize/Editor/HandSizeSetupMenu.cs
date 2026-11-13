using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEditor;
using UnityEngine;

namespace SignBridge.HandSize.Editor
{
    public static class HandSizeSetupMenu
    {
        [MenuItem("Tools/SignBridge/Hand Size/씬 자동 설정 (Estimator + 모든 Grabbable)")]
        private static void SetupScene()
        {
            int added = 0;

            if (Object.FindFirstObjectByType<HandSizeEstimator>() == null)
            {
                var go = new GameObject("HandSizeEstimator");
                Undo.RegisterCreatedObjectUndo(go, "Add HandSizeEstimator");
                Undo.AddComponent<HandSizeEstimator>(go);
            }

            foreach (Grabbable g in Object.FindObjectsByType<Grabbable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (g.GetComponent<GraspSizeAdapter>() != null) continue;
                // 손으로 잡는 설정(Hand Grab)이 있는 오브젝트만 대상으로 한다. (테이블 등 제외)
                if (!HasHandGrab(g.gameObject)) continue;
                Undo.AddComponent<GraspSizeAdapter>(g.gameObject);
                added++;
            }

            Debug.Log($"[HandSize] GraspSizeAdapter {added}개 추가 (Hand Grab이 있는 Grabbable만).");
        }

        private static bool HasHandGrab(GameObject go) =>
            go.GetComponentInChildren<TouchHandGrabInteractable>(true) != null ||
            go.GetComponentInChildren<HandGrabInteractable>(true) != null ||
            go.GetComponentInChildren<DistanceHandGrabInteractable>(true) != null;

        [MenuItem("Tools/SignBridge/Hand Size/선택한 오브젝트에 GraspSizeAdapter 추가")]
        private static void AddToSelection()
        {
            foreach (GameObject go in Selection.gameObjects)
            {
                if (go.GetComponent<GraspSizeAdapter>() == null) Undo.AddComponent<GraspSizeAdapter>(go);
            }
        }

        [MenuItem("Tools/SignBridge/Hand Size/선택한 오브젝트에 GraspSizeAdapter 추가", true)]
        private static bool AddToSelectionValidate() => Selection.gameObjects.Length > 0;
    }
}
