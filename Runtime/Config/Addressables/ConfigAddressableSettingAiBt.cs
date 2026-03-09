using System.Collections.Generic;
using GGemCo2DCore;

namespace GGemCo2DAiBt
{
    public static class ConfigAddressableSettingAiBt
    {
        public static readonly AddressableAssetInfo AiBtSettings = ConfigAddressableSetting.Make(nameof(AiBtSettings));
        
        /// <summary>
        /// 로딩 씬에서 로드해야 하는 리스트
        /// </summary>
        public static readonly List<AddressableAssetInfo> NeedLoadInLoadingScene = new()
        {
            AiBtSettings,
        };
    }
}