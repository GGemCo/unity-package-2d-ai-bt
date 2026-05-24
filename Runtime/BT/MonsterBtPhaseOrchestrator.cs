using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using GGemCo2DCore;
using UnityEngine;

namespace GGemCo2DAiBt
{
    /// <summary>
    /// 몬스터 페이즈 테이블 기반으로 BT를 교체하고 전환 연출을 관리합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MonsterBtPhaseOrchestrator : MonoBehaviour, IMonsterPoolLifecycle, IIncomingHitFinalHpResolver
    {
        private const float MaxCutsceneWaitSeconds = 30f;

        private readonly List<StruckTableMonsterPhase> _phases = new List<StruckTableMonsterPhase>();

        private Monster _owner;
        private MonsterBtRunner _runner;
        private bool _isInitialized;
        private bool _isTransitionRunning;
        private int _currentPhaseListIndex = -1;
        private long _cachedBaseHp;
        private object _brainLockToken;
        private object _controlLockToken;

        /// <summary>
        /// 페이즈 전환이 시작되어 몬스터 제어가 잠기기 직전에 발생합니다.
        /// </summary>
        public event Action<MonsterPhaseTransitionContext> PhaseTransitionStarted;

        /// <summary>
        /// 전환 컷신 재생이 끝난 직후 발생합니다.
        /// </summary>
        public event Action<MonsterPhaseTransitionContext> PhaseTransitionCutsceneCompleted;

        /// <summary>
        /// 다음 페이즈 BT와 시작 HP가 적용된 직후 발생합니다.
        /// </summary>
        public event Action<MonsterPhaseTransitionContext> PhaseApplied;

        /// <summary>
        /// 페이즈 전환 잠금이 해제되고 전환 상태가 종료된 직후 발생합니다.
        /// </summary>
        public event Action<MonsterPhaseTransitionContext> PhaseTransitionCompleted;

        /// <summary>
        /// 현재 적용된 페이즈 번호입니다. 초기화 전에는 0입니다.
        /// </summary>
        public int CurrentPhaseIndex => TryGetPhaseRow(_currentPhaseListIndex, out StruckTableMonsterPhase phase) ? phase.PhaseIndex : 0;

        /// <summary>
        /// 페이즈 정의를 주입하고 첫 페이즈 BT를 적용합니다.
        /// </summary>
        /// <param name="owner">대상 몬스터입니다.</param>
        /// <param name="runner">BT 러너입니다.</param>
        /// <param name="phaseRows">해당 몬스터의 페이즈 행 목록입니다.</param>
        /// <returns>초기화/적용 성공 시 true를 반환합니다.</returns>
        public async Task<bool> InitializeAndApplyAsync(Monster owner, MonsterBtRunner runner, IReadOnlyList<StruckTableMonsterPhase> phaseRows)
        {
            ResetPhaseRuntimeState(clearConfiguration: true);

            _owner = owner;
            _runner = runner;

            if (_owner == null || _runner == null || phaseRows == null || phaseRows.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < phaseRows.Count; i++)
            {
                StruckTableMonsterPhase row = phaseRows[i];
                if (row == null)
                    continue;
                if (row.MonsterUid != _owner.uid)
                    continue;

                _phases.Add(row);
            }

            if (_phases.Count == 0)
                return false;

            _phases.Sort((a, b) =>
            {
                int phaseCompare = a.PhaseIndex.CompareTo(b.PhaseIndex);
                return phaseCompare != 0 ? phaseCompare : a.Uid.CompareTo(b.Uid);
            });

            if (_phases[0].PhaseIndex != 1)
            {
                GcLogger.LogWarning($"[BT][Phase] 1페이즈(PhaseIndex=1)가 없습니다. monsterUid={_owner.uid}");
                return false;
            }

            _cachedBaseHp = Math.Max(1L, _owner.BaseHp);
            _currentPhaseListIndex = 0;

            bool applied = await ApplyPhaseTreeAndStartHpAsync(_currentPhaseListIndex);
            if (!applied)
            {
                ResetPhaseRuntimeState(clearConfiguration: true);
                return false;
            }

            _isInitialized = true;
            return true;
        }

        /// <summary>
        /// 피격 계산의 최종 HP를 페이즈 정책으로 보정합니다.
        /// </summary>
        /// <param name="proposedHp">Core 계산 기준 최종 HP입니다.</param>
        /// <param name="metadataDamage">현재 피격 메타데이터입니다.</param>
        /// <returns>페이즈 정책이 반영된 최종 HP입니다.</returns>
        /// <remarks>
        /// 다음 페이즈가 존재하면 기존과 동일하게 페이즈 전환을 우선 수행하고,
        /// 다음 페이즈가 없는 마지막 페이즈에서는 현재 페이즈의
        /// <c>TransitionCutsceneUid</c>가 설정된 경우 전환 컷신을 우선 재생한 뒤 사망을 확정합니다.
        /// </remarks>
        public long ResolveFinalHpOnIncomingHit(long proposedHp, MetadataDamage metadataDamage)
        {
            if (!_isInitialized || !enabled || !isActiveAndEnabled)
                return proposedHp;
            if (_owner == null || _runner == null)
                return proposedHp;
            if (_owner.IsStatusDead())
                return proposedHp;

            // 전환 중에는 HP를 고정해 중복 전환/중복 연출을 막습니다.
            if (_isTransitionRunning)
                return Math.Max(proposedHp, _owner.CurrentHp.Value);

            if (!TryGetPhaseRow(_currentPhaseListIndex, out _))
                return proposedHp;

            // 현재 페이즈 HP가 모두 소진되면 다음 페이즈로 전환합니다.
            if (proposedHp > 0)
                return proposedHp;

            int nextPhaseIndex = _currentPhaseListIndex + 1;
            if (TryGetPhaseRow(nextPhaseIndex, out _))
            {
                // 사망을 막고 전환 코루틴에서 다음 페이즈 시작 HP를 적용합니다.
                long holdHp = Math.Max(1L, _owner.CurrentHp.Value);
                BeginPhaseTransition(_currentPhaseListIndex, nextPhaseIndex, holdHp);
                return holdHp;
            }

            // 마지막 페이즈 종료 시점의 전환 컷신이 있으면 공용 사망 컷신보다 우선 재생합니다.
            int phaseEndCutsceneUid = ResolvePhaseEndTransitionCutsceneUid(_currentPhaseListIndex);
            if (phaseEndCutsceneUid <= 0)
                return proposedHp;

            long finalHoldHp = Math.Max(1L, _owner.CurrentHp.Value);
            GameObject attacker = metadataDamage != null ? metadataDamage.attacker : null;
            DeathPresentationRequest deathPresentation = metadataDamage != null && metadataDamage.DeathPresentation != null
                ? metadataDamage.DeathPresentation.Clone()
                : null;
            BeginFinalPhaseDeathTransition(phaseEndCutsceneUid, finalHoldHp, attacker, deathPresentation);
            return finalHoldHp;
        }

        /// <summary>
        /// 풀 대여 시 페이즈 런타임 상태를 초기화합니다.
        /// </summary>
        /// <param name="owner">대여된 몬스터입니다.</param>
        public void OnPoolRent(Monster owner)
        {
            ResetPhaseRuntimeState(clearConfiguration: true);
        }

        /// <summary>
        /// 풀 반환 시 페이즈 런타임 상태를 초기화합니다.
        /// </summary>
        /// <param name="owner">반환되는 몬스터입니다.</param>
        public void OnPoolReturn(Monster owner)
        {
            ResetPhaseRuntimeState(clearConfiguration: true);
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            ReleaseTransitionLocks();
            _isTransitionRunning = false;
        }

        /// <summary>
        /// 페이즈 행을 안전하게 조회합니다.
        /// </summary>
        /// <param name="index">조회 인덱스입니다.</param>
        /// <param name="row">조회 결과 행입니다.</param>
        /// <returns>조회 성공 시 true를 반환합니다.</returns>
        private bool TryGetPhaseRow(int index, out StruckTableMonsterPhase row)
        {
            if (index >= 0 && index < _phases.Count)
            {
                row = _phases[index];
                return row != null;
            }

            row = null;
            return false;
        }

        /// <summary>
        /// 내부 리스트 인덱스를 외부에 노출할 페이즈 번호로 변환합니다.
        /// </summary>
        /// <param name="listIndex">페이즈 목록 인덱스입니다.</param>
        /// <returns>테이블의 PhaseIndex 값입니다. 찾지 못하면 0을 반환합니다.</returns>
        private int ResolvePhaseIndexForContext(int listIndex)
        {
            return TryGetPhaseRow(listIndex, out StruckTableMonsterPhase row) ? row.PhaseIndex : 0;
        }

        /// <summary>
        /// 특정 페이즈가 종료될 때 재생할 전환 컷신 UID를 조회합니다.
        /// </summary>
        /// <param name="phaseListIndex">페이즈 목록 인덱스입니다.</param>
        /// <returns>조회된 전환 컷신 UID입니다. 미설정/미조회 시 0을 반환합니다.</returns>
        private int ResolvePhaseEndTransitionCutsceneUid(int phaseListIndex)
        {
            return TryGetPhaseRow(phaseListIndex, out StruckTableMonsterPhase phase)
                ? phase.TransitionCutsceneUid
                : 0;
        }

        /// <summary>
        /// 페이즈 시작 HP를 계산합니다.
        /// </summary>
        /// <param name="phase">대상 페이즈 행입니다.</param>
        /// <returns>계산된 시작 HP(최소 1)입니다.</returns>
        private long ComputePhaseStartHp(StruckTableMonsterPhase phase)
        {
            if (phase == null)
                return Math.Max(1L, _cachedBaseHp);

            if (phase.EndHpFixed > 0)
                return Math.Max(1, phase.EndHpFixed);

            float percent = Mathf.Clamp01(phase.EndHpPercent);
            long baseHp = _cachedBaseHp > 0 ? _cachedBaseHp : (_owner != null ? Math.Max(1L, _owner.BaseHp) : 1L);
            if (percent > 0f)
            {
                long hpByPercent = (long)Math.Round(baseHp * percent, MidpointRounding.AwayFromZero);
                return Math.Max(1L, hpByPercent);
            }

            GcLogger.LogWarning($"[BT][Phase] 페이즈 시작 HP 정책이 비어 있어 몬스터 기본 HP를 사용합니다. phaseUid={phase.Uid}");
            return baseHp;
        }

        /// <summary>
        /// 페이즈 전환 코루틴을 시작합니다.
        /// </summary>
        /// <param name="currentPhaseIndex">현재 페이즈 인덱스입니다.</param>
        /// <param name="nextPhaseIndex">다음 페이즈 인덱스입니다.</param>
        /// <param name="holdHp">전환 중 고정할 HP입니다.</param>
        private void BeginPhaseTransition(int currentPhaseIndex, int nextPhaseIndex, long holdHp)
        {
            if (_isTransitionRunning)
                return;

            _isTransitionRunning = true;
            StartCoroutine(CoTransitionPhase(currentPhaseIndex, nextPhaseIndex, holdHp));
        }

        /// <summary>
        /// 페이즈 전환(잠금 → 컷신 → BT 교체 → 잠금 해제)을 수행합니다.
        /// </summary>
        /// <remarks>
        /// 전환 컷신 UID는 "다음 페이즈 행"이 아니라 "현재(종료되는) 페이즈 행"에서 조회합니다.
        /// </remarks>
        /// <param name="currentPhaseIndex">현재 페이즈 인덱스입니다.</param>
        /// <param name="nextPhaseIndex">다음 페이즈 인덱스입니다.</param>
        /// <param name="holdHp">전환 중 유지할 HP입니다.</param>
        /// <returns>코루틴 이터레이터입니다.</returns>
        private IEnumerator CoTransitionPhase(int currentPhaseIndex, int nextPhaseIndex, long holdHp)
        {
            int transitionCutsceneUid = ResolvePhaseEndTransitionCutsceneUid(currentPhaseIndex);

            MonsterPhaseTransitionContext context = new MonsterPhaseTransitionContext(
                _owner,
                ResolvePhaseIndexForContext(currentPhaseIndex),
                ResolvePhaseIndexForContext(nextPhaseIndex),
                transitionCutsceneUid,
                holdHp);

            PhaseTransitionStarted?.Invoke(context);
            AcquireTransitionLocks();

            bool applied = false;
            try
            {
                // 전환 트리거 프레임에서 HP를 유지값으로 즉시 고정합니다.
                if (_owner != null && _owner.CurrentHp.Value < holdHp)
                {
                    _owner.CurrentHp.OnNext(holdHp);
                }

                if (_owner != null)
                {
                    _owner.Stop(isForce: true);
                }

                if (transitionCutsceneUid > 0)
                {
                    yield return CoPlayTransitionCutscene(transitionCutsceneUid);
                }

                PhaseTransitionCutsceneCompleted?.Invoke(context);

                Task<bool> applyTask = ApplyPhaseTreeAndStartHpAsync(nextPhaseIndex);
                while (!applyTask.IsCompleted)
                    yield return null;

                applied = !applyTask.IsFaulted && !applyTask.IsCanceled && applyTask.Result;
                if (!applied)
                {
                    GcLogger.LogWarning($"[BT][Phase] 다음 페이즈 BT 적용에 실패했습니다. monsterUid={_owner?.uid}, currentIndex={currentPhaseIndex}, nextIndex={nextPhaseIndex}");
                    yield break;
                }

                _currentPhaseListIndex = nextPhaseIndex;
                PhaseApplied?.Invoke(context);
            }
            finally
            {
                ReleaseTransitionLocks();
                _isTransitionRunning = false;

                if (applied)
                {
                    PhaseTransitionCompleted?.Invoke(context);
                }
            }
        }

        /// <summary>
        /// 마지막 페이즈 사망 전환(잠금 → 전환 컷신 → 사망 확정)을 시작합니다.
        /// </summary>
        /// <param name="cutsceneUid">마지막 페이즈 종료 시점 전환 컷신 UID입니다.</param>
        /// <param name="holdHp">컷신 재생 중 유지할 HP입니다.</param>
        /// <param name="attacker">사망 유발 공격자입니다.</param>
        /// <param name="deathPresentation">사망 연출 요청입니다.</param>
        private void BeginFinalPhaseDeathTransition(
            int cutsceneUid,
            long holdHp,
            GameObject attacker,
            DeathPresentationRequest deathPresentation)
        {
            if (_isTransitionRunning)
                return;
            if (cutsceneUid <= 0)
                return;

            _isTransitionRunning = true;
            StartCoroutine(CoFinalizeLastPhaseDeath(cutsceneUid, holdHp, attacker, deathPresentation));
        }

        /// <summary>
        /// 마지막 페이즈 종료 컷신을 재생한 뒤 실제 사망을 확정합니다.
        /// </summary>
        /// <param name="cutsceneUid">마지막 페이즈 종료 시점 전환 컷신 UID입니다.</param>
        /// <param name="holdHp">컷신 재생 중 유지할 HP입니다.</param>
        /// <param name="attacker">사망 유발 공격자입니다.</param>
        /// <param name="deathPresentation">사망 연출 요청입니다.</param>
        /// <returns>코루틴 이터레이터입니다.</returns>
        private IEnumerator CoFinalizeLastPhaseDeath(
            int cutsceneUid,
            long holdHp,
            GameObject attacker,
            DeathPresentationRequest deathPresentation)
        {
            try
            {
                AcquireTransitionLocks();

                if (_owner != null && _owner.CurrentHp.Value < holdHp)
                {
                    _owner.CurrentHp.OnNext(holdHp);
                }

                if (_owner != null)
                {
                    _owner.Stop(isForce: true);
                }

                yield return CoPlayTransitionCutscene(cutsceneUid);

                if (_owner == null || _owner.IsStatusDead())
                    yield break;

                if (_owner is Monster monster)
                {
                    monster.SuppressNextDeadCutsceneOnce();
                }

                _owner.CurrentHp.OnNext(0);
                _owner.CurrentMp.OnNext(0);
                _owner.Dead(
                    CharacterConstants.DieReasonType.Battle,
                    attacker,
                    playDeadAnimation: true,
                    deathPresentation: deathPresentation);
            }
            finally
            {
                ReleaseTransitionLocks();
                _isTransitionRunning = false;
            }
        }

        /// <summary>
        /// 전환 컷신을 재생하고 종료까지 대기합니다.
        /// </summary>
        /// <param name="cutsceneUid">전환 컷신 UID입니다.</param>
        /// <returns>코루틴 이터레이터입니다.</returns>
        private IEnumerator CoPlayTransitionCutscene(int cutsceneUid)
        {
            if (cutsceneUid <= 0)
                yield break;

            CutsceneManager manager = SceneGame.Instance != null ? SceneGame.Instance.CutsceneManager : null;
            if (manager == null)
                yield break;

            manager.PlayCutscene(cutsceneUid);

            // 한 프레임 양보 후 세션 활성 여부를 확인합니다.
            yield return null;

            if (!manager.IsSessionActive())
                yield break;

            float start = Time.realtimeSinceStartup;
            while (manager.IsSessionActive())
            {
                if (Time.realtimeSinceStartup - start > MaxCutsceneWaitSeconds)
                {
                    GcLogger.LogWarning($"[BT][Phase] 컷신 대기 시간이 초과되었습니다. cutsceneUid={cutsceneUid}");
                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>
        /// 지정한 페이즈의 BT를 로드/적용합니다.
        /// </summary>
        /// <param name="phaseIndex">적용할 페이즈 인덱스입니다.</param>
        /// <returns>적용 성공 시 true를 반환합니다.</returns>
        private async Task<bool> ApplyPhaseTreeAsync(int phaseIndex)
        {
            if (_runner == null)
                return false;
            if (!TryGetPhaseRow(phaseIndex, out StruckTableMonsterPhase phase))
                return false;
            if (string.IsNullOrWhiteSpace(phase.BtFileName))
            {
                GcLogger.LogWarning($"[BT][Phase] BtFileName이 비어 있습니다. phaseUid={phase.Uid}");
                return false;
            }

            string key = ConfigAddressableKeyAiBt.GetMonsterBt(phase.BtFileName);
            MonsterBehaviorTreeAsset asset = await AddressableLoaderMonsterBt.LoadTreeAssetAsync(key);
            if (asset == null)
            {
                GcLogger.LogWarning($"[BT][Phase] BT 에셋 로드 실패. key={key}");
                return false;
            }

            MonsterBtRunner.BtTreeSwitchMode switchMode = ResolveTreeSwitchMode(phase.TreeSwitchMode);
            _runner.SetTree(asset, switchMode);
            return true;
        }

        /// <summary>
        /// 지정한 페이즈의 BT를 적용하고 시작 HP를 재설정합니다.
        /// </summary>
        /// <param name="phaseIndex">적용할 페이즈 인덱스입니다.</param>
        /// <returns>적용 성공 시 true를 반환합니다.</returns>
        /// <remarks>
        /// monster_phase를 사용하는 몬스터는 페이즈 전환 시점마다
        /// 시작 HP를 현재 HP뿐 아니라 최대 HP(TotalHp)에도 반영해야
        /// HUD/월드 HP 바의 분모가 현재 페이즈 기준으로 일치합니다.
        /// </remarks>
        private async Task<bool> ApplyPhaseTreeAndStartHpAsync(int phaseIndex)
        {
            bool applied = await ApplyPhaseTreeAsync(phaseIndex);
            if (!applied)
                return false;

            if (_owner == null)
                return false;
            if (!TryGetPhaseRow(phaseIndex, out StruckTableMonsterPhase phase))
                return false;

            long startHp = ComputePhaseStartHp(phase);
            ApplyPhaseHpSnapshot(startHp);
            return true;
        }

        /// <summary>
        /// 현재 페이즈 시작 HP를 최대 HP/현재 HP에 함께 반영합니다.
        /// </summary>
        /// <param name="phaseStartHp">페이즈 테이블 정책으로 계산된 시작 HP입니다.</param>
        /// <remarks>
        /// TotalHp는 CharacterStat 재계산 결과이므로 BaseHp를 먼저 갱신한 뒤
        /// RecalculateStats를 호출해 TotalHp를 갱신합니다.
        /// 그 후 CurrentHp를 최종 TotalHp 범위로 보정하여 반영합니다.
        /// </remarks>
        private void ApplyPhaseHpSnapshot(long phaseStartHp)
        {
            if (_owner == null)
                return;

            long normalizedStartHp = Math.Max(1L, phaseStartHp);
            int normalizedBaseHp = normalizedStartHp > int.MaxValue ? int.MaxValue : (int)normalizedStartHp;

            _owner.BaseHp = normalizedBaseHp;
            _owner.RecalculateStats();

            long resolvedTotalHp = _owner.TotalHp.Value > 0 ? _owner.TotalHp.Value : normalizedStartHp;
            long clampedCurrentHp = Math.Clamp(normalizedStartHp, 0L, resolvedTotalHp);
            _owner.CurrentHp.OnNext(clampedCurrentHp);
        }

        /// <summary>
        /// 문자열 기반 트리 스위치 모드를 파싱합니다.
        /// </summary>
        /// <param name="modeText">테이블 모드 문자열입니다.</param>
        /// <returns>파싱된 트리 스위치 모드입니다.</returns>
        private static MonsterBtRunner.BtTreeSwitchMode ResolveTreeSwitchMode(string modeText)
        {
            if (!string.IsNullOrWhiteSpace(modeText) &&
                Enum.TryParse(modeText, true, out MonsterBtRunner.BtTreeSwitchMode parsed))
            {
                return parsed;
            }

            return MonsterBtRunner.BtTreeSwitchMode.ResetAll;
        }

        /// <summary>
        /// 전환 중 Brain/Control 잠금을 획득합니다.
        /// </summary>
        private void AcquireTransitionLocks()
        {
            if (_owner == null)
                return;

            if (_brainLockToken == null)
                _brainLockToken = _owner.AcquireBrainLock(this);
            if (_controlLockToken == null)
                _controlLockToken = _owner.AcquireControlLock(this);
        }

        /// <summary>
        /// 전환 잠금을 모두 해제합니다.
        /// </summary>
        private void ReleaseTransitionLocks()
        {
            if (_owner != null)
            {
                if (_controlLockToken != null)
                    _owner.ReleaseControlLock(_controlLockToken);
                if (_brainLockToken != null)
                    _owner.ReleaseBrainLock(_brainLockToken);
            }

            _controlLockToken = null;
            _brainLockToken = null;
        }

        /// <summary>
        /// 페이즈 런타임 상태를 초기화합니다.
        /// </summary>
        /// <param name="clearConfiguration">true면 주입된 페이즈/참조까지 초기화합니다.</param>
        private void ResetPhaseRuntimeState(bool clearConfiguration)
        {
            StopAllCoroutines();
            ReleaseTransitionLocks();

            _isTransitionRunning = false;

            if (!clearConfiguration)
                return;

            _isInitialized = false;
            _currentPhaseListIndex = -1;
            _cachedBaseHp = 0;
            _phases.Clear();
            _owner = null;
            _runner = null;
        }
    }
}
