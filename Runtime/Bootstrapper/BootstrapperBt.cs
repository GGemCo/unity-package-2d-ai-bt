using System.Collections.Generic;
using System.Threading.Tasks;
using GGemCo2DCore;
using GGemCo2DSkill;
using UnityEngine;

namespace GGemCo2DAiBt
{
    /// <summary>
    /// Core의 캐릭터 생성 이벤트를 구독하여 스킬/Brain 초기화를 수행합니다.
    /// </summary>
    public class BootstrapperBt : MonoBehaviour
    {
        [SerializeField] private bool addIfMissing = true;

        private void OnEnable()
        {
            CharacterManager.OnCharacterSpawned += OnCharacterSpawned;
            CharacterManager.OnCharacterDestroyed += OnCharacterDestroyed;

            // MapLoadCharacters가 스폰 완료 대기를 위해 호출하는 비동기 Hook.
            CharacterSpawnHooks.OnCharacterSpawnedAsync += OnCharacterSpawnedAsync;
            CharacterSpawnHooks.OnMapUnload += OnMapUnload;
        }

        private void OnDisable()
        {
            CharacterManager.OnCharacterSpawned -= OnCharacterSpawned;
            CharacterManager.OnCharacterDestroyed -= OnCharacterDestroyed;

            CharacterSpawnHooks.OnCharacterSpawnedAsync -= OnCharacterSpawnedAsync;
            CharacterSpawnHooks.OnMapUnload -= OnMapUnload;
        }

        private void OnCharacterSpawned(CharacterBase ch)
        {
            if (!addIfMissing)
                return;
#if GGEMCO_USE_SPINE

#else

#endif
            // 스킬 컴포넌트 추가하기
            var skillExecutor = ch.gameObject.GetComponent<SkillExecutor>();
            if (skillExecutor == null)
                skillExecutor = ch.gameObject.AddComponent<SkillExecutor>();

            // 캐릭터 유형에 맞는 스킬 드라이버를 연결합니다.
            if (ch.IsPlayer())
            {
                var playerSkillDriverAdapter = ch.gameObject.GetComponent<PlayerSkillDriverAdapter>();
                if (playerSkillDriverAdapter == null)
                    playerSkillDriverAdapter = ch.gameObject.AddComponent<PlayerSkillDriverAdapter>();

                playerSkillDriverAdapter.SetSkillExecutor(skillExecutor);
            }
            else if (ch.IsMonster())
            {
                var monsterSkillDriverAdapter = ch.gameObject.GetComponent<MonsterSkillDriverAdapter>();
                if (monsterSkillDriverAdapter == null)
                    monsterSkillDriverAdapter = ch.gameObject.AddComponent<MonsterSkillDriverAdapter>();

                monsterSkillDriverAdapter.SetSkillExecutor(skillExecutor);
            }
        }

        /// <summary>
        /// 몬스터 스폰 직후 BT 초기화를 수행합니다.
        /// </summary>
        /// <param name="ch">스폰된 캐릭터입니다.</param>
        /// <returns>비동기 초기화 작업입니다.</returns>
        private Task OnCharacterSpawnedAsync(CharacterBase ch)
        {
            if (ch == null || !ch.IsMonster())
                return Task.CompletedTask;

            if (TableLoaderManager.Instance == null)
                return Task.CompletedTask;

            var info = TableLoaderManager.Instance.TableMonster.GetDataByUid(ch.uid);
            if (GcLogger.IsNull(info, $"몬스터 테이블에 정보가 없습니다. uid: {ch.uid}"))
                return Task.CompletedTask;

            var monster = ch as Monster;
            if (monster == null)
                return Task.CompletedTask;

            if (TryGetPhaseRows(ch.uid, out IReadOnlyList<StruckTableMonsterPhase> phaseRows))
                return SetupPhaseBtAsync(monster, info, phaseRows);

            DisablePhaseOrchestrator(monster.gameObject);
            return SetupSingleBtOrLegacyAsync(monster.gameObject, info);
        }

        /// <summary>
        /// 몬스터 UID에 대한 페이즈 행 목록을 조회합니다.
        /// </summary>
        /// <param name="monsterUid">조회할 몬스터 UID입니다.</param>
        /// <param name="phaseRows">조회된 페이즈 목록입니다.</param>
        /// <returns>유효한 페이즈 목록이 있으면 true를 반환합니다.</returns>
        private static bool TryGetPhaseRows(int monsterUid, out IReadOnlyList<StruckTableMonsterPhase> phaseRows)
        {
            phaseRows = TableLoaderManager.Instance.GetMonsterPhaseDataByMonsterUid(monsterUid, false);
            return phaseRows != null && phaseRows.Count > 0;
        }

        /// <summary>
        /// 페이즈 BT를 초기화하고 첫 페이즈를 적용합니다.
        /// </summary>
        /// <param name="monster">대상 몬스터입니다.</param>
        /// <param name="info">몬스터 기본 테이블 정보입니다.</param>
        /// <param name="phaseRows">페이즈 테이블 행 목록입니다.</param>
        /// <returns>비동기 초기화 작업입니다.</returns>
        private async Task SetupPhaseBtAsync(Monster monster, StruckTableMonster info, IReadOnlyList<StruckTableMonsterPhase> phaseRows)
        {
            GameObject owner = monster.gameObject;
            DisableLegacyBrain(owner);

            var runner = owner.GetComponent<MonsterBtRunner>();
            if (runner == null)
                runner = owner.AddComponent<MonsterBtRunner>();
            runner.enabled = true;

            var orchestrator = owner.GetComponent<MonsterBtPhaseOrchestrator>();
            if (orchestrator == null)
                orchestrator = owner.AddComponent<MonsterBtPhaseOrchestrator>();
            orchestrator.enabled = true;

            bool initialized = await orchestrator.InitializeAndApplyAsync(monster, runner, phaseRows);
            if (initialized)
                return;

            GcLogger.LogWarning($"[BT][Phase] 페이즈 초기화 실패로 단일 BT/레거시 경로로 폴백합니다. monsterUid={monster.uid}");
            orchestrator.enabled = false;
            await SetupSingleBtOrLegacyAsync(owner, info);
        }

        /// <summary>
        /// 단일 BT 또는 레거시 Brain 경로를 설정합니다.
        /// </summary>
        /// <param name="owner">대상 몬스터 오브젝트입니다.</param>
        /// <param name="info">몬스터 기본 테이블 정보입니다.</param>
        /// <returns>비동기 설정 작업입니다.</returns>
        private async Task SetupSingleBtOrLegacyAsync(GameObject owner, StruckTableMonster info)
        {
            var runner = owner.GetComponent<MonsterBtRunner>();
            // BtFileName은 MonsterBt 루트 하위 상대 경로 규칙으로 정규화해 키를 생성합니다.
            // (예: Common/AirGolem)
            string btRelativePath = ConfigAddressablePathAiBt.MonsterBt.NormalizeRelativePath(info?.BtFileName);
            string btKey = ConfigAddressableKeyAiBt.GetMonsterBt(btRelativePath);

            // BT가 없으면 Runner를 비활성하고 레거시 Brain을 사용합니다.
            if (string.IsNullOrWhiteSpace(btRelativePath) || string.IsNullOrWhiteSpace(btKey))
            {
                if (runner != null)
                    runner.enabled = false;

                if (addIfMissing)
                {
                    var legacy = owner.GetComponent<MonsterLegacyBrain>();
                    if (legacy == null)
                        legacy = owner.AddComponent<MonsterLegacyBrain>();
                    legacy.enabled = true;
                }

                return;
            }

            DisableLegacyBrain(owner);

            if (runner == null)
                runner = owner.AddComponent<MonsterBtRunner>();
            runner.enabled = true;

            await AddressableLoaderMonsterBt.LoadAndApplyAsync(
                btKey,
                runner);
        }

        /// <summary>
        /// 레거시 Brain을 비활성합니다.
        /// </summary>
        /// <param name="owner">대상 몬스터 오브젝트입니다.</param>
        private static void DisableLegacyBrain(GameObject owner)
        {
            var legacyBrain = owner.GetComponent<MonsterLegacyBrain>();
            if (legacyBrain != null)
                legacyBrain.enabled = false;
        }

        /// <summary>
        /// 페이즈 오케스트레이터를 비활성합니다.
        /// </summary>
        /// <param name="owner">대상 몬스터 오브젝트입니다.</param>
        private static void DisablePhaseOrchestrator(GameObject owner)
        {
            var orchestrator = owner.GetComponent<MonsterBtPhaseOrchestrator>();
            if (orchestrator != null)
                orchestrator.enabled = false;
        }

        private void OnMapUnload()
        {
            // 정책: 맵 언로드 시 Addressables 핸들 해제
            AddressableLoaderMonsterBt.ReleaseAll();
        }

        private void OnCharacterDestroyed(CharacterBase ch)
        {
            // 필요 시 언바인드/풀 반환/로그 등 처리
        }
    }
}
