#if UNITY_EDITOR
using GGemCo2DAiBt;

namespace GGemCo2DAiBtEditor
{
    /// <summary>
    /// BT 에디터에서 노드의 다중 부모 입력 허용 여부를 정의한다.
    /// execution key 기반 런타임 상태 분리 이후에는 모든 일반 노드가 공유 입력을 받을 수 있다.
    /// 루트 노드 여부는 레코드만으로 판단할 수 없으므로 GraphView/Validator에서 별도로 검사한다.
    /// </summary>
    internal static class BtNodeParentPolicy
    {
        public static bool SupportsMultipleParents(BtNodeRecord record)
        {
            return record != null;
        }
    }
}
#endif