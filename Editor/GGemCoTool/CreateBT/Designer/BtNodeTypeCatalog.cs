#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using GGemCo2DAiBt;

namespace GGemCo2DAiBtEditor
{
    public sealed class BtNodeTypeDef
    {
        public BtNodeKind Kind;
        public string TypeId;
        public string DisplayName;
    }

    /// <summary>
    /// 노드 파라미터 UI 자동 생성용 정의.
    /// </summary>
    public readonly struct BtParamDef
    {
        public readonly string Key;
        public readonly BtValueType ValueType;
        public readonly bool Required;
        public readonly object DefaultValue;
        public readonly float? Min;
        public readonly float? Max;

        public BtParamDef(string key, BtValueType valueType, bool required, object defaultValue, float? min = null, float? max = null)
        {
            Key = key;
            ValueType = valueType;
            Required = required;
            DefaultValue = defaultValue;
            Min = min;
            Max = max;
        }
    }

    /// <summary>
    /// 디자이너에서 생성 가능한 노드 카탈로그.
    /// - 런타임에서 지원하는 typeId와 반드시 일치해야 한다.
    /// - 향후 노드 확장 시 이 목록과 파라미터 정의를 함께 갱신한다.
    /// </summary>
    public static class BtNodeTypeCatalog
    {
        public static readonly IReadOnlyList<BtNodeTypeDef> All = new List<BtNodeTypeDef>
        {
            // Composite
            new BtNodeTypeDef{ Kind = BtNodeKind.Composite, TypeId = "Composite.Selector", DisplayName = "Selector" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Composite, TypeId = "Composite.Sequence", DisplayName = "Sequence" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Composite, TypeId = "Composite.RandomWeighted", DisplayName = "Random Weighted" },

            // Decorator
            new BtNodeTypeDef{ Kind = BtNodeKind.Decorator, TypeId = "Decorator.Cooldown", DisplayName = "Cooldown" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Decorator, TypeId = "Decorator.Timeout", DisplayName = "Timeout" },

            // Condition
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = BtTypeIds.Condition.HasCombatTarget, DisplayName = "Has Combat Target" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = BtTypeIds.Condition.HasAggroTarget, DisplayName = "Has Aggro Target (Legacy)" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = BtTypeIds.Condition.InAttackRange, DisplayName = "In Attack Range" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = BtTypeIds.Condition.IsTargetInPreferredRange, DisplayName = "Target In Preferred Range" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = BtTypeIds.Condition.IsTargetTooClose, DisplayName = "Target Too Close" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = BtTypeIds.Condition.IsTargetTooFar, DisplayName = "Target Too Far" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = BtTypeIds.Condition.IsOutsideSoftLeash, DisplayName = "Outside Soft Leash" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = BtTypeIds.Condition.IsOutsideHardLeash, DisplayName = "Outside Hard Leash" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = BtTypeIds.Condition.IsReturningHome, DisplayName = "Is Returning Home" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = "Condition.HpPercentBelow", DisplayName = "Hp Percent Below" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = "Condition.TargetWithinDistance", DisplayName = "Target Within Distance" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = "Condition.CanUseSkill", DisplayName = "Can Use Skill" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = "Condition.IsSkillInCastRange", DisplayName = "Is Skill In Cast Range" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = "Condition.SkillUseCountCompare", DisplayName = "Skill Use Count Compare" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = "Condition.LastSkillResult", DisplayName = "Last Skill Result" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = "Condition.LastSkillCombatOutcome", DisplayName = "Last Skill Combat Outcome" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = "Condition.HasAffect", DisplayName = "Has Affect" },

            // Action
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = BtTypeIds.Action.SelectCombatTarget, DisplayName = "Select Combat Target" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = "Action.WaitOneTick", DisplayName = "Wait One Tick" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = "Action.Wait", DisplayName = "Wait" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = "Action.Stop", DisplayName = "Stop" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = "Action.FaceToTarget", DisplayName = "Face To Target" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = BtTypeIds.Action.MoveToTarget, DisplayName = "Move To Target (Legacy Approach)" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = BtTypeIds.Action.MoveToPreferredRange, DisplayName = "Move To Preferred Range" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = BtTypeIds.Action.MoveToSkillRange, DisplayName = "Move To Skill Range" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = "Action.AttackBasic", DisplayName = "Attack Basic" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = "Action.UseSkill", DisplayName = "Use Skill" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = "Action.UseSkillAndWait", DisplayName = "Use Skill And Wait" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = "Action.RequestRestartRoot", DisplayName = "Request Restart Root" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = BtTypeIds.Action.BeginEvade, DisplayName = "Begin Leash Evade" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = BtTypeIds.Action.ReleaseCombatTarget, DisplayName = "Release Combat Target" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = BtTypeIds.Action.ClearAggro, DisplayName = "Clear Aggro (Legacy)" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = "Action.ResetSkillUseCount", DisplayName = "Reset Skill Use Count" },
        };

        private static readonly Dictionary<string, IReadOnlyList<BtParamDef>> ParamDefsByTypeId =
            new Dictionary<string, IReadOnlyList<BtParamDef>>(StringComparer.Ordinal)
            {
                // Decorator
                ["Decorator.Cooldown"] = new List<BtParamDef>
                {
                    new BtParamDef("key", BtValueType.String, required: true, defaultValue: "atk_basic"),
                    new BtParamDef("sec", BtValueType.Float, required: true, defaultValue: 1.2f, min: 0f),
                },
                ["Decorator.Timeout"] = new List<BtParamDef>
                {
                    new BtParamDef("sec", BtValueType.Float, required: true, defaultValue: 1.0f, min: 0f),
                },

                // Condition
                ["Condition.HpPercentBelow"] = new List<BtParamDef>
                {
                    new BtParamDef("threshold", BtValueType.Float, required: true, defaultValue: 0.25f, min: 0f, max: 1f),
                },
                ["Condition.TargetWithinDistance"] = new List<BtParamDef>
                {
                    new BtParamDef("max", BtValueType.Float, required: true, defaultValue: 12f, min: 0f),
                },
                ["Condition.CanUseSkill"] = new List<BtParamDef>
                {
                    new BtParamDef("skillUid", BtValueType.Int, required: true, defaultValue: 0),
                    new BtParamDef("requireTarget", BtValueType.Bool, required: false, defaultValue: true),
                },
                ["Condition.IsSkillInCastRange"] = new List<BtParamDef>
                {
                    new BtParamDef("skillUid", BtValueType.Int, required: true, defaultValue: 0),
                    new BtParamDef("requireTarget", BtValueType.Bool, required: false, defaultValue: true),
                    new BtParamDef("extraMargin", BtValueType.Float, required: false, defaultValue: 0f),
                },
                ["Condition.SkillUseCountCompare"] = new List<BtParamDef>
                {
                    new BtParamDef("skillUid", BtValueType.Int, required: true, defaultValue: 0),
                    // EnumString: >, >=, ==, !=, <, <=
                    new BtParamDef("op", BtValueType.EnumString, required: true, defaultValue: ">="),
                    new BtParamDef("value", BtValueType.Int, required: true, defaultValue: 1),
                    new BtParamDef("resetOnSuccess", BtValueType.Bool, required: false, defaultValue: false),
                },
                ["Condition.HasAffect"] = new List<BtParamDef>
                {
                    new BtParamDef("affectUid", BtValueType.Int, required: true, defaultValue: 0),
                },
                ["Condition.LastSkillResult"] = new List<BtParamDef>
                {
                    new BtParamDef("skillUid", BtValueType.Int, required: true, defaultValue: 0),
                    new BtParamDef("result", BtValueType.EnumString, required: true, defaultValue: "Succeeded"),
                    new BtParamDef("consume", BtValueType.Bool, required: false, defaultValue: true),
                },
                ["Condition.LastSkillCombatOutcome"] = new List<BtParamDef>
                {
                    new BtParamDef("skillUid", BtValueType.Int, required: true, defaultValue: 0),
                    new BtParamDef("outcome", BtValueType.EnumString, required: true, defaultValue: "Hit"),
                    new BtParamDef("consume", BtValueType.Bool, required: false, defaultValue: true),
                },

                // Action
                ["Action.Wait"] = new List<BtParamDef>
                {
                    new BtParamDef("sec", BtValueType.Float, required: true, defaultValue: 0.2f, min: 0f),
                },
                ["Action.MoveToTarget"] = new List<BtParamDef>
                {
                    new BtParamDef("stopOnAttackRange", BtValueType.Bool, required: false, defaultValue: true),
                    new BtParamDef("restartOnAttackRange", BtValueType.Bool, required: false, defaultValue: true),
                    new BtParamDef("giveUpDistance", BtValueType.Float, required: false, defaultValue: -1f),
                    new BtParamDef("giveUpDistanceKey", BtValueType.String, required: false, defaultValue: "ChaseGiveUpRange"),
                },
                [BtTypeIds.Action.MoveToPreferredRange] = new List<BtParamDef>
                {
                    new BtParamDef("allowRetreat", BtValueType.Bool, required: false, defaultValue: true),
                    new BtParamDef("clampToAttackRange", BtValueType.Bool, required: false, defaultValue: false),
                    new BtParamDef("stopInRange", BtValueType.Bool, required: false, defaultValue: true),
                    new BtParamDef("restartRootInRange", BtValueType.Bool, required: false, defaultValue: true),
                    new BtParamDef("giveUpDistance", BtValueType.Float, required: false, defaultValue: -1f),
                    new BtParamDef("giveUpDistanceKey", BtValueType.String, required: false, defaultValue: "ChaseGiveUpRange"),
                },
                [BtTypeIds.Action.MoveToSkillRange] = new List<BtParamDef>
                {
                    new BtParamDef("skillUid", BtValueType.Int, required: true, defaultValue: 0),
                    new BtParamDef("extraMargin", BtValueType.Float, required: false, defaultValue: 0f),
                    new BtParamDef("stopInRange", BtValueType.Bool, required: false, defaultValue: true),
                    new BtParamDef("restartRootInRange", BtValueType.Bool, required: false, defaultValue: true),
                    new BtParamDef("giveUpDistance", BtValueType.Float, required: false, defaultValue: -1f),
                    new BtParamDef("giveUpDistanceKey", BtValueType.String, required: false, defaultValue: "ChaseGiveUpRange"),
                },
                ["Action.UseSkill"] = new List<BtParamDef>
                {
                    new BtParamDef("skillUid", BtValueType.Int, required: true, defaultValue: 0),
                    new BtParamDef("requireTarget", BtValueType.Bool, required: false, defaultValue: true),
                    // EnumString: Running / Success
                    new BtParamDef("busyReturn", BtValueType.EnumString, required: false, defaultValue: "Running"),
                    new BtParamDef("validateCastRange", BtValueType.Bool, required: false, defaultValue: true),
                    new BtParamDef("castRangeMargin", BtValueType.Float, required: false, defaultValue: 0f),
                },
                ["Action.UseSkillAndWait"] = new List<BtParamDef>
                {
                    new BtParamDef("skillUid", BtValueType.Int, required: true, defaultValue: 0),
                    new BtParamDef("requireTarget", BtValueType.Bool, required: false, defaultValue: true),
                    new BtParamDef("restartRoot", BtValueType.Bool, required: false, defaultValue: false),
                    new BtParamDef("validateCastRange", BtValueType.Bool, required: false, defaultValue: true),
                    new BtParamDef("castRangeMargin", BtValueType.Float, required: false, defaultValue: 0f),
                },
                [BtTypeIds.Action.BeginEvade] = new List<BtParamDef>
                {
                    new BtParamDef("trigger", BtValueType.EnumString, required: false, defaultValue: "Manual"),
                },
                ["Action.RequestRestartRoot"] = new List<BtParamDef>
                {
                    new BtParamDef("reason", BtValueType.String, required: false, defaultValue: string.Empty),
                },
                ["Action.ResetSkillUseCount"] = new List<BtParamDef>
                {
                    new BtParamDef("mode", BtValueType.EnumString, required: true, defaultValue: "AllReset"),
                    new BtParamDef("skillUid", BtValueType.Int, required: false, defaultValue: 0),
                    new BtParamDef("value", BtValueType.Int, required: false, defaultValue: 0),
                },
            };

        public static IReadOnlyList<BtParamDef> GetParamDefs(string typeId)
        {
            return typeId != null && ParamDefsByTypeId.TryGetValue(typeId, out var defs)
                ? defs
                : Array.Empty<BtParamDef>();
        }
    }
}
#endif
