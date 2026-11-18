using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Oculus.Interaction;
using UnityEditor;
using UnityEngine;

namespace SignBridge.Diagnostics.Editor
{
    /// <summary>
    /// 씬에 있는 Meta Interaction SDK 컴포넌트 중 필수 참조([Interface] 필드인데 [Optional]이 아닌 것)가
    /// 비어 있는 것을 찾아 콘솔에 보여준다. 로그를 클릭하면 해당 오브젝트가 선택된다.
    /// </summary>
    public static class MissingInteractionReferenceFinder
    {
        private const BindingFlags Fields =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        [MenuItem("Tools/SignBridge/Diagnostics/빠진 Interaction SDK 참조 찾기")]
        public static void FindInScene()
        {
            var report = new StringBuilder();
            int count = 0;

            foreach (MonoBehaviour mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb == null) continue;
                System.Type type = mb.GetType();
                if (type.Namespace == null || !type.Namespace.StartsWith("Oculus.Interaction")) continue;

                foreach (string field in MissingRequiredFields(mb))
                {
                    count++;
                    string state = mb.isActiveAndEnabled ? "활성" : "비활성";
                    string line = $"{GetPath(mb.transform)}  [{type.Name}] {field}  ({state})";
                    report.AppendLine(line);
                    Debug.LogWarning($"[Diagnostics] 빠진 참조: {line}", mb);
                }
            }

            Debug.Log(count == 0
                ? "[Diagnostics] 빠진 Interaction SDK 필수 참조가 없습니다."
                : $"[Diagnostics] 빠진 Interaction SDK 필수 참조 {count}개\n{report}");
        }

        [MenuItem("Tools/SignBridge/Diagnostics/선택한 오브젝트 구조 출력")]
        public static void DumpSelection()
        {
            foreach (GameObject go in Selection.gameObjects)
            {
                var sb = new StringBuilder($"[Diagnostics] 구조: {GetPath(go.transform)}\n");
                Dump(go.transform, 0, sb);
                Debug.Log(sb.ToString(), go);
            }
        }

        [MenuItem("Tools/SignBridge/Diagnostics/선택한 오브젝트 구조 출력", true)]
        private static bool DumpSelectionValidate() => Selection.gameObjects.Length > 0;

        private static void Dump(Transform t, int depth, StringBuilder sb)
        {
            sb.Append(' ', depth * 2).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (off)").Append("  :");
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null) { sb.Append(" <Missing Script>"); continue; }
                if (c is Transform) continue;
                sb.Append(' ').Append(c.GetType().Name);
            }
            sb.AppendLine();
            foreach (Transform child in t) Dump(child, depth + 1, sb);
        }

        private static IEnumerable<string> MissingRequiredFields(MonoBehaviour mb)
        {
            for (System.Type t = mb.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(Fields))
                {
                    if (f.GetCustomAttribute<InterfaceAttribute>() == null) continue;
                    if (f.GetCustomAttribute<OptionalAttribute>() != null) continue;
                    if (!typeof(Object).IsAssignableFrom(f.FieldType)) continue;

                    if (f.GetValue(mb) as Object == null) yield return f.Name;
                }
            }
        }

        private static string GetPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (Transform p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }
    }
}
