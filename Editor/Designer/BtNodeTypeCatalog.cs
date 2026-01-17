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
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = "Condition.HasAggroTarget", DisplayName = "Has Aggro Target" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = "Condition.InAttackRange", DisplayName = "In Attack Range" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = "Condition.HpPercentBelow", DisplayName = "Hp Percent Below" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = "Condition.TargetWithinDistance", DisplayName = "Target Within Distance" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Condition, TypeId = "Condition.CanUseSkill", DisplayName = "Can Use Skill" },

            // Action
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = "Action.WaitOneTick", DisplayName = "Wait One Tick" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = "Action.Wait", DisplayName = "Wait" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = "Action.Stop", DisplayName = "Stop" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = "Action.FaceToTarget", DisplayName = "Face To Target" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = "Action.MoveToTarget", DisplayName = "Move To Target" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = "Action.AttackBasic", DisplayName = "Attack Basic" },
            new BtNodeTypeDef{ Kind = BtNodeKind.Action, TypeId = "Action.UseSkill", DisplayName = "Use Skill" },
        };

        private static readonly Dictionary<string, IReadOnlyList<BtParamDef>> _paramDefsByTypeId =
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
                    new BtParamDef("skillId", BtValueType.String, required: true, defaultValue: "SK_0001"),
                    new BtParamDef("requireTarget", BtValueType.Bool, required: false, defaultValue: true),
                },

                // Action
                ["Action.Wait"] = new List<BtParamDef>
                {
                    new BtParamDef("sec", BtValueType.Float, required: true, defaultValue: 0.2f, min: 0f),
                },
                ["Action.UseSkill"] = new List<BtParamDef>
                {
                    new BtParamDef("skillId", BtValueType.String, required: true, defaultValue: "SK_0001"),
                    new BtParamDef("requireTarget", BtValueType.Bool, required: false, defaultValue: true),
                    // EnumString: Running / Success
                    new BtParamDef("busyReturn", BtValueType.EnumString, required: false, defaultValue: "Running"),
                },

            };

        public static IReadOnlyList<BtParamDef> GetParamDefs(string typeId)
        {
            return typeId != null && _paramDefsByTypeId.TryGetValue(typeId, out var defs)
                ? defs
                : Array.Empty<BtParamDef>();
        }
    }
}
#endif
