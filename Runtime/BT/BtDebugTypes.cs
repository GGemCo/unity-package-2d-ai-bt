using System;
using System.Collections.Generic;

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

    /// <summary>
    /// 디버그 상세 레코드 분류.
    /// </summary>
    public enum BtDebugEventKind
    {
        Composite = 0,
        Decorator = 1,
        Condition = 2,
        Action = 3,
        Blackboard = 4,
        System = 5,
    }

    /// <summary>
    /// 디버그 실패/판단 이유 코드.
    /// </summary>
    public enum BtDebugReason
    {
        None = 0,
        NoTarget,
        OutOfRange,
        HpNotBelowThreshold,
        SkillBusy,
        SkillUidInvalid,
        CooldownNotReady,
        TimeoutExceeded,
        BlackboardMissing,
        InvalidParameter,
        ChildMissing,
        RandomPickFailed,
        UnknownType,
        BreakpointMatched,
    }

    /// <summary>
    /// 노드 브레이크포인트 설정.
    /// </summary>
    [Serializable]
    public struct BtDebugBreakpoint
    {
        public string NodeId;
        public bool BreakOnVisit;
        public bool BreakOnSuccess;
        public bool BreakOnFailure;
        public bool BreakOnRunning;

        public BtDebugBreakpoint(string nodeId, bool breakOnVisit, bool breakOnSuccess, bool breakOnFailure, bool breakOnRunning)
        {
            NodeId = nodeId;
            BreakOnVisit = breakOnVisit;
            BreakOnSuccess = breakOnSuccess;
            BreakOnFailure = breakOnFailure;
            BreakOnRunning = breakOnRunning;
        }

        public bool IsEmpty => !BreakOnVisit && !BreakOnSuccess && !BreakOnFailure && !BreakOnRunning;
    }

    /// <summary>
    /// 브레이크가 걸린 마지막 지점 정보.
    /// </summary>
    [Serializable]
    public struct BtDebugBreakInfo
    {
        public int TickIndex;
        public string NodeId;
        public BtStatus Status;
        public BtDebugReason Reason;
        public string Summary;

        public BtDebugBreakInfo(int tickIndex, string nodeId, BtStatus status, BtDebugReason reason, string summary)
        {
            TickIndex = tickIndex;
            NodeId = nodeId;
            Status = status;
            Reason = reason;
            Summary = summary;
        }

        public bool IsValid => !string.IsNullOrEmpty(NodeId);
    }

    /// <summary>
    /// 조건/액션/데코레이터/컴포지트의 실행 상세 한 줄.
    /// </summary>
    [Serializable]
    public struct BtDebugEvent
    {
        public string NodeId;
        public BtDebugEventKind Kind;
        public string Title;
        public BtStatus Status;
        public BtDebugReason Reason;
        public string Summary;

        public BtDebugEvent(string nodeId, BtDebugEventKind kind, string title, BtStatus status, BtDebugReason reason, string summary)
        {
            NodeId = nodeId;
            Kind = kind;
            Title = title;
            Status = status;
            Reason = reason;
            Summary = summary;
        }
    }

    /// <summary>
    /// 한 틱의 디버그 스냅샷.
    /// </summary>
    [Serializable]
    public sealed class BtDebugFrame
    {
        public int TickIndex;
        public float Time;
        public string RootNodeId;
        public BtStatus RootStatus;
        public string ActiveNodeId;

        public readonly List<string> ActivePath = new();
        public readonly List<BtDebugNodeResult> Visits = new();
        public readonly List<BtDebugMetric> Metrics = new();
        public readonly List<BtDebugEvent> Events = new();

        public void Reset(int tickIndex, float time, string rootNodeId)
        {
            TickIndex = tickIndex;
            Time = time;
            RootNodeId = rootNodeId;
            RootStatus = BtStatus.Failure;
            ActiveNodeId = null;
            ActivePath.Clear();
            Visits.Clear();
            Metrics.Clear();
            Events.Clear();
        }

        public BtDebugFrame Clone()
        {
            var clone = new BtDebugFrame();
            clone.TickIndex = TickIndex;
            clone.Time = Time;
            clone.RootNodeId = RootNodeId;
            clone.RootStatus = RootStatus;
            clone.ActiveNodeId = ActiveNodeId;
            clone.ActivePath.AddRange(ActivePath);
            clone.Visits.AddRange(Visits);
            clone.Metrics.AddRange(Metrics);
            clone.Events.AddRange(Events);
            return clone;
        }
    }
}
