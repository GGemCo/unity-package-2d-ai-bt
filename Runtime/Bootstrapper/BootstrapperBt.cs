using System.Threading.Tasks;
using GGemCo2DCore;
using UnityEngine;
using GGemCo2DSkill;
using GGemCo2DSkillEditor;

namespace GGemCo2DAiBt
{
    /// <summary>
    /// Core의 캐릭터 생성 이벤트를 구독하여 ControlBase 를 자동 부착
    /// </summary>
    public class BootstrapperBt : MonoBehaviour
    {
        [SerializeField] private bool addIfMissing = true;

        private void OnEnable()
        {
            CharacterManager.OnCharacterSpawned   += OnCharacterSpawned;
            CharacterManager.OnCharacterDestroyed += OnCharacterDestroyed;

            // MapLoadCharacters가 스폰 완료 대기를 위해 호출하는 비동기 Hook.
            CharacterSpawnHooks.OnCharacterSpawnedAsync += OnCharacterSpawnedAsync;
            CharacterSpawnHooks.OnMapUnload += OnMapUnload;
        }

        private void OnDisable()
        {
            CharacterManager.OnCharacterSpawned   -= OnCharacterSpawned;
            CharacterManager.OnCharacterDestroyed -= OnCharacterDestroyed;

            CharacterSpawnHooks.OnCharacterSpawnedAsync -= OnCharacterSpawnedAsync;
            CharacterSpawnHooks.OnMapUnload -= OnMapUnload;
        }

        private void OnCharacterSpawned(CharacterBase ch)
        {
            if (!addIfMissing) return;
# if GGEMCO_USE_SPINE
            
#else

#endif
            // 스킬 컴포넌트 추가하기
            var skillExecutor = ch.gameObject.GetComponent<SkillExecutor>();
            if (skillExecutor == null) ch.gameObject.AddComponent<SkillExecutor>();
            
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
                
                if (AddressableLoaderSettingsAiBt.Instance.aiBtSettings.EnableDebug)
                {
                    var skillDamageAreaGizmo = ch.gameObject.GetComponent<SkillDamageAreaGizmo>();
                    if (skillDamageAreaGizmo == null)
                        ch.gameObject.AddComponent<SkillDamageAreaGizmo>();
                }
            }
        }

        private Task OnCharacterSpawnedAsync(CharacterBase ch)
        {
            if (ch == null || !ch.IsMonster())
                return Task.CompletedTask;

            var info = TableLoaderManager.Instance.TableMonster.GetDataByUid(ch.uid);
            if (GcLogger.IsNull(info, $"몬스터 테이블에 정보가 없습니다. uid: {ch.uid}"))
                return Task.CompletedTask;

            // BT가 없으면: Runner를 비활성(또는 미부착)하고 레거시 Brain을 사용한다.
            if (string.IsNullOrWhiteSpace(info.BtFileName))
            {
                var existingRunner = ch.GetComponent<MonsterBtRunner>();
                if (existingRunner != null)
                    existingRunner.enabled = false;

                if (addIfMissing)
                {
                    var legacy = ch.GetComponent<MonsterLegacyBrain>();
                    if (legacy == null) legacy = ch.gameObject.AddComponent<MonsterLegacyBrain>();
                    legacy.enabled = true;
                }

                return Task.CompletedTask;
            }

            // BT가 있으면: 레거시 Brain은 억제하고 Runner를 부착/활성한다.
            var legacyBrain = ch.GetComponent<MonsterLegacyBrain>();
            if (legacyBrain != null) legacyBrain.enabled = false;

            var runner = ch.GetComponent<MonsterBtRunner>();
            if (runner == null)
                runner = ch.gameObject.AddComponent<MonsterBtRunner>();
            runner.enabled = true;

            return AddressableLoaderMonsterBt.LoadAndApplyAsync(
                ConfigAddressableKeyAiBt.GetMonsterBt(info.BtFileName),
                runner);
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