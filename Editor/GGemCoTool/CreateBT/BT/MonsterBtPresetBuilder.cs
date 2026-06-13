#if UNITY_EDITOR
using System;
using GGemCo2DAiBt;
using GGemCo2DCore;
using UnityEngine;

namespace GGemCo2DAiBtEditor
{
    /// <summary>
    /// Threat, 전투 범위 및 Leash 시스템을 사용하는 표준 몬스터 BT 프리셋을 생성합니다.
    /// </summary>
    internal static class MonsterBtPresetBuilder
    {
        /// <summary>
        /// 기존 호출부 호환을 유지하면서 신규 근접 전투 프리셋을 생성합니다.
        /// </summary>
        /// <param name="asset">프리셋을 적용할 BT 에셋입니다.</param>
        public static void CreateMeleeBasicPreset(MonsterBehaviorTreeAsset asset)
        {
            BuildMeleeExample(asset);
        }

        /// <summary>
        /// Threat 타겟 선택, Hard Leash 안전 처리, 기본 공격 및 선호 거리 이동을 포함한 근접 전투 트리를 생성합니다.
        /// </summary>
        /// <param name="asset">프리셋을 적용할 BT 에셋입니다.</param>
        public static void BuildMeleeExample(MonsterBehaviorTreeAsset asset)
        {
            if (asset == null)
                return;

            asset.nodes ??= new();
            asset.nodes.Clear();
            BuildDefaultBlackboard(asset);

            string root = NewId();
            string hardLeashBranch = NewId();
            string engageBranch = NewId();
            string idleBranch = NewId();

            asset.nodes.Add(new BtNodeRecord
            {
                id = root,
                kind = BtNodeKind.Composite,
                typeId = BtTypeIds.Composite.Selector,
                title = "CombatRoot",
                children = { hardLeashBranch, engageBranch, idleBranch },
                graphPosition = new Vector2(50f, 80f),
            });

            BuildHardLeashBranch(asset, hardLeashBranch);
            BuildMeleeEngageBranch(asset, engageBranch);
            BuildIdleBranch(asset, idleBranch);
            asset.rootNodeId = root;
        }

        /// <summary>
        /// 지정한 몬스터 스킬을 CastRange 기준으로 선택·추적·실행하는 표준 스킬 전투 트리를 생성합니다.
        /// </summary>
        /// <param name="asset">프리셋을 적용할 BT 에셋입니다.</param>
        /// <param name="skillUid">사용할 monster skill UID입니다.</param>
        /// <returns>유효한 입력으로 프리셋을 생성했으면 <see langword="true"/>입니다.</returns>
        public static bool BuildSkillExample(MonsterBehaviorTreeAsset asset, int skillUid)
        {
            if (asset == null || skillUid <= 0)
                return false;

            asset.nodes ??= new();
            asset.nodes.Clear();
            BuildDefaultBlackboard(asset);

            string root = NewId();
            string hardLeashBranch = NewId();
            string engageBranch = NewId();
            string idleBranch = NewId();

            asset.nodes.Add(new BtNodeRecord
            {
                id = root,
                kind = BtNodeKind.Composite,
                typeId = BtTypeIds.Composite.Selector,
                title = "SkillCombatRoot",
                children = { hardLeashBranch, engageBranch, idleBranch },
                graphPosition = new Vector2(50f, 80f),
            });

            BuildHardLeashBranch(asset, hardLeashBranch);
            BuildSkillEngageBranch(asset, engageBranch, skillUid);
            BuildIdleBranch(asset, idleBranch);
            asset.rootNodeId = root;
            return true;
        }

        /// <summary>
        /// 전투 프로필에 ChaseRange가 없을 때 사용할 최소 호환 블랙보드를 구성합니다.
        /// </summary>
        private static void BuildDefaultBlackboard(MonsterBehaviorTreeAsset asset)
        {
            asset.blackboardSchema ??= new BlackboardSchema();
            asset.blackboardSchema.keys ??= new();
            asset.blackboardSchema.keys.Clear();
            asset.blackboardSchema.keys.Add(new BlackboardKeyDef
            {
                name = "ChaseGiveUpRange",
                type = BtValueType.Float,
                description = "monster_combat_profile.ChaseRange가 0일 때 사용하는 레거시 추적 포기 거리",
                defaultFloat = 12f,
            });
        }

        /// <summary>
        /// Hard Leash 이탈을 BT에서도 명시적으로 감시하여 Core Evade 흐름을 요청하는 최우선 브랜치를 구성합니다.
        /// </summary>
        private static void BuildHardLeashBranch(MonsterBehaviorTreeAsset asset, string branchId)
        {
            string hasTarget = NewId();
            string outsideHardLeash = NewId();
            string beginEvade = NewId();

            asset.nodes.Add(new BtNodeRecord
            {
                id = branchId,
                kind = BtNodeKind.Composite,
                typeId = BtTypeIds.Composite.Sequence,
                title = "HardLeashSafety",
                children = { hasTarget, outsideHardLeash, beginEvade },
                graphPosition = new Vector2(350f, 0f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = hasTarget,
                kind = BtNodeKind.Condition,
                typeId = BtTypeIds.Condition.HasCombatTarget,
                title = "HasCombatTarget",
                graphPosition = new Vector2(680f, 0f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = outsideHardLeash,
                kind = BtNodeKind.Condition,
                typeId = BtTypeIds.Condition.IsOutsideHardLeash,
                title = "OutsideHardLeash",
                graphPosition = new Vector2(680f, 70f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = beginEvade,
                kind = BtNodeKind.Action,
                typeId = BtTypeIds.Action.BeginEvade,
                title = "BeginEvade(HardLimit)",
                parameters =
                {
                    new BtParamValue
                    {
                        key = "trigger",
                        valueType = BtValueType.EnumString,
                        enumValue = nameof(MonsterLeashTrigger.HardLimit),
                    },
                },
                graphPosition = new Vector2(680f, 140f),
            });
        }

        /// <summary>
        /// Threat 타겟을 선택한 뒤 기본 공격 또는 선호 거리 이동을 수행하는 교전 브랜치를 구성합니다.
        /// </summary>
        private static void BuildMeleeEngageBranch(MonsterBehaviorTreeAsset asset, string branchId)
        {
            string selectTarget = NewId();
            string hasTarget = NewId();
            string combatSelector = NewId();
            string attackSequence = NewId();
            string repositionSequence = NewId();

            asset.nodes.Add(new BtNodeRecord
            {
                id = branchId,
                kind = BtNodeKind.Composite,
                typeId = BtTypeIds.Composite.Sequence,
                title = "EngageCombat",
                children = { selectTarget, hasTarget, combatSelector },
                graphPosition = new Vector2(350f, 280f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = selectTarget,
                kind = BtNodeKind.Action,
                typeId = BtTypeIds.Action.SelectCombatTarget,
                title = "SelectCombatTarget",
                graphPosition = new Vector2(680f, 230f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = hasTarget,
                kind = BtNodeKind.Condition,
                typeId = BtTypeIds.Condition.HasCombatTarget,
                title = "HasCombatTarget",
                graphPosition = new Vector2(680f, 300f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = combatSelector,
                kind = BtNodeKind.Composite,
                typeId = BtTypeIds.Composite.Selector,
                title = "AttackOrReposition",
                children = { attackSequence, repositionSequence },
                graphPosition = new Vector2(680f, 390f),
            });

            BuildBasicAttackBranch(asset, attackSequence);
            BuildPreferredRangeMoveBranch(asset, repositionSequence);
        }

        /// <summary>
        /// 지정 스킬의 사용 가능 여부와 CastRange를 우선 평가하고, 범위 밖이면 스킬 사거리까지 이동하는 브랜치를 구성합니다.
        /// </summary>
        private static void BuildSkillEngageBranch(MonsterBehaviorTreeAsset asset, string branchId, int skillUid)
        {
            string selectTarget = NewId();
            string hasTarget = NewId();
            string combatSelector = NewId();
            string castSequence = NewId();
            string moveSequence = NewId();

            asset.nodes.Add(new BtNodeRecord
            {
                id = branchId,
                kind = BtNodeKind.Composite,
                typeId = BtTypeIds.Composite.Sequence,
                title = $"EngageSkill({skillUid})",
                children = { selectTarget, hasTarget, combatSelector },
                graphPosition = new Vector2(350f, 280f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = selectTarget,
                kind = BtNodeKind.Action,
                typeId = BtTypeIds.Action.SelectCombatTarget,
                title = "SelectCombatTarget",
                graphPosition = new Vector2(680f, 230f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = hasTarget,
                kind = BtNodeKind.Condition,
                typeId = BtTypeIds.Condition.HasCombatTarget,
                title = "HasCombatTarget",
                graphPosition = new Vector2(680f, 300f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = combatSelector,
                kind = BtNodeKind.Composite,
                typeId = BtTypeIds.Composite.Selector,
                title = "CastOrApproach",
                children = { castSequence, moveSequence },
                graphPosition = new Vector2(680f, 390f),
            });

            BuildSkillCastBranch(asset, castSequence, skillUid);
            BuildSkillRangeMoveBranch(asset, moveSequence, skillUid);
        }

        /// <summary>
        /// 스킬 사용 가능 여부와 실제 CastRange를 확인한 후 스킬 완료까지 대기하는 브랜치를 구성합니다.
        /// </summary>
        private static void BuildSkillCastBranch(MonsterBehaviorTreeAsset asset, string branchId, int skillUid)
        {
            string canUse = NewId();
            string inCastRange = NewId();
            string stop = NewId();
            string face = NewId();
            string useSkill = NewId();

            asset.nodes.Add(new BtNodeRecord
            {
                id = branchId,
                kind = BtNodeKind.Composite,
                typeId = BtTypeIds.Composite.Sequence,
                title = $"CastSkill({skillUid})",
                children = { canUse, inCastRange, stop, face, useSkill },
                graphPosition = new Vector2(1020f, 300f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = canUse,
                kind = BtNodeKind.Condition,
                typeId = BtTypeIds.Condition.CanUseSkill,
                title = "CanUseSkill",
                parameters =
                {
                    new BtParamValue { key = "skillUid", valueType = BtValueType.Int, intValue = skillUid },
                    new BtParamValue { key = "requireTarget", valueType = BtValueType.Bool, boolValue = true },
                },
                graphPosition = new Vector2(1360f, 220f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = inCastRange,
                kind = BtNodeKind.Condition,
                typeId = BtTypeIds.Condition.IsSkillInCastRange,
                title = "IsSkillInCastRange",
                parameters =
                {
                    new BtParamValue { key = "skillUid", valueType = BtValueType.Int, intValue = skillUid },
                    new BtParamValue { key = "requireTarget", valueType = BtValueType.Bool, boolValue = true },
                    new BtParamValue { key = "extraMargin", valueType = BtValueType.Float, floatValue = 0f },
                },
                graphPosition = new Vector2(1360f, 290f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = stop,
                kind = BtNodeKind.Action,
                typeId = BtTypeIds.Action.Stop,
                title = "Stop",
                graphPosition = new Vector2(1360f, 360f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = face,
                kind = BtNodeKind.Action,
                typeId = BtTypeIds.Action.FaceToTarget,
                title = "FaceToTarget",
                graphPosition = new Vector2(1360f, 430f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = useSkill,
                kind = BtNodeKind.Action,
                typeId = BtTypeIds.Action.UseSkillAndWait,
                title = $"UseSkillAndWait({skillUid})",
                parameters =
                {
                    new BtParamValue { key = "skillUid", valueType = BtValueType.Int, intValue = skillUid },
                    new BtParamValue { key = "requireTarget", valueType = BtValueType.Bool, boolValue = true },
                    new BtParamValue { key = "restartRoot", valueType = BtValueType.Bool, boolValue = true },
                    new BtParamValue { key = "validateCastRange", valueType = BtValueType.Bool, boolValue = true },
                    new BtParamValue { key = "castRangeMargin", valueType = BtValueType.Float, floatValue = 0f },
                },
                graphPosition = new Vector2(1360f, 500f),
            });
        }

        /// <summary>
        /// 지정 스킬의 CastRange까지 접근하는 이동 브랜치를 구성합니다.
        /// </summary>
        private static void BuildSkillRangeMoveBranch(MonsterBehaviorTreeAsset asset, string branchId, int skillUid)
        {
            string move = NewId();
            asset.nodes.Add(new BtNodeRecord
            {
                id = branchId,
                kind = BtNodeKind.Composite,
                typeId = BtTypeIds.Composite.Sequence,
                title = "ApproachSkillRange",
                children = { move },
                graphPosition = new Vector2(1020f, 620f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = move,
                kind = BtNodeKind.Action,
                typeId = BtTypeIds.Action.MoveToSkillRange,
                title = $"MoveToSkillRange({skillUid})",
                parameters =
                {
                    new BtParamValue { key = "skillUid", valueType = BtValueType.Int, intValue = skillUid },
                    new BtParamValue { key = "extraMargin", valueType = BtValueType.Float, floatValue = 0f },
                    new BtParamValue { key = "stopInRange", valueType = BtValueType.Bool, boolValue = true },
                    new BtParamValue { key = "restartRootInRange", valueType = BtValueType.Bool, boolValue = true },
                    new BtParamValue { key = "giveUpDistance", valueType = BtValueType.Float, floatValue = -1f },
                    new BtParamValue { key = "giveUpDistanceKey", valueType = BtValueType.String, stringValue = "ChaseGiveUpRange" },
                },
                graphPosition = new Vector2(1360f, 620f),
            });
        }

        /// <summary>
        /// 기본 공격 시작 범위에 들어온 타겟을 정지·정면 보정 후 공격하는 브랜치를 구성합니다.
        /// </summary>
        private static void BuildBasicAttackBranch(MonsterBehaviorTreeAsset asset, string branchId)
        {
            string inAttackRange = NewId();
            string stop = NewId();
            string face = NewId();
            string cooldown = NewId();
            string attack = NewId();

            asset.nodes.Add(new BtNodeRecord
            {
                id = branchId,
                kind = BtNodeKind.Composite,
                typeId = BtTypeIds.Composite.Sequence,
                title = "BasicAttack",
                children = { inAttackRange, stop, face, cooldown },
                graphPosition = new Vector2(1020f, 300f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = inAttackRange,
                kind = BtNodeKind.Condition,
                typeId = BtTypeIds.Condition.InAttackRange,
                title = "InAttackRange",
                graphPosition = new Vector2(1360f, 250f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = stop,
                kind = BtNodeKind.Action,
                typeId = BtTypeIds.Action.Stop,
                title = "Stop",
                graphPosition = new Vector2(1360f, 320f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = face,
                kind = BtNodeKind.Action,
                typeId = BtTypeIds.Action.FaceToTarget,
                title = "FaceToTarget",
                graphPosition = new Vector2(1360f, 390f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = cooldown,
                kind = BtNodeKind.Decorator,
                typeId = BtTypeIds.Decorator.Cooldown,
                title = "Cooldown(atk_basic)",
                children = { attack },
                parameters =
                {
                    new BtParamValue { key = "key", valueType = BtValueType.String, stringValue = "atk_basic" },
                    new BtParamValue { key = "sec", valueType = BtValueType.Float, floatValue = 1.2f },
                },
                graphPosition = new Vector2(1360f, 460f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = attack,
                kind = BtNodeKind.Action,
                typeId = BtTypeIds.Action.AttackBasic,
                title = "AttackBasic",
                graphPosition = new Vector2(1680f, 460f),
            });
        }

        /// <summary>
        /// 공격할 수 없는 경우 전투 프로필의 선호 거리까지 접근하거나 후퇴하는 브랜치를 구성합니다.
        /// </summary>
        private static void BuildPreferredRangeMoveBranch(MonsterBehaviorTreeAsset asset, string branchId)
        {
            string move = NewId();
            asset.nodes.Add(new BtNodeRecord
            {
                id = branchId,
                kind = BtNodeKind.Composite,
                typeId = BtTypeIds.Composite.Sequence,
                title = "Reposition",
                children = { move },
                graphPosition = new Vector2(1020f, 580f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = move,
                kind = BtNodeKind.Action,
                typeId = BtTypeIds.Action.MoveToPreferredRange,
                title = "MoveToPreferredRange",
                parameters =
                {
                    new BtParamValue { key = "allowRetreat", valueType = BtValueType.Bool, boolValue = true },
                    new BtParamValue { key = "clampToAttackRange", valueType = BtValueType.Bool, boolValue = true },
                    new BtParamValue { key = "stopInRange", valueType = BtValueType.Bool, boolValue = true },
                    new BtParamValue { key = "restartRootInRange", valueType = BtValueType.Bool, boolValue = true },
                    new BtParamValue { key = "giveUpDistance", valueType = BtValueType.Float, floatValue = -1f },
                    new BtParamValue { key = "giveUpDistanceKey", valueType = BtValueType.String, stringValue = "ChaseGiveUpRange" },
                },
                graphPosition = new Vector2(1360f, 580f),
            });
        }

        /// <summary>
        /// 전투 타겟이 없을 때 낮은 빈도로 루트를 다시 평가하는 대기 브랜치를 구성합니다.
        /// </summary>
        private static void BuildIdleBranch(MonsterBehaviorTreeAsset asset, string branchId)
        {
            string wait = NewId();
            asset.nodes.Add(new BtNodeRecord
            {
                id = branchId,
                kind = BtNodeKind.Composite,
                typeId = BtTypeIds.Composite.Sequence,
                title = "Idle",
                children = { wait },
                graphPosition = new Vector2(350f, 700f),
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = wait,
                kind = BtNodeKind.Action,
                typeId = BtTypeIds.Action.Wait,
                title = "Wait(0.2)",
                parameters =
                {
                    new BtParamValue { key = "sec", valueType = BtValueType.Float, floatValue = 0.2f },
                },
                graphPosition = new Vector2(680f, 700f),
            });
        }

        /// <summary>
        /// 새 BT 노드 식별자를 생성합니다.
        /// </summary>
        private static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
#endif
