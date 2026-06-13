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
        public string ExecutionKey;
        public BtStatus Status;
        public int Depth;

        public BtDebugNodeResult(string nodeId, string executionKey, BtStatus status, int depth)
        {
            NodeId = nodeId;
            ExecutionKey = executionKey;
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
        public string ExecutionKey;
        public string Key;
        public float Value;
        public string Text;

        public BtDebugMetric(string nodeId, string executionKey, string key, float value, string text = null)
        {
            NodeId = nodeId;
            ExecutionKey = executionKey;
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
        RootRestartRequested,
        RootRestartApplied,
        /// <summary>입력 방향/축 제한 문제로 이동이 거부됨.</summary>
        MoveBlockedByDirection,
        /// <summary>상태(DontMove/Attack/Dead)로 이동이 거부됨.</summary>
        MoveBlockedByStatus,
        /// <summary>이동 속도 계산값이 0 이하라 이동이 거부됨.</summary>
        MoveBlockedBySpeed,
        /// <summary>이동 요청이 수락되지 않았으나 세부 사유를 분류하지 못함.</summary>
        MoveRequestRejected,
        UnknownType,
        BreakpointMatched,
        /// <summary>현재 타겟이 선호 전투 거리보다 가까움.</summary>
        TargetTooClose,
        /// <summary>현재 타겟이 선호 전투 거리보다 멂.</summary>
        TargetTooFar,
        /// <summary>선호 전투 거리 또는 스킬 사거리에 도달함.</summary>
        DesiredRangeReached,
        /// <summary>현재 Threat 목록에서 전투 타겟을 선택하지 못함.</summary>
        CombatTargetSelectionFailed,
        /// <summary>Leash 범위가 설정되지 않아 요청을 실행할 수 없음.</summary>
        LeashNotConfigured,
        /// <summary>Leash Evade 및 홈 복귀가 시작됨.</summary>
        LeashReturnStarted,
        /// <summary>Leash Provider가 Evade 요청을 거부함.</summary>
        LeashRequestRejected,
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
        public string ExecutionKey;
        public BtDebugEventKind Kind;
        public string Title;
        public BtStatus Status;
        public BtDebugReason Reason;
        public string Summary;

        public BtDebugEvent(string nodeId, string executionKey, BtDebugEventKind kind, string title, BtStatus status, BtDebugReason reason, string summary)
        {
            NodeId = nodeId;
            ExecutionKey = executionKey;
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
        public string ActiveExecutionKey;

        public readonly List<string> ActivePath = new();
        public readonly List<string> ActiveExecutionPath = new();
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
            ActiveExecutionKey = null;
            ActivePath.Clear();
            ActiveExecutionPath.Clear();
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
            clone.ActiveExecutionKey = ActiveExecutionKey;
            clone.ActivePath.AddRange(ActivePath);
            clone.ActiveExecutionPath.AddRange(ActiveExecutionPath);
            clone.Visits.AddRange(Visits);
            clone.Metrics.AddRange(Metrics);
            clone.Events.AddRange(Events);
            return clone;
        }
    }
}
