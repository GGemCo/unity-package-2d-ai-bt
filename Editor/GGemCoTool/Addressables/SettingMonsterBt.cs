using System.Collections.Generic;
using System.IO;
using GGemCo2DAiBt;
using GGemCo2DCore;
using GGemCo2DCoreEditor;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace GGemCo2DAiBtEditor
{
    public class SettingMonsterBt : DefaultAddressable
    {
        private const string Title = "몬스터 BT 추가하기";
        private readonly AddressableEditorAiBt _addressableEditorAiBt;
        
        public SettingMonsterBt(AddressableEditorAiBt addressableEditorAiBtWindow)
        {
            _addressableEditorAiBt = addressableEditorAiBtWindow;
            targetGroupName = ConfigAddressableGroupNameAiBt.MonsterBt;
        }
        public void OnGUI()
        {
            if (!File.Exists($"{ConfigAddressableTable.TableMonster.Path}"))
            {
                EditorGUILayout.HelpBox($"{ConfigAddressableTable.Monster} 테이블이 없습니다.", MessageType.Info);
            }
            else
            {
                if (GUILayout.Button(Title, GUILayout.Width(_addressableEditorAiBt.buttonWidth), GUILayout.Height(_addressableEditorAiBt.buttonHeight)))
                {
                    try
                    {
                        Setup();
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogException(e);
                        EditorUtility.DisplayDialog(Title, "스킬 Addressable 설정 중 오류가 발생했습니다.\n자세한 내용은 콘솔 로그를 확인해주세요.", "OK");
                    }
                }
            }
        }
        /// <summary>
        /// Addressable 설정하기
        /// </summary>
        public void Setup(EditorSetupContext ctx = null)
        {
            if (ctx == null)
            {
                bool result = EditorUtility.DisplayDialog(TextDisplayDialogTitle, TextDisplayDialogMessage, "네", "아니요");
                if (!result) return;
            }
            
            Dictionary<int, StruckTableMonster> dictionary = GGemCo2DCoreEditor.TableLoaderManager.LoadMonsterTable().GetDatas();
            
            // AddressableSettings 가져오기 (없으면 생성)
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (!settings)
            {
                HelperLog.Warn("Addressable 설정을 찾을 수 없습니다. 새로 생성합니다.", ctx);
                settings = CreateAddressableSettings();
            }

            // GGemCo_Tables 그룹 가져오기 또는 생성
            AddressableAssetGroup group = GetOrCreateGroup(settings, targetGroupName);
            if (!group)
            {
                HelperLog.Error($"'{targetGroupName}' 그룹을 설정할 수 없습니다.", ctx);
                return;
            }
            
            // 그룹 엔트리 전체 초기화 (스키마/설정은 유지)
            ClearGroupEntries(settings, group);

            if (group)
            {
                // foreach 문을 사용하여 딕셔너리 내용을 출력
                foreach (KeyValuePair<int, StruckTableMonster> outerPair in dictionary)
                {
                    var info = outerPair.Value;
                    if (info.Uid <= 0) continue;
                    if (string.IsNullOrEmpty(info.BtFileName)) continue;
                
                    string key = $"{ConfigAddressableKeyAiBt.MonsterBt}_{info.BtFileName}";
                    string assetPath = $"{ConfigAddressablePathAiBt.MonsterBt.Root}/{info.BtFileName}.asset";
                    string label = "";
                
                    Add(settings, group, key, assetPath, label);
                }
            }

            // 설정 저장
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
            
            if (ctx != null)
            {
                HelperLog.Info("[Addressable] 몬스터 BT 설정 완료", ctx);
            }
            else
            {
                AssetDatabase.SaveAssets();
                EditorUtility.DisplayDialog(Title, "[Addressable] 몬스터 BT 완료", "OK");
            }
        }
    }
}