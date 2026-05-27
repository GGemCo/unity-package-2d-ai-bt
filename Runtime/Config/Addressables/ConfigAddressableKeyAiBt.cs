using GGemCo2DCore;

namespace GGemCo2DAiBt
{
    /// <summary>
    /// AI BT 패키지 Addressables 키 규칙입니다.
    /// </summary>
    public static class ConfigAddressableKeyAiBt
    {
        /// <summary>몬스터 BT Addressables 키 접두어입니다.</summary>
        public const string MonsterBt = ConfigDefine.NameSDK + "_MonsterBt";

        /// <summary>
        /// 몬스터 BT Addressables 키를 생성합니다.
        /// 테이블 입력값은 MonsterBt 루트 하위 상대 경로 기준으로 정규화됩니다.
        /// </summary>
        /// <param name="infoBtFileName">BtFileName 원본 입력값입니다.</param>
        /// <returns>정규화된 Addressables 키 문자열입니다. 입력값이 유효하지 않으면 빈 문자열을 반환합니다.</returns>
        public static string GetMonsterBt(string infoBtFileName)
        {
            string relativePath = ConfigAddressablePathAiBt.MonsterBt.NormalizeRelativePath(infoBtFileName);
            if (string.IsNullOrWhiteSpace(relativePath))
                return string.Empty;

            return $"{MonsterBt}_{relativePath}";
        }
    }
}
