using System;
using System.Collections.Generic;
using GGemCo2DCore;

namespace GGemCo2DAiBt
{
    /// <summary>
    /// AiBt 패키지 ScriptableObject 메뉴 설정 정의
    /// </summary>
    public static class ConfigScriptableObjectAiBt
    {
        /// <summary>
        /// AiBt 패키지 Settings 식별 키
        /// </summary>
        public enum AiBtSettingsKey
        {
            Main,
        }

        /// <summary>
        /// AiBt 패키지 내부 메뉴 정렬 순서
        /// </summary>
        public enum AiBtLocalOrder
        {
            MainSettings = 0,
        }

        public const string BasePath = ConfigDefine.NameSDK + "/Settings/";
        public const string BaseName = ConfigDefine.NameSDK;

        /// <summary>
        /// 메인 설정 메뉴 정보
        /// </summary>
        public static class Main
        {
            public const string FileName = BaseName + "AiBtSettings";
            public const string MenuName = BasePath + FileName;
            public const int Ordering =
                (int)ConfigScriptableObjectCommon.PackageOrder.AiBt +
                (int)AiBtLocalOrder.MainSettings;
        }

        /// <summary>
        /// AiBt 패키지 전체 메뉴 메타데이터
        /// </summary>
        public static readonly IReadOnlyDictionary<AiBtSettingsKey, ConfigScriptableObjectCommon.MenuInfo> Infos =
            new Dictionary<AiBtSettingsKey, ConfigScriptableObjectCommon.MenuInfo>
            {
                {
                    AiBtSettingsKey.Main,
                    new ConfigScriptableObjectCommon.MenuInfo(
                        Main.FileName,
                        Main.MenuName,
                        Main.Ordering,
                        typeof(GGemCoAiBtSettings))
                },
            };

        /// <summary>
        /// 파일명 기준으로 타입을 조회하기 위한 매핑
        /// </summary>
        public static readonly IReadOnlyDictionary<string, Type> SettingsTypes =
            new Dictionary<string, Type>
            {
                { Main.FileName, typeof(GGemCoAiBtSettings) },
            };

        /// <summary>
        /// 설정 키로 메뉴 정보를 조회한다.
        /// </summary>
        public static ConfigScriptableObjectCommon.MenuInfo GetInfo(AiBtSettingsKey key)
        {
            return Infos[key];
        }

        /// <summary>
        /// 파일명으로 설정 타입을 조회한다.
        /// </summary>
        public static bool TryGetSettingsType(string fileName, out Type settingsType)
        {
            return SettingsTypes.TryGetValue(fileName, out settingsType);
        }
    }
}