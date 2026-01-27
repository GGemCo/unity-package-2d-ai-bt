using System.Threading.Tasks;
using GGemCo2DCore;
using UnityEngine;
#if GGEMCO_2D_SKILL
using GGemCo2DSkill;
#endif

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
#if GGEMCO_2D_SKILL
            var skillExecutor = ch.gameObject.GetComponent<SkillExecutor>();
            if (skillExecutor == null) ch.gameObject.AddComponent<SkillExecutor>();
            
            var monsterSkillDriverAdapter = ch.gameObject.GetComponent<MonsterSkillDriverAdapter>();
            if (monsterSkillDriverAdapter == null)
            {
                monsterSkillDriverAdapter = ch.gameObject.AddComponent<MonsterSkillDriverAdapter>();
            }
            monsterSkillDriverAdapter.SetSkillExecutor(skillExecutor);
#endif
        }

        private Task OnCharacterSpawnedAsync(CharacterBase ch)
        {
            if (ch == null || !ch.IsMonster())
                return Task.CompletedTask;

            // Runner는 CharacterManager 스폰 이벤트에서 붙이는 것을 원칙으로 하되,
            // Hook 경로에서도 방어적으로 누락 시 추가한다.
            var runner = ch.GetComponent<MonsterBtRunner>();
            if (runner == null)
                runner = ch.gameObject.AddComponent<MonsterBtRunner>();

            var info = TableLoaderManager.Instance.TableMonster.GetDataByUid(ch.uid);
            if (GcLogger.IsNull(info, $"몬스터 테이블에 정보가 없습니다. uid: {ch.uid}")) return Task.CompletedTask;
            if (string.IsNullOrWhiteSpace(info.BtFileName)) return Task.CompletedTask;

            return AddressableLoaderMonsterBt.LoadAndApplyAsync(ConfigAddressableKeyAiBt.GetMonsterBt(info.BtFileName), runner);
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