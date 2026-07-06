using GGemCo2DCore;
using GGemCo2DCoreEditor;

namespace GGemCo2DAiBtEditor
{
    /// <summary>
    /// AI BT 패키지 에디터 툴 메뉴 경로와 정렬 순서를 정의합니다.
    /// </summary>
    public static class ConfigEditorAiBt
    {
        public enum ToolOrdering
        {
            /// <summary>기본 셋팅 메뉴 섹션의 시작 위치입니다.</summary>
            DefaultSetting = 1,

            /// <summary>Addressables 셋팅 메뉴 섹션의 시작 위치입니다.</summary>
            SettingAddressable,

            /// <summary>Pre-Intro 씬 셋팅 메뉴 섹션의 시작 위치입니다.</summary>
            SettingScenePreIntro,

            /// <summary>로딩 씬 셋팅 메뉴 섹션의 시작 위치입니다.</summary>
            SettingSceneLoading,

            /// <summary>게임 씬 셋팅 메뉴 섹션의 시작 위치입니다.</summary>
            SettingSceneGame,

            /// <summary>개발 도구 메뉴 섹션의 시작 위치입니다.</summary>
            Development = 100,
            CreateBt,

            /// <summary>테스트 도구 메뉴 섹션의 시작 위치입니다.</summary>
            Test = 200,
            SettingTestSkill,

            /// <summary>셔플(Shuffle) 미리보기 메뉴의 위치입니다.</summary>
            PreviewShuffle,

            /// <summary>기타 도구 메뉴 섹션의 시작 위치입니다.</summary>
            Etc = 900,
        }

       private const string NameToolGGemCoAiBt = GGemCoToolMenu.AiBt;

        // 기본 셋팅하기

        /// <summary>
        /// 기본 셋팅 메뉴(설정하기)의 경로 접두사입니다.
        /// </summary>
        private const string NameToolSettings = NameToolGGemCoAiBt + GGemCoToolMenu.Settings;

        /// <summary>
        /// "자동 셋팅하기" 메뉴 경로입니다.
        /// </summary>
        public const string NameToolSettingAuto = NameToolSettings + "자동 셋팅하기";

        /// <summary>
        /// "기본 셋팅하기" 메뉴 경로입니다.
        /// </summary>
        public const string NameToolSettingDefault = NameToolSettings + "기본 셋팅하기";

        /// <summary>
        /// "Addressable 셋팅하기" 메뉴 경로입니다.
        /// </summary>
        public const string NameToolSettingAddressable = NameToolSettings + "Addressable 셋팅하기";

        /// <summary>
        /// "Pre 인트로 씬 셋팅하기" 메뉴 경로입니다.
        /// </summary>
        public const string NameToolSettingScenePreIntro = NameToolSettings + "Pre 인트로 씬 셋팅하기";

        /// <summary>
        /// "로딩 씬 셋팅하기" 메뉴 경로입니다.
        /// </summary>
        public const string NameToolSettingSceneLoading = NameToolSettings + "로딩 씬 셋팅하기";

        /// <summary>
        /// "게임 씬 셋팅하기" 메뉴 경로입니다.
        /// </summary>
        public const string NameToolSettingSceneGame = NameToolSettings + "게임 씬 셋팅하기";

        // 개발툴

        /// <summary>
        /// 개발툴 메뉴의 경로 접두사입니다.
        /// </summary>
        private const string NameToolDevelopment = NameToolGGemCoAiBt + GGemCoToolMenu.Development;

        public const string NameToolCreateBt= NameToolDevelopment + "BT 생성&테스트 툴";

        // 테스트

        /// <summary>
        /// 테스트툴 메뉴의 경로 접두사입니다.
        /// </summary>
        /// <remarks>
        /// NOTE: 현재 문자열이 "테스트툴"로 되어 있는데, 의도한 표기가 "테스트툴"이라면 수정이 필요합니다.
        /// </remarks>
        private const string NameToolTest = NameToolGGemCoAiBt + GGemCoToolMenu.Test;

        // etc

        /// <summary>
        /// 기타 메뉴의 경로 접두사입니다.
        /// </summary>
        private const string NameToolEtc = NameToolGGemCoAiBt + GGemCoToolMenu.Etc;

        public const string PathPackageCore = "Packages/com.ggemco.2d.ai.bt";
    }
}
