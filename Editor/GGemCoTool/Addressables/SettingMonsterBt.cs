using System;
using System.Collections.Generic;
using System.IO;
using GGemCo2DAiBt;
using GGemCo2DCore;
using GGemCo2DCoreEditor;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using TableLoaderManager = GGemCo2DCoreEditor.TableLoaderManager;

namespace GGemCo2DAiBtEditor
{
    /// <summary>
    /// 몬스터 BT Addressables 동기화 옵션입니다.
    /// 수동 실행/자동 실행에 따라 확인 다이얼로그, 완료 다이얼로그, SaveAssets 여부를 제어합니다.
    /// </summary>
    public sealed class SettingMonsterBtOptions
    {
        public bool ShowConfirmDialog = true;
        public bool ShowCompletedDialog = true;
        public bool SaveAssets = true;
        public EditorSetupContext Context;
    }

    /// <summary>
    /// monster / monster_phase 테이블의 BtFileName 기준으로 몬스터 BT Addressables를 구성합니다.
    /// </summary>
    public class SettingMonsterBt : DefaultAddressable
    {
        private const string Title = "몬스터 BT 추가하기";
        private readonly AddressableEditorAiBt _addressableEditorAiBt;

        /// <summary>
        /// 몬스터 BT Addressables 설정 도우미를 생성합니다.
        /// </summary>
        /// <param name="addressableEditorAiBtWindow">Addressables 설정 윈도우 인스턴스입니다.</param>
        public SettingMonsterBt(AddressableEditorAiBt addressableEditorAiBtWindow)
        {
            _addressableEditorAiBt = addressableEditorAiBtWindow;
            targetGroupName = ConfigAddressableGroupNameAiBt.MonsterBt;
        }

        /// <summary>
        /// 몬스터 BT Addressables 수동 실행 버튼 UI를 렌더링합니다.
        /// </summary>
        public void OnGUI()
        {
            if (!File.Exists($"{ConfigAddressableTable.TableMonster.Path}"))
            {
                EditorGUILayout.HelpBox($"{ConfigAddressableTable.Monster} 테이블이 없습니다.", MessageType.Info);
                return;
            }

            if (GUILayout.Button(Title, GUILayout.Width(_addressableEditorAiBt.buttonWidth), GUILayout.Height(_addressableEditorAiBt.buttonHeight)))
            {
                try
                {
                    Setup();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    EditorUtility.DisplayDialog(Title, "몬스터 BT Addressable 설정 중 오류가 발생했습니다.\n자세한 내용은 콘솔 로그를 확인해주세요.", "OK");
                }
            }
        }

        /// <summary>
        /// monster / monster_phase 테이블을 기준으로 몬스터 BT 그룹을 전체 재구성합니다.
        /// 기존 그룹 엔트리는 모두 제거하고, 현재 참조되는 BT 파일만 다시 등록합니다.
        /// </summary>
        /// <param name="ctx">자동 설정 실행 컨텍스트입니다.</param>
        public void Setup(EditorSetupContext ctx = null)
        {
            if (ctx == null)
            {
                bool result = EditorUtility.DisplayDialog(TextDisplayDialogTitle, TextDisplayDialogMessage, "네", "아니요");
                if (!result)
                    return;
            }

            var options = new SettingMonsterBtOptions
            {
                Context = ctx,
                ShowConfirmDialog = false,
                ShowCompletedDialog = false,
                SaveAssets = true,
            };

            if (!TryPrepareSyncEnvironment(options, out _, out SettingMonsterBt helper, out AddressableAssetSettings settings, out AddressableAssetGroup group))
                return;

            // 그룹 엔트리 전체 초기화(스키마/설정은 유지) 후 현재 참조 BT만 재등록합니다.
            ClearGroupEntries(settings, group);

            HashSet<string> referencedBtNames = CollectReferencedBtFileNamesFromAllTables(forceReload: true);
            foreach (string btFileName in referencedBtNames)
                UpsertBtEntry(helper, settings, group, btFileName);

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

        /// <summary>
        /// monster 테이블 BtFileName 변경분만 Addressables에 증분 반영합니다.
        /// - rowsToUpsert: 등록/갱신 대상
        /// - rowsToRemove: 삭제 후보(기존 값)
        /// </summary>
        /// <param name="rowsToUpsert">등록/갱신 대상 몬스터 행 목록입니다.</param>
        /// <param name="rowsToRemove">삭제 후보 몬스터 행 목록입니다.</param>
        /// <param name="options">동기화 옵션입니다. null이면 기본 옵션을 사용합니다.</param>
        public static void SyncFromTableDelta(
            IReadOnlyList<StruckTableMonster> rowsToUpsert,
            IReadOnlyList<StruckTableMonster> rowsToRemove,
            SettingMonsterBtOptions options = null)
        {
            IReadOnlyList<string> btFileNamesToUpsert = ExtractNormalizedBtFileNamesFromMonsterRows(rowsToUpsert);
            IReadOnlyList<string> btFileNamesToRemove = ExtractNormalizedBtFileNamesFromMonsterRows(rowsToRemove);
            SyncFromBtFileNameDelta(btFileNamesToUpsert, btFileNamesToRemove, options);
        }

        /// <summary>
        /// BtFileName 문자열 변경분만 Addressables에 증분 반영합니다.
        /// monster / monster_phase 공통 후처리에서 함께 사용하는 진입점입니다.
        /// </summary>
        /// <param name="btFileNamesToUpsert">등록/갱신 후보 BtFileName 목록입니다.</param>
        /// <param name="btFileNamesToRemove">삭제 후보 BtFileName 목록입니다.</param>
        /// <param name="options">동기화 옵션입니다. null이면 기본 옵션을 사용합니다.</param>
        public static void SyncFromBtFileNameDelta(
            IReadOnlyList<string> btFileNamesToUpsert,
            IReadOnlyList<string> btFileNamesToRemove,
            SettingMonsterBtOptions options = null)
        {
            options ??= new SettingMonsterBtOptions();

            bool hasUpsert = btFileNamesToUpsert != null && btFileNamesToUpsert.Count > 0;
            bool hasRemove = btFileNamesToRemove != null && btFileNamesToRemove.Count > 0;
            if (!hasUpsert && !hasRemove)
                return;

            if (options.ShowConfirmDialog)
            {
                bool result = EditorUtility.DisplayDialog(
                    TextDisplayDialogTitle,
                    "몬스터 BT Addressables 변경분을 동기화합니다.\n진행하시겠습니까?",
                    "네",
                    "아니요");
                if (!result)
                    return;
            }

            if (!TryPrepareSyncEnvironment(options, out EditorSetupContext ctx, out SettingMonsterBt helper, out AddressableAssetSettings settings, out AddressableAssetGroup group))
                return;

            // 삭제 판단은 저장 완료 후 최신 테이블 상태(monster + monster_phase) 기준으로 수행합니다.
            HashSet<string> currentReferences = CollectReferencedBtFileNamesFromAllTables(forceReload: true);

            int removedCount = 0;
            if (hasRemove)
            {
                var uniqueRemoveCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < btFileNamesToRemove.Count; i++)
                {
                    string btFileName = ConfigAddressablePathAiBt.MonsterBt.NormalizeRelativePath(btFileNamesToRemove[i]);
                    if (string.IsNullOrWhiteSpace(btFileName))
                        continue;
                    if (!uniqueRemoveCandidates.Add(btFileName))
                        continue;
                    if (currentReferences.Contains(btFileName))
                        continue;

                    if (TryRemoveBtEntry(settings, group, btFileName))
                        removedCount++;
                }
            }

            int upsertCount = 0;
            if (hasUpsert)
            {
                var uniqueUpsertCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < btFileNamesToUpsert.Count; i++)
                {
                    string btFileName = ConfigAddressablePathAiBt.MonsterBt.NormalizeRelativePath(btFileNamesToUpsert[i]);
                    if (string.IsNullOrWhiteSpace(btFileName))
                        continue;
                    if (!uniqueUpsertCandidates.Add(btFileName))
                        continue;

                    AddressableAssetEntry entry = UpsertBtEntry(helper, settings, group, btFileName);
                    if (entry != null)
                        upsertCount++;
                }
            }

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true);
            if (options.SaveAssets)
                AssetDatabase.SaveAssets();

            string completedMessage = $"[Addressable] 몬스터 BT 변경분 동기화 완료 (등록/갱신: {upsertCount}, 삭제: {removedCount})";
            if (ctx != null)
            {
                HelperLog.Info(completedMessage, ctx);
            }
            else if (options.ShowCompletedDialog)
            {
                EditorUtility.DisplayDialog(Title, completedMessage, "OK");
            }
        }

        /// <summary>
        /// monster 행 목록에서 BtFileName을 정규화해 중복 없는 문자열 목록으로 변환합니다.
        /// </summary>
        /// <param name="rows">monster 행 목록입니다.</param>
        /// <returns>정규화된 BtFileName 목록입니다.</returns>
        private static IReadOnlyList<string> ExtractNormalizedBtFileNamesFromMonsterRows(IReadOnlyList<StruckTableMonster> rows)
        {
            if (rows == null || rows.Count == 0)
                return Array.Empty<string>();

            var result = new List<string>(rows.Count);
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rows.Count; i++)
            {
                string btFileName = ConfigAddressablePathAiBt.MonsterBt.NormalizeRelativePath(rows[i]?.BtFileName);
                if (string.IsNullOrWhiteSpace(btFileName))
                    continue;
                if (!unique.Add(btFileName))
                    continue;

                result.Add(btFileName);
            }

            return result;
        }

        /// <summary>
        /// 증분/전체 동기화에서 공통으로 사용하는 Addressables 환경(settings/group)을 준비합니다.
        /// </summary>
        /// <param name="options">동기화 옵션입니다.</param>
        /// <param name="ctx">실행 컨텍스트입니다.</param>
        /// <param name="helper">몬스터 BT 설정 헬퍼입니다.</param>
        /// <param name="settings">Addressables 설정 객체입니다.</param>
        /// <param name="group">몬스터 BT 대상 그룹입니다.</param>
        /// <returns>준비에 성공하면 true를 반환합니다.</returns>
        private static bool TryPrepareSyncEnvironment(
            SettingMonsterBtOptions options,
            out EditorSetupContext ctx,
            out SettingMonsterBt helper,
            out AddressableAssetSettings settings,
            out AddressableAssetGroup group)
        {
            ctx = options?.Context;
            helper = new SettingMonsterBt(null);
            settings = AddressableAssetSettingsDefaultObject.Settings;
            group = null;

            if (!settings)
            {
                HelperLog.Warn("Addressable 설정을 찾을 수 없습니다. 새로 생성합니다.", ctx);
                settings = helper.CreateAddressableSettings();
            }

            group = helper.GetOrCreateGroup(settings, helper.targetGroupName);
            if (!group)
            {
                HelperLog.Error($"'{helper.targetGroupName}' 그룹을 설정할 수 없습니다.", ctx);
                return false;
            }

            return true;
        }

        /// <summary>
        /// BT 파일명을 Addressables 엔트리로 등록/갱신합니다.
        /// </summary>
        /// <param name="helper">몬스터 BT 설정 헬퍼입니다.</param>
        /// <param name="settings">Addressables 설정 객체입니다.</param>
        /// <param name="group">대상 그룹입니다.</param>
        /// <param name="btFileName">BT 파일명(확장자 제외 권장)입니다.</param>
        /// <returns>등록/갱신된 엔트리이며 실패 시 null을 반환합니다.</returns>
        private static AddressableAssetEntry UpsertBtEntry(
            SettingMonsterBt helper,
            AddressableAssetSettings settings,
            AddressableAssetGroup group,
            string btFileName)
        {
            if (helper == null || settings == null || group == null)
                return null;

            string normalizedBtFileName = ConfigAddressablePathAiBt.MonsterBt.NormalizeRelativePath(btFileName);
            if (string.IsNullOrWhiteSpace(normalizedBtFileName))
                return null;

            string key = BuildAddressKey(normalizedBtFileName);
            if (string.IsNullOrWhiteSpace(key))
                return null;

            string assetPath = ResolveBtAssetPath(normalizedBtFileName);
            return helper.Add(settings, group, key, assetPath, string.Empty);
        }

        /// <summary>
        /// BT 파일명에 해당하는 Addressables 엔트리를 제거합니다.
        /// 우선 address 기준으로 제거를 시도하고, 실패하면 GUID 기반 제거를 시도합니다.
        /// </summary>
        /// <param name="settings">Addressables 설정 객체입니다.</param>
        /// <param name="group">삭제 대상 그룹입니다.</param>
        /// <param name="btFileName">삭제할 BT 파일명입니다.</param>
        /// <returns>삭제 성공 시 true를 반환합니다.</returns>
        private static bool TryRemoveBtEntry(
            AddressableAssetSettings settings,
            AddressableAssetGroup group,
            string btFileName)
        {
            if (settings == null || group == null)
                return false;

            string normalizedBtFileName = ConfigAddressablePathAiBt.MonsterBt.NormalizeRelativePath(btFileName);
            if (string.IsNullOrWhiteSpace(normalizedBtFileName))
                return false;

            string key = BuildAddressKey(normalizedBtFileName);
            if (string.IsNullOrWhiteSpace(key))
                return false;

            if (TryRemoveBtEntryByAddress(settings, group, key))
                return true;

            string guid = AssetDatabase.AssetPathToGUID(ResolveBtAssetPath(normalizedBtFileName));
            if (string.IsNullOrWhiteSpace(guid))
                return false;

            AddressableAssetEntry entry = settings.FindAssetEntry(guid);
            if (entry == null || entry.parentGroup != group)
                return false;

            return settings.RemoveAssetEntry(guid);
        }

        /// <summary>
        /// 그룹 내 address 값이 일치하는 엔트리를 찾아 제거합니다.
        /// </summary>
        /// <param name="settings">Addressables 설정 객체입니다.</param>
        /// <param name="group">검색 대상 그룹입니다.</param>
        /// <param name="address">삭제할 Addressables address입니다.</param>
        /// <returns>삭제 성공 시 true를 반환합니다.</returns>
        private static bool TryRemoveBtEntryByAddress(
            AddressableAssetSettings settings,
            AddressableAssetGroup group,
            string address)
        {
            if (settings == null || group == null || string.IsNullOrWhiteSpace(address))
                return false;

            foreach (AddressableAssetEntry entry in group.entries)
            {
                if (entry == null)
                    continue;

                if (!string.Equals(entry.address, address, StringComparison.OrdinalIgnoreCase))
                    continue;

                return settings.RemoveAssetEntry(entry.guid);
            }
            
            return false;
        }

        /// <summary>
        /// monster / monster_phase 테이블에서 현재 참조되는 BT 파일명 집합을 수집합니다.
        /// </summary>
        /// <param name="forceReload">테이블을 강제 리로드할지 여부입니다.</param>
        /// <returns>중복 제거된 BT 파일명 집합입니다.</returns>
        private static HashSet<string> CollectReferencedBtFileNamesFromAllTables(bool forceReload)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Dictionary<int, StruckTableMonster> monsterRows = TableLoaderManager.LoadMonsterTable(forceReload)?.GetDatas();
            if (monsterRows != null)
            {
                foreach (KeyValuePair<int, StruckTableMonster> pair in monsterRows)
                {
                    StruckTableMonster row = pair.Value;
                    if (row == null || row.Uid <= 0)
                        continue;

                    string btFileName = ConfigAddressablePathAiBt.MonsterBt.NormalizeRelativePath(row.BtFileName);
                    if (string.IsNullOrWhiteSpace(btFileName))
                        continue;

                    result.Add(btFileName);
                }
            }

            Dictionary<int, StruckTableMonsterPhase> phaseRows = TableLoaderManager.LoadMonsterPhaseTable(forceReload)?.GetDatas();
            if (phaseRows != null)
            {
                foreach (KeyValuePair<int, StruckTableMonsterPhase> pair in phaseRows)
                {
                    StruckTableMonsterPhase row = pair.Value;
                    if (row == null || row.Uid <= 0)
                        continue;

                    string btFileName = ConfigAddressablePathAiBt.MonsterBt.NormalizeRelativePath(row.BtFileName);
                    if (string.IsNullOrWhiteSpace(btFileName))
                        continue;

                    result.Add(btFileName);
                }
            }

            return result;
        }

        /// <summary>
        /// BT 파일명을 Addressables key 규칙으로 변환합니다.
        /// </summary>
        /// <param name="btFileName">BT 파일명입니다.</param>
        /// <returns>Addressables key 문자열입니다.</returns>
        private static string BuildAddressKey(string btFileName)
        {
            return ConfigAddressableKeyAiBt.GetMonsterBt(btFileName);
        }

        /// <summary>
        /// BT 파일명으로 Addressables 대상 에셋 경로를 생성합니다.
        /// </summary>
        /// <param name="btFileName">BT 파일명입니다.</param>
        /// <returns>Assets 기준 .asset 경로입니다.</returns>
        private static string ResolveBtAssetPath(string btFileName)
        {
            return ConfigAddressablePathAiBt.MonsterBt.BuildAssetPath(btFileName);
        }
    }
}
