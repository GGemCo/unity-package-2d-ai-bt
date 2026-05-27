using System;
using GGemCo2DCore;

namespace GGemCo2DAiBt
{
    /// <summary>
    /// AI BT 패키지의 Addressables 경로 규칙을 정의합니다.
    /// </summary>
    public static class ConfigAddressablePathAiBt
    {
        /// <summary>
        /// 몬스터 BT 에셋 경로 규칙입니다.
        /// </summary>
        public static class MonsterBt
        {
            /// <summary>BT 에셋 확장자입니다.</summary>
            public const string AssetExtension = ".asset";

            /// <summary>Assets/{SDK}/DataAddressable/MonsterBt</summary>
            public static string Root => ConfigAddressablePath.Combine(ConfigAddressablePath.Root, "MonsterBt");

            /// <summary>
            /// 테이블 입력값을 "MonsterBt 루트 하위 상대 경로" 규칙으로 검증하고 정규화합니다.
            /// 예: Common/AirGolem
            /// </summary>
            /// <param name="rawInput">테이블의 BtFileName 원본 입력값입니다.</param>
            /// <param name="normalizedRelativePath">정규화된 상대 경로입니다.</param>
            /// <param name="failReason">검증 실패 사유입니다.</param>
            /// <returns>검증/정규화 성공 시 true를 반환합니다.</returns>
            public static bool TryNormalizeTableRelativePath(
                string rawInput,
                out string normalizedRelativePath,
                out string failReason)
            {
                normalizedRelativePath = string.Empty;
                failReason = string.Empty;

                string normalizedRaw = NormalizeRawPath(rawInput);
                if (string.IsNullOrWhiteSpace(normalizedRaw))
                {
                    failReason = "BtFileName이 비어 있습니다.";
                    return false;
                }

                if (string.Equals(normalizedRaw, Root, StringComparison.OrdinalIgnoreCase) ||
                    normalizedRaw.StartsWith(Root + "/", StringComparison.OrdinalIgnoreCase))
                {
                    failReason = $"공통 루트 경로('{Root}/')를 제외한 하위 경로만 입력해야 합니다. 예: Common/AirGolem";
                    return false;
                }

                if (normalizedRaw.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    failReason = $"Assets 경로 전체가 아닌 '{Root}/' 하위 상대 경로를 입력해야 합니다. 예: Common/AirGolem";
                    return false;
                }

                if (normalizedRaw.StartsWith("/", StringComparison.Ordinal) || normalizedRaw.Contains(":"))
                {
                    failReason = "절대 경로는 허용되지 않습니다. MonsterBt 하위 상대 경로를 입력해주세요.";
                    return false;
                }

                string normalized = NormalizeRelativePath(rawInput);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    failReason = "BtFileName 정규화 결과가 비어 있습니다.";
                    return false;
                }

                if (HasTraversalSegment(normalized))
                {
                    failReason = "상위 폴더(..) 역참조는 허용되지 않습니다.";
                    return false;
                }

                normalizedRelativePath = normalized;
                return true;
            }

            /// <summary>
            /// BT 파일명 입력값을 런타임/에디터 공통 규칙으로 정규화합니다.
            /// - 역슬래시를 슬래시로 통일
            /// - MonsterBt 루트 접두어 제거
            /// - .asset 확장자 제거
            /// </summary>
            /// <param name="rawInput">정규화할 원본 입력값입니다.</param>
            /// <returns>정규화된 상대 경로입니다. 유효한 값이 없으면 빈 문자열을 반환합니다.</returns>
            public static string NormalizeRelativePath(string rawInput)
            {
                string normalized = NormalizeRawPath(rawInput);
                if (string.IsNullOrWhiteSpace(normalized))
                    return string.Empty;

                if (string.Equals(normalized, Root, StringComparison.OrdinalIgnoreCase))
                    return string.Empty;

                if (normalized.StartsWith(Root + "/", StringComparison.OrdinalIgnoreCase))
                    normalized = normalized.Substring(Root.Length + 1);

                normalized = normalized.TrimStart('/');
                if (normalized.EndsWith(AssetExtension, StringComparison.OrdinalIgnoreCase))
                    normalized = normalized.Substring(0, normalized.Length - AssetExtension.Length);

                return normalized.Trim();
            }

            /// <summary>
            /// BT 상대 경로를 Addressables 대상 에셋 경로로 변환합니다.
            /// </summary>
            /// <param name="rawInputOrRelativePath">원본 입력값 또는 상대 경로입니다.</param>
            /// <returns>Assets 기준 .asset 경로입니다. 경로 생성에 실패하면 빈 문자열을 반환합니다.</returns>
            public static string BuildAssetPath(string rawInputOrRelativePath)
            {
                string relativePath = NormalizeRelativePath(rawInputOrRelativePath);
                if (string.IsNullOrWhiteSpace(relativePath))
                    return string.Empty;

                return ConfigAddressablePath.Combine(Root, relativePath + AssetExtension);
            }

            /// <summary>
            /// 원본 경로 문자열에서 공백/따옴표 제거 및 슬래시 정규화를 수행합니다.
            /// </summary>
            /// <param name="rawInput">정규화할 원본 문자열입니다.</param>
            /// <returns>정규화된 경로 문자열입니다.</returns>
            private static string NormalizeRawPath(string rawInput)
            {
                if (string.IsNullOrWhiteSpace(rawInput))
                    return string.Empty;

                string trimmed = rawInput.Trim().Trim('"');
                return ConfigAddressablePath.EnsureForwardSlashes(trimmed);
            }

            /// <summary>
            /// 상위 폴더 역참조("..") 세그먼트 포함 여부를 검사합니다.
            /// </summary>
            /// <param name="relativePath">검사할 상대 경로입니다.</param>
            /// <returns>역참조 세그먼트를 포함하면 true를 반환합니다.</returns>
            private static bool HasTraversalSegment(string relativePath)
            {
                if (string.IsNullOrWhiteSpace(relativePath))
                    return false;

                string[] segments = relativePath.Split('/');
                for (int i = 0; i < segments.Length; i++)
                {
                    if (string.Equals(segments[i], "..", StringComparison.Ordinal))
                        return true;
                }

                return false;
            }
        }
    }
}
