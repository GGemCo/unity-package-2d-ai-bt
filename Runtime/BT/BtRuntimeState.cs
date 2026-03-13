using System;
using System.Collections.Generic;

namespace GGemCo2DAiBt
{
    internal sealed class BtNodeState
    {
        public string NodeId;
        public BtStatus LastStatus;
        public int RunningChildIndex;
        public float StartTime;
        public bool SkillStarted;
        public int RunningSkillUid;
    }

    internal sealed class BtRuntimeState
    {
        public int TickIndex;
        public float LastTickTime;

        public readonly Dictionary<string, BtNodeState> NodeStates = new(StringComparer.Ordinal);
        /// <summary>
        /// 전역 쿨다운 키별 다음 사용 가능 시간(Time.time 기준).
        /// </summary>
        public readonly Dictionary<string, float> Cooldowns = new(StringComparer.Ordinal);

        /// <summary>
        /// 노드별 타임아웃/대기 시작 시간(Time.time 기준).
        /// </summary>
        public readonly Dictionary<string, float> Timeouts = new(StringComparer.Ordinal);

        public BtNodeState GetOrCreateNodeState(string nodeId)
        {
            if (!NodeStates.TryGetValue(nodeId, out var st))
            {
                st = new BtNodeState { NodeId = nodeId, LastStatus = BtStatus.Failure, RunningChildIndex = 0, StartTime = 0f, SkillStarted = false, RunningSkillUid = 0 };
                NodeStates[nodeId] = st;
            }
            return st;
        }
    }
}
