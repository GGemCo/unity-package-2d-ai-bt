using GGemCo2DCore;
using UnityEngine;

namespace GGemCo2DAiBt
{
    public class SceneLoadingAiBt : DefaultScene
    {
        private GameLoaderManager _gameLoaderManager;

        private void Awake()
        {
            if (!AddressableLoaderSettings.Instance)
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(ConfigDefine.SceneNamePreIntro);
                return;
            }
        }

        /// <summary>
        /// 오브젝트 활성화 시 로딩 시작 직전 이벤트 훅을 구독합니다.
        /// </summary>
        private void OnEnable()
        {
            // PreIntro 씬/Loading 씬에서 로딩 시작 직전 훅
            GameLoaderManager.BeforeLoadStartInLoadingScene += OnBeforeLoadStartInLoadingScene;
        }

        /// <summary>
        /// 오브젝트 비활성화 시 이벤트 구독을 해제합니다.
        /// </summary>
        private void OnDisable()
        {
            GameLoaderManager.BeforeLoadStartInLoadingScene -= OnBeforeLoadStartInLoadingScene;
        }

        private void OnBeforeLoadStartInLoadingScene(
            GameLoaderManager sender,
            GameLoaderManager.EventArgsBeforeLoadStart e)
        {
            // GcLogger.Log($"GameLoaderManagerControl RegisterSteps");
            // 설정 스크립터블 오브젝트 
            var addrSettings = Object.FindFirstObjectByType<AddressableLoaderSettingsAiBt>() ??
                               new GameObject("AddressableLoaderSettingsAiBt")
                                   .AddComponent<AddressableLoaderSettingsAiBt>();
            var step = new AddressableTaskStep(
                id: "aibt.settings",
                order: 250,
                localizedKey: LocalizationConstants.Keys.Loading.TextTypeSettings(),
                startTask: () => addrSettings.LoadAllSettingsAsync(),
                getProgress: () => addrSettings.GetLoadProgress()
            );
            sender.Register(step);
        }
    }
}
