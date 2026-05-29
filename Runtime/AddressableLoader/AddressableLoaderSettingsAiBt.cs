using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GGemCo2DCore;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace GGemCo2DAiBt
{
    /// <summary>
    /// GGemCo Settings 불러오기
    /// </summary>
    public class AddressableLoaderSettingsAiBt : MonoBehaviour
    {
        public static AddressableLoaderSettingsAiBt Instance { get; private set; }

        [HideInInspector] public GGemCoAiBtSettings aiBtSettings;

        public delegate void DelegateLoadSettings(GGemCoAiBtSettings aiBtSettings);
        public event DelegateLoadSettings OnLoadSettings;
        
        private readonly HashSet<AsyncOperationHandle> _activeHandles = new HashSet<AsyncOperationHandle>();
        private float _loadProgress;

        private void Awake()
        {
            _loadProgress = 0f;
            if (!Instance)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            ReleaseAll();
        }

        /// <summary>
        /// 모든 로드된 리소스를 해제합니다.
        /// </summary>
        private void ReleaseAll()
        {
            AddressableLoaderController.ReleaseByHandles(_activeHandles);
        }
        /// <summary>
        /// 모든 설정 파일을 Addressables에서 로드
        /// </summary>
        public async Task LoadAllSettingsAsync()
        {
            try
            {
                // 여러 개의 설정을 병렬적으로 로드
                var taskAiBt = LoadSettingsAsync<GGemCoAiBtSettings>(ConfigAddressableSettingAiBt.AiBtSettings.Key);

                // 모든 작업이 완료될 때까지 대기
                await Task.WhenAll(taskAiBt);

                // 결과 저장
                aiBtSettings = taskAiBt.Result;

                // 이벤트 호출
                OnLoadSettings?.Invoke(aiBtSettings);
            }
            catch (Exception ex)
            {
                GcLogger.LogError($"설정 로딩 중 오류 발생: {ex.Message}");
            }
        }

        /// <summary>
        /// 개발용 Settings Override를 먼저 확인한 뒤, 없으면 Addressables에서 서비스용 Settings를 로드합니다.
        /// </summary>
        /// <typeparam name="T">로드할 ScriptableObject 타입입니다.</typeparam>
        /// <param name="key">서비스용 Settings Addressables Key입니다.</param>
        /// <returns>개발용 또는 서비스용 Settings 에셋입니다.</returns>
        private async Task<T> LoadSettingsAsync<T>(string key) where T : ScriptableObject
        {
            // 에디터 Play Mode에서 작업자별 개발용 Settings가 등록되어 있으면 서비스용 Addressables보다 먼저 사용합니다.
            if (SettingsRuntimeResolver.TryGetOverride(key, out T overrideSettings))
            {
                return overrideSettings;
            }

            // 키가 Addressables에 등록되어 있는지 확인
            var locationsHandle = Addressables.LoadResourceLocationsAsync(key);
            await locationsHandle.Task;

            if (!locationsHandle.Status.Equals(AsyncOperationStatus.Succeeded) || locationsHandle.Result.Count == 0)
            {
                GcLogger.LogError($"[AddressableSettingsLoader] '{key}' 가 Addressables에 등록되지 않았습니다. '{key}' 를 생성한 후 {ConfigDefine.NameSDK}Tool > 기본 셋팅하기 메뉴를 열고 Addressable 추가하기 버튼을 클릭해주세요.");
                Addressables.Release(locationsHandle);
                return null;
            }

            // 설정 로드
            AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(key);
            T asset = await handle.Task;

            // 핸들 해제
            Addressables.Release(locationsHandle);
            return asset;
        }
        public float GetLoadProgress() => _loadProgress;
    }
}
