using System;

namespace GGemCo2DAiBt
{
    /// <summary>
    /// 디버그용: 한 틱 동안 방문한 노드의 실행 결과.
    /// 런타임 로직에는 영향이 없는 관측(telemetry) 데이터이다.
    /// </summary>
    [Serializable]
    public struct BtDebugNodeResult
    {
        public string NodeId;
        public BtStatus Status;
        public int Depth;

        public BtDebugNodeResult(string nodeId, BtStatus status, int depth)
        {
            NodeId = nodeId;
            Status = status;
            Depth = depth;
        }
    }

    /// <summary>
    /// 디버그용: 조건 평가/거리/체력 등 실시간 관측 값.
    /// </summary>
    [Serializable]
    public struct BtDebugMetric
    {
        public string NodeId;
        public string Key;
        public float Value;
        public string Text;

        public BtDebugMetric(string nodeId, string key, float value, string text = null)
        {
            NodeId = nodeId;
            Key = key;
            Value = value;
            Text = text;
        }
    }
}
