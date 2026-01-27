using GGemCo2DCore;

namespace GGemCo2DAiBt
{
    public static class ConfigAddressableKeyAiBt
    {
        public const string MonsterBt       = ConfigDefine.NameSDK + "_MonsterBt";

        public static string GetMonsterBt(string infoBtFileName)
        {
            return $"{MonsterBt}_{infoBtFileName}";
        }
    }
}