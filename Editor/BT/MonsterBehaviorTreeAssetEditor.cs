#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GGemCo2DAiBt.Editor
{
    /// <summary>
    /// MonsterBehaviorTreeAsset 기본 인스펙터 확장.
    /// GraphView 기반 편집기는 추후 확장하고, MVP에서는
    /// - 검증
    /// - 예시 트리 생성
    /// - 노드/블랙보드 목록 확인
    /// 을 제공한다.
    /// </summary>
    [CustomEditor(typeof(MonsterBehaviorTreeAsset))]
    public sealed class MonsterBehaviorTreeAssetEditor : UnityEditor.Editor
    {
        private const float Space = 4f;

        public override void OnInspectorGUI()
        {
            var asset = (MonsterBehaviorTreeAsset)target;
            if (asset == null) return;

            EditorGUILayout.LabelField("Monster Behavior Tree", EditorStyles.boldLabel);
            EditorGUILayout.Space(Space);

            DrawDefaultInspector();

            EditorGUILayout.Space(10f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate", GUILayout.Height(24)))
                {
                    var issues = MonsterBtValidator.Validate(asset);
                    MonsterBtValidator.LogIssues(asset, issues);
                }

                if (GUILayout.Button("Create Example BT", GUILayout.Height(24)))
                {
                    Undo.RecordObject(asset, "Create Example BT");
                    MonsterBtPresetBuilder.BuildMeleeExample(asset);
                    EditorUtility.SetDirty(asset);
                }
            }

            EditorGUILayout.Space(6f);
            DrawSummary(asset);
        }

        private static void DrawSummary(MonsterBehaviorTreeAsset asset)
        {
            int nodeCount = asset.nodes?.Count ?? 0;
            int keyCount = asset.blackboardSchema?.keys?.Count ?? 0;
            EditorGUILayout.LabelField($"Nodes: {nodeCount}");
            EditorGUILayout.LabelField($"Blackboard Keys: {keyCount}");
            EditorGUILayout.LabelField($"Root: {(string.IsNullOrEmpty(asset.rootNodeId) ? "(none)" : asset.rootNodeId)}");
        }
    }
}
#endif
