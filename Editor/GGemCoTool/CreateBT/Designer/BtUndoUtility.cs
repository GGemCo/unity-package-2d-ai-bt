#if UNITY_EDITOR
using System;
using GGemCo2DAiBt;
using UnityEditor;

namespace GGemCo2DAiBtEditor
{
    /// <summary>
    /// BT 에셋 편집용 Undo 공통 유틸리티.
    /// - 구조 변경은 전체 스냅샷 기반 Undo를 사용한다.
    /// - 단일 값 변경은 delta 기반 RecordObject를 사용한다.
    /// </summary>
    internal static class BtUndoUtility
    {
        public static bool HasAsset(MonsterBehaviorTreeAsset asset)
        {
            return asset != null;
        }

        public static bool RecordDelta(MonsterBehaviorTreeAsset asset, string actionName)
        {
            if (asset == null)
                return false;

            Undo.RecordObject(asset, actionName);
            return true;
        }

        public static bool RecordComplete(MonsterBehaviorTreeAsset asset, string actionName)
        {
            if (asset == null)
                return false;

            Undo.RegisterCompleteObjectUndo(asset, actionName);
            return true;
        }

        public static bool RecordObjects(UnityEngine.Object[] objects, string actionName)
        {
            if (objects == null || objects.Length == 0)
                return false;

            Undo.RecordObjects(objects, actionName);
            return true;
        }

        public static void SetDirty(MonsterBehaviorTreeAsset asset)
        {
            if (asset == null)
                return;

            EditorUtility.SetDirty(asset);
        }

        public static void MarkChanged(MonsterBehaviorTreeAsset asset, Action onChanged)
        {
            SetDirty(asset);
            onChanged?.Invoke();
        }
    }
}
#endif
