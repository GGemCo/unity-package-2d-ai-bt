#if UNITY_EDITOR
using GGemCo2DAiBt;
using UnityEditor;
using UnityEngine;

namespace GGemCo2DAiBtEditor
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
    public sealed class MonsterBehaviorTreeAssetEditor : Editor
    {
        private const float Space = 4f;
        private const float ButtonHeight = 24f;
        private int _skillPresetUid;

        /// <summary>
        /// MonsterBehaviorTreeAsset 인스펙터 UI를 렌더링하고
        /// 편집/검증/예시 트리 생성 기능을 제공한다.
        /// </summary>
        public override void OnInspectorGUI()
        {
            var asset = (MonsterBehaviorTreeAsset)target;
            if (asset == null) return;

            EditorGUILayout.LabelField("Monster Behavior Tree", EditorStyles.boldLabel);
            EditorGUILayout.Space(Space);

            DrawDefaultInspector();

            EditorGUILayout.Space(10f);
            DrawActionButtons(asset);

            EditorGUILayout.Space(6f);
            DrawSummary(asset);
        }

        /// <summary>
        /// 인스펙터 하단의 액션 버튼 영역을 렌더링한다.
        /// </summary>
        /// <param name="asset">현재 선택된 BT 에셋</param>
        private void DrawActionButtons(MonsterBehaviorTreeAsset asset)
        {
            if (asset == null)
            {
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("편집하기", GUILayout.Height(ButtonHeight)))
                {
                    OpenBtEditor(asset);
                }

                if (GUILayout.Button("Validate", GUILayout.Height(ButtonHeight)))
                {
                    ValidateTree(asset);
                }

                if (GUILayout.Button("Create Melee BT", GUILayout.Height(ButtonHeight)))
                {
                    CreateExampleTree(asset);
                }
            }

            EditorGUILayout.Space(Space);
            _skillPresetUid = Mathf.Max(0, EditorGUILayout.IntField("Monster Skill UID", _skillPresetUid));
            if (GUILayout.Button("Create Skill CastRange BT", GUILayout.Height(ButtonHeight)))
            {
                CreateSkillExampleTree(asset, _skillPresetUid);
            }
        }

        /// <summary>
        /// BT 생성/테스트 툴 창을 현재 에셋과 함께 연다.
        /// </summary>
        /// <param name="asset">편집 대상으로 전달할 BT 에셋</param>
        private static void OpenBtEditor(MonsterBehaviorTreeAsset asset)
        {
            if (asset == null)
            {
                return;
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            CreateBtWindow.Open(asset);

            // IMGUI 이벤트 중 창 전환이 발생하므로 레이아웃 예외를 방지하기 위해 즉시 종료한다.
            GUIUtility.ExitGUI();
        }

        /// <summary>
        /// 현재 BT 에셋의 구조 유효성을 검사하고 결과를 Console에 출력한다.
        /// </summary>
        /// <param name="asset">검증할 BT 에셋</param>
        private static void ValidateTree(MonsterBehaviorTreeAsset asset)
        {
            if (asset == null)
            {
                return;
            }

            var issues = MonsterBtValidator.Validate(asset);
            MonsterBtValidator.LogIssues(asset, issues);
        }

        /// <summary>
        /// 현재 BT 에셋에 근접 전투 예시 트리를 생성한다.
        /// </summary>
        /// <param name="asset">예시 트리를 생성할 BT 에셋</param>
        private static void CreateExampleTree(MonsterBehaviorTreeAsset asset)
        {
            if (asset == null)
            {
                return;
            }

            Undo.RecordObject(asset, "Create Example BT");
            MonsterBtPresetBuilder.BuildMeleeExample(asset);
            EditorUtility.SetDirty(asset);
        }

        /// <summary>
        /// 지정한 몬스터 스킬 UID로 CastRange 기반 스킬 전투 예시 트리를 생성합니다.
        /// </summary>
        /// <param name="asset">예시 트리를 생성할 BT 에셋입니다.</param>
        /// <param name="skillUid">사용할 monster skill UID입니다.</param>
        private static void CreateSkillExampleTree(MonsterBehaviorTreeAsset asset, int skillUid)
        {
            if (asset == null)
                return;

            if (skillUid <= 0)
            {
                Debug.LogWarning("[BT] Monster Skill UID must be greater than 0.", asset);
                return;
            }

            Undo.RecordObject(asset, "Create Skill CastRange BT");
            if (!MonsterBtPresetBuilder.BuildSkillExample(asset, skillUid))
            {
                Debug.LogWarning($"[BT] Failed to create skill combat preset. skillUid={skillUid}", asset);
                return;
            }

            EditorUtility.SetDirty(asset);
        }

        /// <summary>
        /// 현재 에셋의 핵심 요약 정보를 인스펙터에 출력한다.
        /// </summary>
        /// <param name="asset">요약 정보를 표시할 BT 에셋</param>
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
