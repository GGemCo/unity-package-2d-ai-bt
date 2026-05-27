using System;
using System.Collections.Generic;
using System.IO;
using GGemCo2DAiBt;
using GGemCo2DCore;
using GGemCo2DCoreEditor;
using UnityEditor;

namespace GGemCo2DAiBtEditor
{
    /// <summary>
    /// monster_phase 테이블 저장 전/후 후처리를 담당하는 SaveProcessor입니다.
    /// - 저장 전: BtFileName 변경/추가 행의 BT 에셋 존재 및 경로 정책 검증
    /// - 저장 후: 변경된 BtFileName만 Addressables 증분 동기화
    /// </summary>
    public sealed class TableEditorMonsterPhaseBtSaveProcessor : ITableEditorSaveProcessor
    {
        private const string HeaderUid = "Uid";
        private const string HeaderBtFileName = "BtFileName";

        private readonly List<StruckTableMonsterPhase> _rowsToValidate = new List<StruckTableMonsterPhase>();
        private readonly List<StruckTableMonsterPhase> _rowsToUpsert = new List<StruckTableMonsterPhase>();
        private readonly List<StruckTableMonsterPhase> _rowsToRemove = new List<StruckTableMonsterPhase>();

        /// <summary>
        /// monster 후처리 이후에 이어서 수행되도록 순서를 설정합니다.
        /// </summary>
        public int Order => 21;

        /// <summary>
        /// 현재 저장 컨텍스트가 monster_phase 테이블인지 판별합니다.
        /// </summary>
        /// <param name="context">현재 저장 컨텍스트입니다.</param>
        /// <returns>monster_phase 테이블이면 true를 반환합니다.</returns>
        public bool CanProcess(TableEditorSaveContext context)
        {
            return context != null && context.IsTable(ConfigAddressableTable.MonsterPhase);
        }

        /// <summary>
        /// monster_phase 테이블 저장 전에 BtFileName 변경/추가 행만 검증합니다.
        /// </summary>
        /// <param name="context">현재 저장 컨텍스트입니다.</param>
        public void BeforeSave(TableEditorSaveContext context)
        {
            ClearPendingDelta();

            if (!ShouldRunChangedOnlyProcessing(context))
                return;

            BuildPendingDelta(context);
            ValidateBtFileNames(_rowsToValidate);
        }

        /// <summary>
        /// monster_phase 테이블 저장 완료 후 변경된 BtFileName만 Addressables에 증분 반영합니다.
        /// </summary>
        /// <param name="context">현재 저장 컨텍스트입니다.</param>
        public void AfterSave(TableEditorSaveContext context)
        {
            try
            {
                if (!ShouldRunChangedOnlyProcessing(context))
                    return;

                IReadOnlyList<string> btFileNamesToUpsert = ExtractNormalizedBtFileNames(_rowsToUpsert);
                IReadOnlyList<string> btFileNamesToRemove = ExtractNormalizedBtFileNames(_rowsToRemove);
                if (btFileNamesToUpsert.Count == 0 && btFileNamesToRemove.Count == 0)
                    return;

                // Table 저장 후 자동 실행이므로 다이얼로그 없이 동작시킵니다.
                SettingMonsterBt.SyncFromBtFileNameDelta(btFileNamesToUpsert, btFileNamesToRemove, new SettingMonsterBtOptions
                {
                    ShowConfirmDialog = false,
                    ShowCompletedDialog = false,
                    SaveAssets = true,
                });
            }
            finally
            {
                ClearPendingDelta();
            }
        }

        /// <summary>
        /// 현재 저장 요청이 실제 문서 변경을 포함하는지 확인합니다.
        /// </summary>
        /// <param name="context">현재 저장 컨텍스트입니다.</param>
        /// <returns>변경 기반 후처리를 수행해야 하면 true를 반환합니다.</returns>
        private static bool ShouldRunChangedOnlyProcessing(TableEditorSaveContext context)
        {
            return context != null && context.HasDocumentChanges;
        }

        /// <summary>
        /// 저장 전/후에 사용할 증분 변경 목록(검증/등록/삭제 대상)을 계산합니다.
        /// </summary>
        /// <param name="context">현재 저장 컨텍스트입니다.</param>
        private void BuildPendingDelta(TableEditorSaveContext context)
        {
            Dictionary<int, StruckTableMonsterPhase> currentRowsByUid = BuildRowsByUid(context?.Rows);
            Dictionary<int, StruckTableMonsterPhase> previousRowsByUid = LoadPersistedRowsByUid(context?.TableDefinition?.AssetPath);

            foreach (KeyValuePair<int, StruckTableMonsterPhase> pair in currentRowsByUid)
            {
                int uid = pair.Key;
                StruckTableMonsterPhase current = pair.Value;

                if (!previousRowsByUid.TryGetValue(uid, out StruckTableMonsterPhase previous))
                {
                    AddUpsertAndValidationTargetIfNeeded(current);
                    continue;
                }

                if (!IsBtFileNameChanged(previous, current))
                    continue;

                if (!string.IsNullOrWhiteSpace(previous.BtFileName))
                    _rowsToRemove.Add(previous);

                AddUpsertAndValidationTargetIfNeeded(current);
            }

            foreach (KeyValuePair<int, StruckTableMonsterPhase> pair in previousRowsByUid)
            {
                int uid = pair.Key;
                if (currentRowsByUid.ContainsKey(uid))
                    continue;

                StruckTableMonsterPhase deleted = pair.Value;
                if (deleted == null || string.IsNullOrWhiteSpace(deleted.BtFileName))
                    continue;

                _rowsToRemove.Add(deleted);
            }
        }

        /// <summary>
        /// 현재 저장 요청에서 계산한 증분 변경 목록을 초기화합니다.
        /// </summary>
        private void ClearPendingDelta()
        {
            _rowsToValidate.Clear();
            _rowsToUpsert.Clear();
            _rowsToRemove.Clear();
        }

        /// <summary>
        /// BtFileName이 유효한 행을 Addressables 등록/파일 검증 대상으로 추가합니다.
        /// </summary>
        /// <param name="row">추가 후보 monster_phase 행입니다.</param>
        private void AddUpsertAndValidationTargetIfNeeded(StruckTableMonsterPhase row)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.BtFileName))
                return;

            _rowsToUpsert.Add(row);
            _rowsToValidate.Add(row);
        }

        /// <summary>
        /// 저장 전 검증 대상 행을 순회하며 BtFileName 정책 및 실제 BT 에셋 존재를 검증합니다.
        /// </summary>
        /// <param name="rowsToValidate">검증 대상 monster_phase 행 목록입니다.</param>
        /// <exception cref="InvalidOperationException">검증 실패 행이 하나라도 있으면 발생합니다.</exception>
        private static void ValidateBtFileNames(IReadOnlyList<StruckTableMonsterPhase> rowsToValidate)
        {
            if (rowsToValidate == null || rowsToValidate.Count == 0)
                return;

            var invalidEntries = new List<string>();

            for (int i = 0; i < rowsToValidate.Count; i++)
            {
                StruckTableMonsterPhase row = rowsToValidate[i];
                if (!TryNormalizeBtFileNameForTableInput(row?.BtFileName, out string normalizedBtFileName, out string failReason))
                {
                    int invalidUid = row != null ? row.Uid : 0;
                    invalidEntries.Add($"- Uid={invalidUid}, BtFileName='{row?.BtFileName}', Reason='{failReason}'");
                    continue;
                }

                string assetPath = ResolveBtAssetPath(normalizedBtFileName);
                MonsterBehaviorTreeAsset asset = AssetDatabase.LoadAssetAtPath<MonsterBehaviorTreeAsset>(assetPath);
                if (asset != null)
                    continue;

                int uid = row != null ? row.Uid : 0;
                invalidEntries.Add($"- Uid={uid}, BtFileName='{row?.BtFileName}', AssetPath='{assetPath}'");
            }

            if (invalidEntries.Count == 0)
                return;

            const int maxPreviewCount = 12;
            int previewCount = Math.Min(maxPreviewCount, invalidEntries.Count);
            List<string> preview = invalidEntries.GetRange(0, previewCount);

            string message = "monster_phase 테이블 저장을 중단했습니다.\n"
                             + "BtFileName 컬럼에 입력된 BT 에셋 경로가 유효하지 않습니다.\n\n"
                             + string.Join("\n", preview);

            if (invalidEntries.Count > previewCount)
                message += $"\n... 외 {invalidEntries.Count - previewCount}건";

            throw new InvalidOperationException(message);
        }

        /// <summary>
        /// 현재 편집 중인 행 목록을 UID 기준 monster_phase 딕셔너리로 변환합니다.
        /// UID가 없거나 0 이하인 행은 비교 대상에서 제외합니다.
        /// </summary>
        /// <param name="rows">변환할 TableEditor 행 목록입니다.</param>
        /// <returns>UID 기준 monster_phase 행 딕셔너리입니다.</returns>
        private static Dictionary<int, StruckTableMonsterPhase> BuildRowsByUid(IReadOnlyList<TableEditorDocumentRow> rows)
        {
            var result = new Dictionary<int, StruckTableMonsterPhase>();
            if (rows == null || rows.Count == 0)
                return result;

            for (int i = 0; i < rows.Count; i++)
            {
                if (!TryCreateMonsterPhaseRow(rows[i], out StruckTableMonsterPhase row))
                    continue;

                result[row.Uid] = row;
            }

            return result;
        }

        /// <summary>
        /// 저장 직전 디스크 기준 monster_phase 테이블을 읽어 UID 기준 딕셔너리를 구성합니다.
        /// </summary>
        /// <param name="assetPath">monster_phase 테이블 에셋 경로입니다.</param>
        /// <returns>디스크 기준 UID monster_phase 딕셔너리입니다.</returns>
        private static Dictionary<int, StruckTableMonsterPhase> LoadPersistedRowsByUid(string assetPath)
        {
            var result = new Dictionary<int, StruckTableMonsterPhase>();
            if (string.IsNullOrWhiteSpace(assetPath))
                return result;

            string fullPath = ResolveAbsolutePath(assetPath);
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath))
                return result;

            string content = File.ReadAllText(fullPath);
            if (string.IsNullOrWhiteSpace(content))
                return result;

            string[] lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (lines.Length == 0)
                return result;

            string headerLine = lines[0].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(headerLine))
                return result;

            string[] headers = headerLine.Split('\t');
            int uidIndex = FindHeaderIndex(headers, HeaderUid);
            int btFileNameIndex = FindHeaderIndex(headers, HeaderBtFileName);
            if (uidIndex < 0 || btFileNameIndex < 0)
                return result;

            for (int i = 1; i < lines.Length; i++)
            {
                string rawLine = lines[i].TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;
                if (rawLine.StartsWith("#", StringComparison.Ordinal))
                    continue;

                string[] values = rawLine.Split('\t');
                string uidRaw = GetValueByIndex(values, uidIndex).Trim();
                if (!int.TryParse(uidRaw, out int uid) || uid <= 0)
                    continue;

                string btFileName = GetValueByIndex(values, btFileNameIndex).Trim();
                result[uid] = new StruckTableMonsterPhase
                {
                    Uid = uid,
                    BtFileName = btFileName,
                };
            }

            return result;
        }

        /// <summary>
        /// 상대 에셋 경로(Assets/...)를 절대 경로로 변환합니다.
        /// </summary>
        /// <param name="assetPath">상대 또는 절대 경로 문자열입니다.</param>
        /// <returns>절대 경로 문자열입니다.</returns>
        private static string ResolveAbsolutePath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return string.Empty;

            string normalized = assetPath.Trim().Trim('"').Replace('\\', '/');
            return Path.IsPathRooted(normalized)
                ? normalized
                : Path.GetFullPath(normalized);
        }

        /// <summary>
        /// 헤더 배열에서 지정된 헤더명의 인덱스를 찾습니다.
        /// </summary>
        /// <param name="headers">헤더 문자열 배열입니다.</param>
        /// <param name="headerName">검색할 헤더명입니다.</param>
        /// <returns>인덱스(없으면 -1)를 반환합니다.</returns>
        private static int FindHeaderIndex(IReadOnlyList<string> headers, string headerName)
        {
            if (headers == null || headers.Count == 0 || string.IsNullOrWhiteSpace(headerName))
                return -1;

            for (int i = 0; i < headers.Count; i++)
            {
                if (string.Equals(headers[i], headerName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// 인덱스가 유효하면 배열 값을 반환하고, 아니면 빈 문자열을 반환합니다.
        /// </summary>
        /// <param name="values">값 배열입니다.</param>
        /// <param name="index">조회 인덱스입니다.</param>
        /// <returns>조회된 값 또는 빈 문자열입니다.</returns>
        private static string GetValueByIndex(IReadOnlyList<string> values, int index)
        {
            if (values == null || index < 0 || index >= values.Count)
                return string.Empty;

            return values[index] ?? string.Empty;
        }

        /// <summary>
        /// 테이블 에디터의 단일 행을 monster_phase 행 스냅샷으로 변환합니다.
        /// UID가 유효하지 않으면 false를 반환합니다.
        /// </summary>
        /// <param name="row">변환 대상 TableEditor 행입니다.</param>
        /// <param name="phaseRow">변환된 monster_phase 행입니다.</param>
        /// <returns>변환에 성공하면 true를 반환합니다.</returns>
        private static bool TryCreateMonsterPhaseRow(TableEditorDocumentRow row, out StruckTableMonsterPhase phaseRow)
        {
            phaseRow = null;
            if (row?.Values == null)
                return false;

            if (!int.TryParse(GetTrimmedValue(row, HeaderUid), out int uid) || uid <= 0)
                return false;

            phaseRow = new StruckTableMonsterPhase
            {
                Uid = uid,
                BtFileName = GetTrimmedValue(row, HeaderBtFileName),
            };

            return true;
        }

        /// <summary>
        /// BtFileName 문자열 변경 여부를 정규화 기준으로 비교합니다.
        /// </summary>
        /// <param name="before">저장 전 행입니다.</param>
        /// <param name="after">저장 후 행입니다.</param>
        /// <returns>BtFileName이 변경되었으면 true를 반환합니다.</returns>
        private static bool IsBtFileNameChanged(StruckTableMonsterPhase before, StruckTableMonsterPhase after)
        {
            string beforeValue = ConfigAddressablePathAiBt.MonsterBt.NormalizeRelativePath(before?.BtFileName);
            string afterValue = ConfigAddressablePathAiBt.MonsterBt.NormalizeRelativePath(after?.BtFileName);
            return !string.Equals(beforeValue, afterValue, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 행 데이터에서 헤더 값을 안전하게 꺼내고 공백을 제거합니다.
        /// </summary>
        /// <param name="row">대상 행입니다.</param>
        /// <param name="headerName">조회할 헤더명입니다.</param>
        /// <returns>정리된 문자열 값입니다. 없으면 빈 문자열을 반환합니다.</returns>
        private static string GetTrimmedValue(TableEditorDocumentRow row, string headerName)
        {
            if (row == null || row.Values == null || string.IsNullOrWhiteSpace(headerName))
                return string.Empty;

            return row.Values.TryGetValue(headerName, out string value)
                ? (value ?? string.Empty).Trim()
                : string.Empty;
        }

        /// <summary>
        /// BtFileName 입력값이 테이블 정책(루트 하위 상대 경로)에 맞는지 검증하고 정규화합니다.
        /// </summary>
        /// <param name="rawFileName">원본 파일명 문자열입니다.</param>
        /// <param name="normalizedRelativePath">정규화된 상대 경로입니다.</param>
        /// <param name="failReason">검증 실패 사유입니다.</param>
        /// <returns>검증/정규화 성공 시 true를 반환합니다.</returns>
        private static bool TryNormalizeBtFileNameForTableInput(
            string rawFileName,
            out string normalizedRelativePath,
            out string failReason)
        {
            return ConfigAddressablePathAiBt.MonsterBt.TryNormalizeTableRelativePath(
                rawFileName,
                out normalizedRelativePath,
                out failReason);
        }

        /// <summary>
        /// phase 행 목록에서 BtFileName을 정규화해 중복 없는 문자열 목록으로 변환합니다.
        /// </summary>
        /// <param name="rows">phase 행 목록입니다.</param>
        /// <returns>정규화된 BtFileName 목록입니다.</returns>
        private static IReadOnlyList<string> ExtractNormalizedBtFileNames(IReadOnlyList<StruckTableMonsterPhase> rows)
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
