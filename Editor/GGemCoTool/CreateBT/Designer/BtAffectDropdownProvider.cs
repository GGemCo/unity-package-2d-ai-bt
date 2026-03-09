#if UNITY_EDITOR
#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GGemCo2DCore;
using GGemCo2DCoreEditor;

namespace GGemCo2DAiBtEditor
{
    /// <summary>
    /// BT 디자이너에서 스킬 UID 파라미터 입력을 지원하기 위한 옵션(드롭다운) 제공자.
    /// </summary>
    internal static class BtAffectDropdownProvider
    {
        private static List<SearchableDropdownUtility.Option<int>> _cached = new List<SearchableDropdownUtility.Option<int>>(512);
        private static int _cachedHash;

        /// <summary>
        /// affect 테이블을 파싱하여 드롭다운 옵션을 반환합니다.
        /// </summary>
        public static IReadOnlyList<SearchableDropdownUtility.Option<int>> GetOptions(bool forceReload = false)
        {
            // 테이블 텍스트를 직접 읽어 간단히 파싱합니다.
            // (Affect 패키지의 TableAffect 클래스 의존을 피하기 위함)
            string path = $"{ConfigAddressablePath.Tables}/affect";
            string? content = AssetDatabaseLoaderManager.LoadFileText(path);
            if (string.IsNullOrEmpty(content))
            {
                // 옵션이 없더라도 드롭다운이 깨지지 않도록 최소 항목 제공
                _cached.Clear();
                _cached.Add(new SearchableDropdownUtility.Option<int>("0", "(affect table not found)", 0));
                _cachedHash = 0;
                return _cached;
            }

            int hash = content.GetHashCode();
            if (!forceReload && _cachedHash == hash && _cached.Count > 0)
                return _cached;

            _cachedHash = hash;
            _cached = ParseAffectTable(content);
            if (_cached.Count == 0)
                _cached.Add(new SearchableDropdownUtility.Option<int>("0", "(no affects)", 0));

            return _cached;
        }

        public static int FindIndexByUid(IReadOnlyList<SearchableDropdownUtility.Option<int>>? options, int uid)
        {
            if (options == null) return -1;
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Data == uid)
                    return i;
            }
            return -1;
        }

        public static string FormatSelected(IReadOnlyList<SearchableDropdownUtility.Option<int>>? options, int selectedIndex, int fallbackUid)
        {
            if (options != null && selectedIndex >= 0 && selectedIndex < options.Count)
                return options[selectedIndex].ToString();

            return $"{fallbackUid.ToString(CultureInfo.InvariantCulture)}  |  (unknown)";
        }

        private static List<SearchableDropdownUtility.Option<int>> ParseAffectTable(string content)
        {
            var list = new List<SearchableDropdownUtility.Option<int>>(512);

            string[] lines = content
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("#", StringComparison.Ordinal))
                .ToArray();

            if (lines.Length <= 1)
                return list;

            char delimiter = lines[0].Contains('\t') ? '\t' : ',';
            string[] header = lines[0].Split(delimiter);

            int uidIndex = FindColumnIndex(header, "Uid");
            int nameIndex = FindColumnIndex(header, "Memo");

            if (uidIndex < 0) uidIndex = 0;
            if (nameIndex < 0) nameIndex = Math.Min(1, header.Length - 1);

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;
                string[] cols = line.Split(delimiter);
                if (cols.Length <= uidIndex) continue;

                if (!int.TryParse(cols[uidIndex].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int uid))
                    continue;

                string name = (nameIndex >= 0 && nameIndex < cols.Length) ? cols[nameIndex].Trim() : string.Empty;
                if (string.IsNullOrEmpty(name))
                    name = "(no name)";

                list.Add(new SearchableDropdownUtility.Option<int>(uid.ToString(CultureInfo.InvariantCulture), name, uid));
            }

            // UID 순 정렬
            list.Sort((a, b) => a.Data.CompareTo(b.Data));
            return list;
        }

        private static int FindColumnIndex(string[] header, params string[] candidates)
        {
            for (int i = 0; i < header.Length; i++)
            {
                string h = header[i].Trim();
                for (int c = 0; c < candidates.Length; c++)
                {
                    if (string.Equals(h, candidates[c], StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
            return -1;
        }
    }
}
#endif