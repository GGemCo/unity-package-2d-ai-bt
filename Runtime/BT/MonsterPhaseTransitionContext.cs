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
        /// <param name="transitionCutsceneUid">전환 컷신 UID입니다.</param>
        /// <param name="holdHp">전환 중 유지할 HP입니다.</param>
        public MonsterPhaseTransitionContext(
            Monster monster,
            int currentPhaseIndex,
            int nextPhaseIndex,
            int transitionCutsceneUid,
            long holdHp)
        {
            Monster = monster;
            MonsterUid = monster != null ? monster.uid : 0;
            CurrentPhaseIndex = currentPhaseIndex;
            NextPhaseIndex = nextPhaseIndex;
            TransitionCutsceneUid = transitionCutsceneUid;
            HoldHp = holdHp;
        }
    }
}
