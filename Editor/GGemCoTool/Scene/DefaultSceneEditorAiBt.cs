using GGemCo2DCore;
using GGemCo2DCoreEditor;

namespace GGemCo2DAiBtEditor
{
    public class DefaultSceneEditorAiBt : DefaultSceneEditor
    {
        protected override void OnEnable()
        {
            base.OnEnable();
            packageType = ConfigPackageInfo.PackageType.AiBt;
        }
    }
}