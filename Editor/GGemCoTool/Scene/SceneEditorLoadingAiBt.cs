using GGemCo2DCore;
using GGemCo2DCoreEditor;
using UnityEditor;
using UnityEngine;

namespace GGemCo2DAiBtEditor
{
    public class SceneEditorLoadingAiBt : DefaultSceneEditorAiBt
    {
        /// <summary>
        /// 에디터 윈도우의 표시 제목입니다.
        /// </summary>
        private const string Title = "로딩 씬 셋팅하기";

        /// <summary>
        /// 패키지 루트(공용) 오브젝트 참조입니다. 필요 시 생성됩니다.
        /// </summary>
        private GameObject _objGGemCoCore;

        /// <summary>
        /// 메뉴 항목에서 호출되어 로딩 씬 셋업 창을 엽니다.
        /// </summary>
        [MenuItem(ConfigEditorAiBt.NameToolSettingSceneLoading, false, (int)ConfigEditorAiBt.ToolOrdering.SettingSceneLoading)]
        public static void ShowWindow()
        {
            GetWindow<SceneEditorLoadingAiBt>(Title);
        }

        /// <summary>
        /// 에디터 윈도우 UI를 그립니다.
        /// </summary>
        private void OnGUI()
        {
            if (!CheckCurrentLoadedScene(ConfigDefine.SceneNameLoading))
            {
                EditorGUILayout.HelpBox("로딩 씬을 불러와 주세요.", MessageType.Error);
            }
            else
            {
                DrawRequiredSection();
            }
        }

        /// <summary>
        /// 로딩 씬에서 반드시 존재해야 하는 필수 항목 셋업 섹션을 그립니다.
        /// </summary>
        private void DrawRequiredSection()
        {
            HelperEditorUI.OnGUITitle("필수 항목");
            EditorGUILayout.HelpBox("* SceneLoadingSkill 오브젝트\n", MessageType.Info);

            if (GUILayout.Button("필수 항목 셋팅하기"))
            {
                SetupRequiredObjects();
            }
        }

        public void SetupRequiredObjects(EditorSetupContext ctx = null)
        {
            string sceneName = nameof(SceneLoading);

            // Core 패키지에서 SceneLoading 루트 오브젝트를 찾습니다.
            GGemCo2DCore.SceneLoading scene =
                CreateUIComponent.Find(sceneName, ConfigPackageInfo.PackageType.Core)?.GetComponent<SceneLoading>();

            if (scene == null)
            {
                HelperLog.Error(
                    $"[{nameof(SceneEditorLoadingAiBt)}] {sceneName} 이 없습니다.\n" +
                    "GGemCoTool > 설정하기 > 로딩 씬 셋팅하기에서 필수 항목 셋팅하기를 실행해주세요.",
                    ctx);
                return;
            }

            _objGGemCoCore = GetOrCreateRootPackageGameObject();

            // NOTE: CreateOrAddComponent 구현에 따라 동일 이름 오브젝트가 있으면 재사용될 수 있습니다.
            GGemCo2DAiBt.SceneLoadingAiBt sceneLoading =
                CreateOrAddComponent<GGemCo2DAiBt.SceneLoadingAiBt>(nameof(GGemCo2DAiBt.SceneLoadingAiBt));

            HelperLog.Info($"[{nameof(SceneEditorLoadingAiBt)}] 로딩 씬 필수 셋업 완료", ctx);

            // 반드시 SetDirty 처리해야 저장됨
            EditorUtility.SetDirty(scene);
        }
    }
}
