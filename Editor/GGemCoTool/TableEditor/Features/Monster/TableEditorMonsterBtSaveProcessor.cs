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
    /// monster 테이블 저장 전/후 후처리를 담당하는 SaveProcessor입니다.
    /// - 저장 전: BtFileName 변경/추가 행의 BT 에셋 존재 검증
    /// - 저장 후: 변경된 행만 Addressables 증분 동기화, 삭제된 행은 Addressables 삭제 후보 처리
    /// </summary>
    public sealed class TableEditorMonsterBtSaveProcessor : ITableEditorSaveProcessor
    {
        private const string HeaderUid = "Uid";
        private const string HeaderBtFileName = "BtFileName";

        private readonly List<StruckTableMonster> _rowsToValidate = new List<StruckTableMonster>();
        private readonly List<StruckTableMonster> _rowsToUpsert = new List<StruckTableMonster>();
        private readonly List<StruckTableMonster> _rowsToRemove = new List<StruckTableMonster>();

        /// <summary>
        /// sound 후처리와 충돌하지 않도록 적당한 후순위로 실행합니다.
        /// </summary>
        public int Order => 20;

        /// <summary>
        /// 현재 저장 컨텍스트가 monster 테이블인지 판별합니다.
        /// </summary>
        /// <param name="context">현재 저장 컨텍스트입니다.</param>
        /// <returns>monster 테이블이면 true를 반환합니다.</returns>
        public bool CanProcess(TableEditorSaveContext context)
        {
            return context != null && context.IsTable(ConfigAddressableTable.Monster);
        }

        /// <summary>
        /// monster 테이블 저장 전에 BtFileName 변경/추가 행만 실제 BT 에셋 존재 여부를 검증합니다.
        /// 에셋이 없으면 예외를 발생시켜 저장을 중단합니다.
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
        /// monster 테이블 저장 완료 후 변경 행만 Addressables에 증분 반영합니다.
        /// 삭제된 행은 Addressables 삭제 후보로 전달됩니다.
        /// </summary>
        /// <param name="context">현재 저장 컨텍스트입니다.</param>
        public void AfterSave(TableEditorSaveContext context)
        {
            try
            {
                if (!ShouldRunChangedOnlyProcessing(context))
                    return;

                if (_rowsToUpsert.Count == 0 && _rowsToRemove.Count == 0)
                    return;

                // Table 저장 후 자동 실행이므로 다이얼로그 없이 동작시킵니다.
                SettingMonsterBt.SyncFromTableDelta(_rowsToUpsert, _rowsToRemove, new SettingMonsterBtOptions
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
            Dictionary<int, StruckTableMonster> currentRowsByUid = BuildRowsByUid(context?.Rows);
            Dictionary<int, StruckTableMonster> previousRowsByUid = LoadPersistedRowsByUid(context?.TableDefinition?.AssetPath);

            foreach (KeyValuePair<int, StruckTableMonster> pair in currentRowsByUid)
            {
                int uid = pair.Key;
                StruckTableMonster current = pair.Value;

                if (!previousRowsByUid.TryGetValue(uid, out StruckTableMonster previous))
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

            foreach (KeyValuePair<int, StruckTableMonster> pair in previousRowsByUid)
            {
                int uid = pair.Key;
                if (currentRowsByUid.ContainsKey(uid))
                    continue;

                StruckTableMonster deleted = pair.Value;
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
        /// <param name="row">추가 후보 monster 행입니다.</param>
        private void AddUpsertAndValidationTargetIfNeeded(StruckTableMonster row)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.BtFileName))
                return;

            _rowsToUpsert.Add(row);
            _rowsToValidate.Add(row);
        }

        /// <summary>
        /// 저장 전 검증 대상 행을 순회하며 BtFileName으로부터 실제 BT 에셋 경로를 해석합니다.
        /// </summary>
        /// <param name="rowsToValidate">검증 대상 monster 행 목록입니다.</param>
        /// <exception cref="InvalidOperationException">에셋이 없는 행이 하나라도 있으면 발생합니다.</exception>
        private static void ValidateBtFileNames(IReadOnlyList<StruckTableMonster> rowsToValidate)
        {
            if (rowsToValidate == null || rowsToValidate.Count == 0)
                return;

            var missingEntries = new List<string>();

            for (int i = 0; i < rowsToValidate.Count; i++)
            {
                StruckTableMonster row = rowsToValidate[i];
                if (!TryNormalizeBtFileNameForTableInput(row?.BtFileName, out string normalizedBtFileName, out string failReason))
                {
                    int invalidUid = row != null ? row.Uid : 0;
                    missingEntries.Add($"- Uid={invalidUid}, BtFileName='{row?.BtFileName}', Reason='{failReason}'");
                    continue;
                }

                string assetPath = ResolveBtAssetPath(normalizedBtFileName);
                MonsterBehaviorTreeAsset asset = AssetDatabase.LoadAssetAtPath<MonsterBehaviorTreeAsset>(assetPath);
                if (asset != null)
                    continue;

                int uid = row != null ? row.Uid : 0;
                missingEntries.Add($"- Uid={uid}, BtFileName='{row?.BtFileName}', AssetPath='{assetPath}'");
            }

            if (missingEntries.Count == 0)
                return;

            const int maxPreviewCount = 12;
            int previewCount = Math.Min(maxPreviewCount, missingEntries.Count);
            List<string> preview = missingEntries.GetRange(0, previewCount);

            string message = "monster 테이블 저장을 중단했습니다.\n"
                             + "BtFileName 컬럼에 입력된 BT 에셋을 찾을 수 없습니다.\n\n"
                             + string.Join("\n", preview);

            if (missingEntries.Count > previewCount)
                message += $"\n... 외 {missingEntries.Count - previewCount}건";

            throw new InvalidOperationException(message);
        }

        /// <summary>
        /// 현재 편집 중인 행 목록을 UID 기준 monster 딕셔너리로 변환합니다.
        /// UID가 없거나 0 이하인 행은 비교 대상에서 제외합니다.
        /// </summary>
        /// <param name="rows">변환할 TableEditor 행 목록입니다.</param>
        /// <returns>UID 기준 monster 행 딕셔너리입니다.</returns>
        private static Dictionary<int, StruckTableMonster> BuildRowsByUid(IReadOnlyList<TableEditorDocumentRow> rows)
        {
            var result = new Dictionary<int, StruckTableMonster>();
            if (rows == null || rows.Count == 0)
                return result;

            for (int i = 0; i < rows.Count; i++)
            {
                if (!TryCreateMonsterRow(rows[i], out StruckTableMonster row))
                    continue;

                result[row.Uid] = row;
            }

            return result;
        }

        /// <summary>
        /// 저장 직전 디스크 기준 monster 테이블을 읽어 UID 기준 딕셔너리를 구성합니다.
        /// </summary>
        /// <param name="assetPath">monster 테이블 에셋 경로입니다.</param>
        /// <returns>디스크 기준 UID monster 딕셔너리입니다.</returns>
        private static Dictionary<int, StruckTableMonster> LoadPersistedRowsByUid(string assetPath)
        {
            var result = new Dictionary<int, StruckTableMonster>();
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
                result[uid] = new StruckTableMonster
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
        /// 테이블 에디터의 단일 행을 monster 행 스냅샷으로 변환합니다.
        /// UID가 유효하지 않으면 false를 반환합니다.
        /// </summary>
        /// <param name="row">변환 대상 TableEditor 행입니다.</param>
        /// <param name="monsterRow">변환된 monster 행입니다.</param>
        /// <returns>변환에 성공하면 true를 반환합니다.</returns>
        private static bool TryCreateMonsterRow(TableEditorDocumentRow row, out StruckTableMonster monsterRow)
        {
            monsterRow = null;
            if (row?.Values == null)
                return false;

            if (!int.TryParse(GetTrimmedValue(row, HeaderUid), out int uid) || uid <= 0)
                return false;

            monsterRow = new StruckTableMonster
            {
                Uid = uid,
                BtFileName = GetTrimmedValue(row, HeaderBtFileName),
            };

            return true;
        }

        /// <summary>
        /// FileName 문자열 변경 여부를 정규화 기준으로 비교합니다.
        /// </summary>
        /// <param name="before">저장 전 행입니다.</param>
        /// <param name="after">저장 후 행입니다.</param>
        /// <returns>BtFileName이 변경되었으면 true를 반환합니다.</returns>
        private static bool IsBtFileNameChanged(StruckTableMonster before, StruckTableMonster after)
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
