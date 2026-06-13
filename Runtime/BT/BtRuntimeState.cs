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
        public int SelectedChildIndex;
        public float StartTime;
        public bool SkillStarted;
        public int RunningSkillUid;
        /// <summary>
        /// 직전 틱에 MoveToTarget이 공격 범위 진입 상태였는지 기록한다.
        /// </summary>
        public bool MoveInAttackRangeLastTick;

        /// <summary>
        /// 직전 틱에 선호 전투 거리 또는 스킬 사거리 진입 상태였는지 기록합니다.
        /// </summary>
        public bool MoveInDesiredRangeLastTick;
    }

    internal sealed class BtRuntimeState
    {
        public int TickIndex;
        public float LastTickTime;
        public bool RestartRootRequested;
        public string RestartRootReason;
        public string RestartRootNodeId;
        public string RestartRootExecutionKey;

        public readonly Dictionary<string, BtNodeState> NodeStates = new(StringComparer.Ordinal);
        /// <summary>
        /// 전역 쿨다운 키별 다음 사용 가능 시간(Time.time 기준).
        /// </summary>
        public readonly Dictionary<string, float> Cooldowns = new(StringComparer.Ordinal);

        /// <summary>
        /// 노드별 타임아웃/대기 시작 시간(Time.time 기준).
        /// </summary>
        public readonly Dictionary<string, float> Timeouts = new(StringComparer.Ordinal);


        public void RequestRestartRoot(string nodeId, string executionKey, string reason)
        {
            RestartRootRequested = true;
            RestartRootNodeId = nodeId;
            RestartRootExecutionKey = executionKey;
            RestartRootReason = reason;
        }

        public bool ConsumeRestartRootRequest(out string nodeId, out string executionKey, out string reason)
        {
            if (!RestartRootRequested)
            {
                nodeId = null;
                executionKey = null;
                reason = null;
                return false;
            }

            nodeId = RestartRootNodeId;
            executionKey = RestartRootExecutionKey;
            reason = RestartRootReason;

            RestartRootRequested = false;
            RestartRootNodeId = null;
            RestartRootExecutionKey = null;
            RestartRootReason = null;
            return true;
        }

        public void ClearExecutionStateForRootRestart()
        {
            NodeStates.Clear();
            Timeouts.Clear();
        }

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
                    SelectedChildIndex = -1,
                    StartTime = 0f,
                    SkillStarted = false,
                    RunningSkillUid = 0,
                    MoveInAttackRangeLastTick = false,
                    MoveInDesiredRangeLastTick = false,
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
    }
}
