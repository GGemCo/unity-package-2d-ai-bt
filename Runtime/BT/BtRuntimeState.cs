using System;
using System.Collections.Generic;

namespace GGemCo2DAiBt
{
    internal sealed class BtNodeState
    {
        public string nodeId;
        public BtStatus lastStatus;
        public int runningChildIndex;
        public float startTime;
    }

    internal sealed class BtRuntimeState
    {
        public int tickIndex;
        public float lastTickTime;

        public readonly Dictionary<string, BtNodeState> nodeStates = new(StringComparer.Ordinal);
        /// <summary>
        /// 전역 쿨다운 키별 다음 사용 가능 시간(Time.time 기준).
        /// </summary>
        public readonly Dictionary<string, float> cooldowns = new(StringComparer.Ordinal);

        /// <summary>
        /// 노드별 타임아웃/대기 시작 시간(Time.time 기준).
        /// </summary>
        public readonly Dictionary<string, float> timeouts = new(StringComparer.Ordinal);

        public BtNodeState GetOrCreateNodeState(string nodeId)
        {
            if (!nodeStates.TryGetValue(nodeId, out var st))
            {
                st = new BtNodeState { nodeId = nodeId, lastStatus = BtStatus.Failure, runningChildIndex = 0, startTime = 0f };
                nodeStates[nodeId] = st;
            }
            return st;
        }
    }
}
