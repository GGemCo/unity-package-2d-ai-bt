using System;
using System.Collections.Generic;

namespace GGemCo2DAiBt
{
    internal sealed class BtNodeState
    {
        public string NodeId;
        public string ExecutionKey;
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

        /// <summary>
        /// 현재 틱에서 Root부터 다시 시작하도록 다음 틱 재평가를 요청한다.
        /// </summary>
        public bool RestartRequested;
        public string RestartReason;
        public string RestartRequestedByExecutionKey;
        public string RestartRequestedByNodeId;

        public BtNodeState GetOrCreateNodeState(string executionKey, string nodeId)
        {
            if (!NodeStates.TryGetValue(executionKey, out var st))
            {
                st = new BtNodeState
                {
                    NodeId = nodeId,
                    ExecutionKey = executionKey,
                    LastStatus = BtStatus.Failure,
                    RunningChildIndex = 0,
                    StartTime = 0f,
                    SkillStarted = false,
                    RunningSkillUid = 0
                };
                NodeStates[executionKey] = st;
            }
            else
            {
                st.NodeId = nodeId;
                st.ExecutionKey = executionKey;
            }
            return st;
        }

        public void RequestRestart(string nodeId, string executionKey, string reason)
        {
            RestartRequested = true;
            RestartReason = reason;
            RestartRequestedByExecutionKey = executionKey;
            RestartRequestedByNodeId = nodeId;
        }

        public void ClearRestartRequest()
        {
            RestartRequested = false;
            RestartReason = null;
            RestartRequestedByExecutionKey = null;
            RestartRequestedByNodeId = null;
        }
    }
}
