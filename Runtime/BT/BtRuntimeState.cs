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

        /// <summary>
        /// 노드 실행 인스턴스(execution key)별 상태.
        /// 같은 노드가 여러 부모 경로에서 공유되더라도 상태가 섞이지 않도록 분리한다.
        /// </summary>
        public readonly Dictionary<string, BtNodeState> NodeStates = new(StringComparer.Ordinal);
        /// <summary>
        /// 전역 쿨다운 키별 다음 사용 가능 시간(Time.time 기준).
        /// </summary>
        public readonly Dictionary<string, float> Cooldowns = new(StringComparer.Ordinal);

        /// <summary>
        /// 실행 인스턴스별 타임아웃/대기 시작 시간(Time.time 기준).
        /// </summary>
        public readonly Dictionary<string, float> Timeouts = new(StringComparer.Ordinal);

        public BtNodeState GetOrCreateNodeState(string executionKey, string nodeId)
        {
            if (string.IsNullOrEmpty(executionKey))
                executionKey = nodeId ?? string.Empty;

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

        public void RemoveExecutionScope(string executionKeyPrefix, bool includeSelf)
        {
            if (string.IsNullOrEmpty(executionKeyPrefix))
                return;

            RemoveMatching(NodeStates, executionKeyPrefix, includeSelf);
            RemoveMatching(Timeouts, executionKeyPrefix, includeSelf);
        }

        private static void RemoveMatching<T>(Dictionary<string, T> dictionary, string prefix, bool includeSelf)
        {
            if (dictionary == null || dictionary.Count == 0)
                return;

            var removeKeys = ListPool<string>.Get();
            try
            {
                foreach (var key in dictionary.Keys)
                {
                    if (string.IsNullOrEmpty(key))
                        continue;

                    if (includeSelf)
                    {
                        if (key.Equals(prefix, StringComparison.Ordinal) || key.StartsWith(prefix + "/", StringComparison.Ordinal))
                            removeKeys.Add(key);
                    }
                    else if (key.StartsWith(prefix + "/", StringComparison.Ordinal))
                    {
                        removeKeys.Add(key);
                    }
                }

                for (int i = 0; i < removeKeys.Count; i++)
                    dictionary.Remove(removeKeys[i]);
            }
            finally
            {
                ListPool<string>.Release(removeKeys);
            }
        }

        private static class ListPool<T>
        {
            [ThreadStatic] private static List<T> _cache;

            public static List<T> Get()
            {
                var list = _cache;
                if (list != null)
                {
                    _cache = null;
                    list.Clear();
                    return list;
                }

                return new List<T>();
            }

            public static void Release(List<T> list)
            {
                if (list == null)
                    return;

                list.Clear();
                _cache = list;
            }
        }
    }
}
