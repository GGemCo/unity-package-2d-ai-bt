#if UNITY_EDITOR
using System;
using UnityEngine;

namespace GGemCo2DAiBt.Editor
{
    /// <summary>
    /// BT 프리셋 빌더. MVP에서는 예시 트리 1종을 제공한다.
    /// </summary>
    internal static class MonsterBtPresetBuilder
    {
        public static void BuildMeleeExample(MonsterBehaviorTreeAsset asset)
        {
            if (asset == null) return;

            asset.nodes ??= new();
            asset.nodes.Clear();

            // --- Blackboard schema ---
            asset.blackboardSchema ??= new();
            asset.blackboardSchema.keys ??= new();
            asset.blackboardSchema.keys.Clear();

            asset.blackboardSchema.keys.Add(new BlackboardKeyDef
            {
                name = "DesiredMeleeRange",
                type = BtValueType.Float,
                description = "근접 공격 거리(참조용). InAttackRange는 ControllerMonster의 공격 캡슐을 사용한다.",
                defaultFloat = 1.8f
            });
            asset.blackboardSchema.keys.Add(new BlackboardKeyDef
            {
                name = "ChaseGiveUpRange",
                type = BtValueType.Float,
                description = "추적 포기 거리",
                defaultFloat = 12f
            });
            asset.blackboardSchema.keys.Add(new BlackboardKeyDef
            {
                name = "LowHpThreshold",
                type = BtValueType.Float,
                description = "체력 비율 임계값",
                defaultFloat = 0.25f
            });

            // --- Nodes ---
            string root = NewId();
            string emergency = NewId();
            string engage = NewId();
            string idle = NewId();

            // Root Selector
            asset.nodes.Add(new BtNodeRecord
            {
                id = root,
                kind = BtNodeKind.Composite,
                typeId = BtTypeIds.Composite.Selector,
                title = "CombatOrIdle",
                children = { emergency, engage, idle },
                graphPosition = new Vector2(50, 50)
            });

            // EmergencyRetreat Sequence: HasAggro + HpBelow + (Cooldown->Wait)
            string cHasAggro = NewId();
            string cHpBelow = NewId();
            string dRetreatCooldown = NewId();
            string aFace = NewId();
            string aWait = NewId();

            asset.nodes.Add(new BtNodeRecord
            {
                id = emergency,
                kind = BtNodeKind.Composite,
                typeId = BtTypeIds.Composite.Sequence,
                title = "EmergencyRetreat",
                children = { cHasAggro, cHpBelow, dRetreatCooldown },
                graphPosition = new Vector2(350, 10)
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = cHasAggro,
                kind = BtNodeKind.Condition,
                typeId = BtTypeIds.Condition.HasAggroTarget,
                title = "HasAggroTarget",
                graphPosition = new Vector2(650, 0)
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = cHpBelow,
                kind = BtNodeKind.Condition,
                typeId = BtTypeIds.Condition.HpPercentBelow,
                title = "HpPercentBelow",
                parameters =
                {
                    new BtParamValue { key = "threshold", valueType = BtValueType.Float, floatValue = 0.25f }
                },
                graphPosition = new Vector2(650, 60)
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = dRetreatCooldown,
                kind = BtNodeKind.Decorator,
                typeId = BtTypeIds.Decorator.Cooldown,
                title = "Cooldown(retreat_cd)",
                children = { aFace },
                parameters =
                {
                    new BtParamValue { key = "key", valueType = BtValueType.String, stringValue = "retreat_cd" },
                    new BtParamValue { key = "sec", valueType = BtValueType.Float, floatValue = 6f },
                },
                graphPosition = new Vector2(650, 120)
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = aFace,
                kind = BtNodeKind.Action,
                typeId = BtTypeIds.Action.FaceToTarget,
                title = "FaceToTarget",
                graphPosition = new Vector2(950, 120)
            });
            // (MVP) Retreat 이동 액션은 아직 포함하지 않음. 대신 잠깐 Wait로만 처리.
            // 필요 시 Action.MoveAwayFromTarget 등을 추가해 확장.
            asset.nodes.Add(new BtNodeRecord
            {
                id = aWait,
                kind = BtNodeKind.Action,
                typeId = BtTypeIds.Action.Wait,
                title = "Wait(0.7)",
                parameters = { new BtParamValue { key = "sec", valueType = BtValueType.Float, floatValue = 0.7f } },
                graphPosition = new Vector2(950, 180)
            });
            // FaceToTarget 다음에 Wait를 연결(간단 완화)
            var faceIndex = FindNodeIndex(asset, aFace);
            if (faceIndex >= 0)
                asset.nodes[faceIndex].children.Add(aWait);

            // EngageCombat Sequence: HasAggro + TargetWithinDistance + (AttackOrChase Selector)
            string cHasAggro2 = NewId();
            string cWithin = NewId();
            string sAttackOrChase = NewId();

            asset.nodes.Add(new BtNodeRecord
            {
                id = engage,
                kind = BtNodeKind.Composite,
                typeId = BtTypeIds.Composite.Sequence,
                title = "EngageCombat",
                children = { cHasAggro2, cWithin, sAttackOrChase },
                graphPosition = new Vector2(350, 240)
            });

            asset.nodes.Add(new BtNodeRecord
            {
                id = cHasAggro2,
                kind = BtNodeKind.Condition,
                typeId = BtTypeIds.Condition.HasAggroTarget,
                title = "HasAggroTarget",
                graphPosition = new Vector2(650, 220)
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = cWithin,
                kind = BtNodeKind.Condition,
                typeId = BtTypeIds.Condition.TargetWithinDistance,
                title = "TargetWithinDistance(12)",
                parameters = { new BtParamValue { key = "max", valueType = BtValueType.Float, floatValue = 12f } },
                graphPosition = new Vector2(650, 280)
            });

            // AttackOrChase Selector
            string qAttack = NewId();
            string qChase = NewId();
            asset.nodes.Add(new BtNodeRecord
            {
                id = sAttackOrChase,
                kind = BtNodeKind.Composite,
                typeId = BtTypeIds.Composite.Selector,
                title = "AttackOrChase",
                children = { qAttack, qChase },
                graphPosition = new Vector2(650, 340)
            });

            // Attack Sequence: InAttackRange + Stop + Face + Cooldown(AttackBasic)
            string cInRange = NewId();
            string aStop = NewId();
            string aFace2 = NewId();
            string dAtkCd = NewId();
            string aAtk = NewId();
            asset.nodes.Add(new BtNodeRecord
            {
                id = qAttack,
                kind = BtNodeKind.Composite,
                typeId = BtTypeIds.Composite.Sequence,
                title = "AttackIfInRange",
                children = { cInRange, aStop, aFace2, dAtkCd },
                graphPosition = new Vector2(950, 320)
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = cInRange,
                kind = BtNodeKind.Condition,
                typeId = BtTypeIds.Condition.InAttackRange,
                title = "InAttackRange",
                graphPosition = new Vector2(1250, 300)
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = aStop,
                kind = BtNodeKind.Action,
                typeId = BtTypeIds.Action.Stop,
                title = "Stop",
                graphPosition = new Vector2(1250, 360)
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = aFace2,
                kind = BtNodeKind.Action,
                typeId = BtTypeIds.Action.FaceToTarget,
                title = "FaceToTarget",
                graphPosition = new Vector2(1250, 420)
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = dAtkCd,
                kind = BtNodeKind.Decorator,
                typeId = BtTypeIds.Decorator.Cooldown,
                title = "Cooldown(atk_basic)",
                children = { aAtk },
                parameters =
                {
                    new BtParamValue { key = "key", valueType = BtValueType.String, stringValue = "atk_basic" },
                    new BtParamValue { key = "sec", valueType = BtValueType.Float, floatValue = 1.2f },
                },
                graphPosition = new Vector2(1250, 480)
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = aAtk,
                kind = BtNodeKind.Action,
                typeId = BtTypeIds.Action.AttackBasic,
                title = "AttackBasic",
                graphPosition = new Vector2(1550, 480)
            });

            // Chase Sequence: MoveToTarget + WaitOneTick
            string aMove = NewId();
            string aTick = NewId();
            asset.nodes.Add(new BtNodeRecord
            {
                id = qChase,
                kind = BtNodeKind.Composite,
                typeId = BtTypeIds.Composite.Sequence,
                title = "Chase",
                children = { aMove, aTick },
                graphPosition = new Vector2(950, 520)
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = aMove,
                kind = BtNodeKind.Action,
                typeId = BtTypeIds.Action.MoveToTarget,
                title = "MoveToTarget",
                graphPosition = new Vector2(1250, 520)
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = aTick,
                kind = BtNodeKind.Action,
                typeId = BtTypeIds.Action.WaitOneTick,
                title = "WaitOneTick",
                graphPosition = new Vector2(1250, 580)
            });

            // Idle: Wait(0.2)
            string aIdleWait = NewId();
            asset.nodes.Add(new BtNodeRecord
            {
                id = idle,
                kind = BtNodeKind.Composite,
                typeId = BtTypeIds.Composite.Sequence,
                title = "Idle",
                children = { aIdleWait },
                graphPosition = new Vector2(350, 650)
            });
            asset.nodes.Add(new BtNodeRecord
            {
                id = aIdleWait,
                kind = BtNodeKind.Action,
                typeId = BtTypeIds.Action.Wait,
                title = "Wait(0.2)",
                parameters = { new BtParamValue { key = "sec", valueType = BtValueType.Float, floatValue = 0.2f } },
                graphPosition = new Vector2(650, 650)
            });

            asset.rootNodeId = root;
        }

        private static string NewId() => Guid.NewGuid().ToString("N");

        private static int FindNodeIndex(MonsterBehaviorTreeAsset asset, string nodeId)
        {
            for (int i = 0; i < asset.nodes.Count; i++)
            {
                if (asset.nodes[i].id == nodeId) return i;
            }
            return -1;
        }
    }
}
#endif
