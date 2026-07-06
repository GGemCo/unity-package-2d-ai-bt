using GGemCo2DAiBt;
using GGemCo2DCore;
using GGemCo2DCoreEditor;
using UnityEditor;
using UnityEngine;

namespace GGemCo2DAiBtEditor
{
    /// <summary>
    /// 인트로 씬 설정 툴
    /// </summary>
    public class SceneEditorGameAiBt : DefaultSceneEditorAiBt
    {
        private const string Title = "게임 씬 셋팅하기";
        private GameObject _objGGemCoCore;
        
        [MenuItem(ConfigEditorAiBt.NameToolSettingSceneGame, false, (int)ConfigEditorAiBt.ToolOrdering.SettingSceneGame)]
        public static void ShowWindow()
        {
            GetWindow<SceneEditorGameAiBt>(Title);
        }

        private void OnGUI()
        {
            if (!CheckCurrentLoadedScene(ConfigDefine.SceneNameGame))
            {
                EditorGUILayout.HelpBox($"게임 씬을 불러와 주세요.", MessageType.Error);
            }
            else
            {
                DrawRequiredSection();
            }
        }
        private void DrawRequiredSection()
        {
            HelperEditorUI.OnGUITitle("필수 항목");
            EditorGUILayout.HelpBox($"* {nameof(AiBtPackageManager)} 오브젝트\n", MessageType.Info);
            if (GUILayout.Button("필수 항목 셋팅하기"))
            {
                SetupRequiredObjects();
            }
        }
        /// <summary>
        /// 필수 항목 셋팅
        /// </summary>
        private void SetupRequiredObjects()
        {
            string sceneName = nameof(SceneGame);
            GGemCo2DCore.SceneGame scene = CreateUIComponent.Find(sceneName, ConfigPackageInfo.PackageType.Core)?.GetComponent<SceneGame>();
            if (scene == null) 
            {
                GcLogger.LogError($"{sceneName} 이 없습니다.\nGGemCoTool > AI BT > 설정하기 > 게임 씬 셋팅하기에서 필수 항목 셋팅하기를 실행해주세요.");
                return;
            }
            _objGGemCoCore = GetOrCreateRootPackageGameObject();
            // GGemCo2DControl.ControlPackageManager GameObject 만들기
            GGemCo2DAiBt.AiBtPackageManager aiBtPackageManager =
                CreateOrAddComponent<GGemCo2DAiBt.AiBtPackageManager>(nameof(GGemCo2DAiBt.AiBtPackageManager));
            
            // ControlPackageManager 은 싱글톤으로 활용하고 있어 root 로 이동
            aiBtPackageManager.gameObject.transform.SetParent(null);
            
            // 반드시 SetDirty 처리해야 저장됨
            EditorUtility.SetDirty(scene);
        }
    }
}