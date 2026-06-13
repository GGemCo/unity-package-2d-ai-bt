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
            /// <summary>Threat 시스템에서 선택 가능한 현재 전투 타겟이 있는지 확인합니다.</summary>
            public const string HasCombatTarget = "Condition.HasCombatTarget";

            /// <summary>기존 BT 에셋 호환을 위한 레거시 전투 타겟 조건입니다.</summary>
            public const string HasAggroTarget = "Condition.HasAggroTarget";

            /// <summary>현재 타겟이 기본 공격 시작 범위 안인지 확인합니다.</summary>
            public const string InAttackRange = "Condition.InAttackRange";

            /// <summary>현재 타겟이 몬스터의 선호 전투 거리 구간 안인지 확인합니다.</summary>
            public const string IsTargetInPreferredRange = "Condition.IsTargetInPreferredRange";

            /// <summary>현재 타겟이 선호 전투 거리보다 가까운지 확인합니다.</summary>
            public const string IsTargetTooClose = "Condition.IsTargetTooClose";

            /// <summary>현재 타겟이 선호 전투 거리보다 먼지 확인합니다.</summary>
            public const string IsTargetTooFar = "Condition.IsTargetTooFar";

            /// <summary>몬스터 또는 타겟이 홈 기준 Soft Leash 범위를 벗어났는지 확인합니다.</summary>
            public const string IsOutsideSoftLeash = "Condition.IsOutsideSoftLeash";

            /// <summary>몬스터 또는 타겟이 홈 기준 Hard Leash 범위를 벗어났는지 확인합니다.</summary>
            public const string IsOutsideHardLeash = "Condition.IsOutsideHardLeash";

            /// <summary>몬스터가 홈 복귀 또는 재활성 대기 중인지 확인합니다.</summary>
            public const string IsReturningHome = "Condition.IsReturningHome";

            /// <summary>현재 대상의 공격 슬롯을 예약할 수 있는지 확인합니다.</summary>
            public const string CanReserveAttackSlot = "Condition.CanReserveAttackSlot";

            /// <summary>현재 유효한 공격 슬롯 예약을 보유하는지 확인합니다.</summary>
            public const string HasAttackSlotReservation = "Condition.HasAttackSlotReservation";
            public const string HpPercentBelow = "Condition.HpPercentBelow";
            public const string TargetWithinDistance = "Condition.TargetWithinDistance";
            public const string CanUseSkill = "Condition.CanUseSkill";
            public const string IsSkillInCastRange = "Condition.IsSkillInCastRange";

            // Skill UID별 사용 횟수 비교
            public const string SkillUseCountCompare = "Condition.SkillUseCountCompare";
            public const string LastSkillResult = "Condition.LastSkillResult";
            public const string LastSkillCombatOutcome = "Condition.LastSkillCombatOutcome";
            public const string HasAffect = "Condition.HasAffect";
        }

        public static class Action
        {
            /// <summary>Threat 목록을 평가하여 현재 전투 타겟을 선택합니다.</summary>
            public const string SelectCombatTarget = "Action.SelectCombatTarget";

            public const string Wait = "Action.Wait";
            public const string WaitOneTick = "Action.WaitOneTick";
            public const string Stop = "Action.Stop";
            public const string FaceToTarget = "Action.FaceToTarget";
            public const string MoveToTarget = "Action.MoveToTarget";

            /// <summary>선호 전투 거리 구간까지 접근하거나 후퇴합니다.</summary>
            public const string MoveToPreferredRange = "Action.MoveToPreferredRange";

            /// <summary>지정한 스킬의 CastRange 안까지 이동합니다.</summary>
            public const string MoveToSkillRange = "Action.MoveToSkillRange";

            public const string AttackBasic = "Action.AttackBasic";
            public const string UseSkill = "Action.UseSkill";
            public const string UseSkillAndWait = "Action.UseSkillAndWait";
            public const string RequestRestartRoot = "Action.RequestRestartRoot";

            /// <summary>Core Leash 시스템에 Evade 및 홈 복귀 시작을 요청합니다.</summary>
            public const string BeginEvade = "Action.BeginEvade";

            /// <summary>현재 Threat와 전투 타겟 관계를 모두 해제합니다.</summary>
            public const string ReleaseCombatTarget = "Action.ReleaseCombatTarget";

            /// <summary>현재 전투 대상의 공격 슬롯을 예약합니다.</summary>
            public const string ReserveAttackSlot = "Action.ReserveAttackSlot";

            /// <summary>현재 보유한 공격 슬롯을 즉시 반환합니다.</summary>
            public const string ReleaseAttackSlot = "Action.ReleaseAttackSlot";

            /// <summary>기존 BT 에셋 호환을 위한 레거시 어그로 해제 액션입니다.</summary>
            public const string ClearAggro = "Action.ClearAggro";
            public const string ResetSkillUseCount = "Action.ResetSkillUseCount";
        }
    }
}
