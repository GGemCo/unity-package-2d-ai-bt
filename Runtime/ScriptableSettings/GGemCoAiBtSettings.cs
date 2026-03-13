using GGemCo2DCore;
using UnityEngine;

namespace GGemCo2DAiBt
{
    /// <summary>
    /// 스킬 메인 설정
    /// </summary>

    [CreateAssetMenu(fileName = ConfigScriptableObjectAiBt.Main.FileName, menuName = ConfigScriptableObjectAiBt.Main.MenuName, order = ConfigScriptableObjectAiBt.Main.Ordering)]
    public class GGemCoAiBtSettings : ScriptableObject
    {
        [Header("Tick")]
        [Tooltip("0 이면 Update 프레임마다 평가한다. 0보다 크면 해당 Hz로 평가한다.")]
        public float tickRateHz = 1f;

        [Header("Debug")]
        [SerializeField, DebugOption("플레이어 상태 로그 출력")]
        private bool enableDebugLog;
        public bool EnableDebugLog => DebugOptionRuntimeUtility.Resolve(enableDebugLog);
        
        [Tooltip("디자이너/디버그 창에서 실행 노드 하이라이트를 위해 트레이스를 수집한다.")]
        public bool enableDebugTrace = true;
        [Min(16), Tooltip("디버그 트레이스의 최대 방문 노드 기록 개수(한 틱 기준).")]
        public int debugTraceCapacity = 256;
        [Min(16), Tooltip("디버그 메트릭의 최대 기록 개수(한 틱 기준).")]
        public int debugMetricCapacity = 256;
        [Min(4), Tooltip("에디터 디버그 타임라인용 최근 프레임 보관 개수.")]
        public int debugHistoryCapacity = 32;
        [Tooltip("디버그 브레이크포인트 사용 여부.")]
        public bool enableDebugBreakpoints = true;
        
        /// <summary>
        /// 기존 값이 비어있을 때만 기본값을 설정
        /// </summary>
        private void OnEnable()
        {
        }

        /// <summary>
        /// 처음 생성 시 한 번만 실행됨
        /// </summary>
        private void Reset()
        {
        }
    }
}