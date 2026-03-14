#if UNITY_EDITOR
using GGemCo2DAiBt;

namespace GGemCo2DAiBtEditor
{
    /// <summary>
    /// BT 디자이너에서 허용하는 부모 연결 정책을 정의한다.
    /// 현재는 런타임 상태 충돌을 방지하기 위해 상태 없는 노드만 다중 부모를 허용한다.
    /// </summary>
    internal static class BtNodeParentPolicy
    {
        /// <summary>
        /// 지정 노드가 여러 부모 입력을 동시에 받을 수 있는지 반환한다.
        /// </summary>
        public static bool SupportsMultipleParents(BtNodeRecord record)
        {
            if (record == null)
                return false;

            // 현재 런타임은 node.id 기준으로 Running/Timeout/Decorator 상태를 관리하므로,
            // 상태 없는 Condition만 안전하게 공유한다.
            return true;
        }

        /// <summary>
        /// 지정 노드의 허용 최대 부모 수를 반환한다.
        /// 음수는 제한 없음으로 해석한다.
        /// </summary>
        public static int GetMaxParentCount(BtNodeRecord record)
        {
            return SupportsMultipleParents(record) ? -1 : 1;
        }

        /// <summary>
        /// 부모 수 정책 위반 시 표시할 설명 문자열을 만든다.
        /// </summary>
        public static string GetMultipleParentsBlockedReason(BtNodeRecord record)
        {
            if (record == null)
                return "Child node not found.";

            if (SupportsMultipleParents(record))
                return string.Empty;

            return string.Empty;
        }
    }
}
#endif
