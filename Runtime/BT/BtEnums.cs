using System;

namespace GGemCo2DAiBt
{
    /// <summary>
    /// BT 노드 평가 결과.
    /// </summary>
    public enum BtStatus
    {
        Failure = 0,
        Success = 1,
        Running = 2,
    }

    /// <summary>
    /// 노드의 대분류.
    /// </summary>
    public enum BtNodeKind
    {
        Composite = 0,
        Decorator = 1,
        Condition = 2,
        Action = 3,
    }

    /// <summary>
    /// 파라미터 / 블랙보드 키 값 타입.
    /// </summary>
    public enum BtValueType
    {
        Bool = 0,
        Int = 1,
        Float = 2,
        String = 3,
        Vector2 = 4,
        Vector3 = 5,
        EnumString = 6,
        BlackboardKey = 7,
    }

    /// <summary>
    /// 실행 중 우선순위 재평가(고급). MVP에서는 None만 사용해도 된다.
    /// </summary>
    public enum BtAbortMode
    {
        None = 0,
        Self = 1,
        LowerPriority = 2,
        Both = 3,
    }

    /// <summary>
    /// 공용으로 사용하는 노드 타입 ID 규칙.
    /// </summary>
    public static class BtTypeIds
    {
        public static class Composite
        {
            public const string Selector = "Composite.Selector";
            public const string Sequence = "Composite.Sequence";
            public const string RandomWeighted = "Composite.RandomWeighted";
        }

        public static class Decorator
        {
            public const string Cooldown = "Decorator.Cooldown";
            public const string Timeout = "Decorator.Timeout";
        }

        public static class Condition
        {
            public const string HasAggroTarget = "Condition.HasAggroTarget";
            public const string InAttackRange = "Condition.InAttackRange";
            public const string HpPercentBelow = "Condition.HpPercentBelow";
            public const string TargetWithinDistance = "Condition.TargetWithinDistance";
            public const string CanUseSkill = "Condition.CanUseSkill";

            // Skill UID별 사용 횟수 비교
            public const string SkillUseCountCompare = "Condition.SkillUseCountCompare";
        }

        public static class Action
        {
            public const string Wait = "Action.Wait";
            public const string WaitOneTick = "Action.WaitOneTick";
            public const string Stop = "Action.Stop";
            public const string FaceToTarget = "Action.FaceToTarget";
            public const string MoveToTarget = "Action.MoveToTarget";
            public const string AttackBasic = "Action.AttackBasic";
            public const string UseSkill = "Action.UseSkill";
            public const string ClearAggro = "Action.ClearAggro";
        }
    }
}
