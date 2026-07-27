using GGemCo2DCore;

namespace GGemCo2DAiBt
{
    /// <summary>
    /// 몬스터 페이즈 전환 상태를 외부 시나리오 시스템에 전달하기 위한 범용 컨텍스트입니다.
    /// 특정 보스/게임 전용 로직은 포함하지 않습니다.
    /// </summary>
    public readonly struct MonsterPhaseTransitionContext
    {
        public readonly Monster Monster;

        /// <summary>
        /// 전환 대상 몬스터의 Unity 런타임 인스턴스 ID입니다.
        /// </summary>
        public readonly int MonsterInstanceId;

        public readonly int MonsterUid;
        public readonly int CurrentPhaseIndex;
        public readonly int NextPhaseIndex;
        public readonly int TransitionCutsceneUid;
        public readonly long HoldHp;

        /// <summary>
        /// 페이즈 전환 컨텍스트를 생성합니다.
        /// </summary>
        /// <param name="monster">전환 대상 몬스터입니다.</param>
        /// <param name="currentPhaseIndex">현재 페이즈 번호입니다.</param>
        /// <param name="nextPhaseIndex">다음 페이즈 번호입니다.</param>
        /// <param name="transitionCutsceneUid">현재 페이즈 종료 시점에 시작할 전환 컷신 UID입니다.</param>
        /// <param name="holdHp">전환 중 유지할 HP입니다.</param>
        public MonsterPhaseTransitionContext(
            Monster monster,
            int currentPhaseIndex,
            int nextPhaseIndex,
            int transitionCutsceneUid,
            long holdHp)
        {
            Monster = monster;
            MonsterInstanceId = monster != null ? monster.GetInstanceID() : 0;
            MonsterUid = monster != null ? monster.uid : 0;
            CurrentPhaseIndex = currentPhaseIndex;
            NextPhaseIndex = nextPhaseIndex;
            TransitionCutsceneUid = transitionCutsceneUid;
            HoldHp = holdHp;
        }
    }

    /// <summary>
    /// 몬스터 페이즈 전환의 최종 종료 상태입니다.
    /// </summary>
    public enum MonsterPhaseTransitionFinishStatus
    {
        /// <summary>
        /// 다음 페이즈 적용과 전환 잠금 해제가 정상적으로 완료되었습니다.
        /// </summary>
        Succeeded = 0,

        /// <summary>
        /// 다음 페이즈 적용에 실패하여 전환이 종료되었습니다.
        /// </summary>
        Failed = 1,

        /// <summary>
        /// 오브젝트 비활성화, 풀 반환 또는 런타임 초기화로 전환이 취소되었습니다.
        /// </summary>
        Cancelled = 2,
    }

    /// <summary>
    /// 성공, 실패 또는 취소를 포함한 몬스터 페이즈 전환 종료 결과입니다.
    /// </summary>
    public readonly struct MonsterPhaseTransitionResult
    {
        /// <summary>
        /// 종료된 페이즈 전환의 컨텍스트입니다.
        /// </summary>
        public readonly MonsterPhaseTransitionContext Context;

        /// <summary>
        /// 페이즈 전환의 최종 종료 상태입니다.
        /// </summary>
        public readonly MonsterPhaseTransitionFinishStatus Status;

        /// <summary>
        /// 페이즈 전환이 정상적으로 완료되었는지 여부입니다.
        /// </summary>
        public bool Succeeded => Status == MonsterPhaseTransitionFinishStatus.Succeeded;

        /// <summary>
        /// 페이즈 전환 종료 결과를 생성합니다.
        /// </summary>
        /// <param name="context">종료된 페이즈 전환 컨텍스트입니다.</param>
        /// <param name="status">페이즈 전환의 최종 종료 상태입니다.</param>
        public MonsterPhaseTransitionResult(
            MonsterPhaseTransitionContext context,
            MonsterPhaseTransitionFinishStatus status)
        {
            Context = context;
            Status = status;
        }
    }
}
