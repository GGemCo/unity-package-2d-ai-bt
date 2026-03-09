using GGemCo2DAiBt;
using GGemCo2DCore;
using GGemCo2DCoreEditor;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace GGemCo2DAiBtEditor
{
    /// <summary>
    /// 로딩 씬에서 필요로 하는 설정용 ScriptableObject들을 Common Addressables 그룹에 등록하는 에디터 설정 클래스입니다.
    /// </summary>
    /// <remarks>
    /// ConfigAddressableSettingAiBt.NeedLoadInLoadingScene 목록을 기반으로 키/경로/라벨을 일괄 등록합니다.
    /// </remarks>
    public class SettingScriptableObjectAiBt : DefaultAddressable
    {
        /// <summary>
        /// 버튼에 표시되는 텍스트(기능 실행 제목)입니다.
        /// </summary>
        private const string Title = "설정 ScriptableObject 추가하기";

        /// <summary>
        /// UI 레이아웃(버튼 폭/높이 등) 정보를 제공하는 부모 에디터 윈도우 참조입니다.
        /// </summary>
        private readonly AddressableEditorAiBt _addressableEditorAiBt;

        /// <summary>
        /// 설정 ScriptableObject Addressables 등록 UI를 초기화합니다.
        /// </summary>
        /// <param name="addressableEditorAiBtWindow">버튼 크기 등 UI 정보를 참조할 에디터 윈도우입니다.</param>
        public SettingScriptableObjectAiBt(AddressableEditorAiBt addressableEditorAiBtWindow)
        {
            _addressableEditorAiBt = addressableEditorAiBtWindow;

            // NOTE: 공용 설정은 Common 그룹으로 등록합니다.
            targetGroupName = ConfigAddressableGroupName.Common;
        }

        /// <summary>
        /// 에디터 윈도우에 표시될 UI를 그립니다.
        /// </summary>
        /// <remarks>
        /// 버튼 클릭 시 Setup을 호출하여 Addressables 등록을 수행합니다.
        /// </remarks>
        public void OnGUI()
        {
            // Common.OnGUITitle(Title);

            if (GUILayout.Button(Title, GUILayout.Width(_addressableEditorAiBt.buttonWidth), GUILayout.Height(_addressableEditorAiBt.buttonHeight)))
            {
                try
                {
                    Setup();
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                    EditorUtility.DisplayDialog(
                        Title,
                        "설정 스크립터블 오브젝트 Addressable 설정 중 오류가 발생했습니다.\n자세한 내용은 콘솔 로그를 확인해주세요.",
                        "OK");
                }
            }
        }

        /// <summary>
        /// 설정 ScriptableObject들을 Addressables에 등록하고 저장합니다.
        /// </summary>
        /// <param name="ctx">
        /// 배치/자동화 실행 컨텍스트입니다. null이면 완료 시 AssetDatabase 저장 및 다이얼로그를 표시합니다.
        /// </param>
        /// <remarks>
        /// 동작 개요:
        /// - AddressableAssetSettings가 없으면 생성합니다.
        /// - Common 그룹을 가져오거나 생성합니다.
        /// - 로딩 씬에서 필요로 하는 설정 목록을 순회하며 키/경로/라벨로 엔트리를 추가합니다.
        /// </remarks>
        public void Setup(EditorSetupContext ctx = null)
        {
            // AddressableSettings 가져오기 (없으면 생성)
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (!settings)
            {
                HelperLog.Warn("Addressable 설정을 찾을 수 없습니다. 새로 생성합니다.", ctx);
                settings = CreateAddressableSettings();
            }

            // 그룹 가져오기 또는 생성
            AddressableAssetGroup group = GetOrCreateGroup(settings, targetGroupName);
            if (!group)
            {
                HelperLog.Error($"'{targetGroupName}' 그룹을 설정할 수 없습니다.", ctx);
                return;
            }

            // 로딩 씬에서 즉시 필요로 하는 설정 ScriptableObject들을 일괄 등록
            foreach (var addressableAssetInfo in ConfigAddressableSettingAiBt.NeedLoadInLoadingScene)
            {
                Add(settings, group, addressableAssetInfo.Key, addressableAssetInfo.Path, addressableAssetInfo.Label);
            }

            // 설정 저장
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
            if (ctx != null)
            {
                HelperLog.Info("[Addressable] Setting 스크립터블 오브젝트 설정 완료", ctx);
            }
            else
            {
                AssetDatabase.SaveAssets();
                EditorUtility.DisplayDialog(Title, "[Addressable] Setting 스크립터블 오브젝트 설정 완료", "OK");
            }
        }
    }
}
