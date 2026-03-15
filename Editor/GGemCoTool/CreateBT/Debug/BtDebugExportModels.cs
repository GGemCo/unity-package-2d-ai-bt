#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace GGemCo2DAiBtEditor
{
    [Serializable]
    public sealed class BtDebugExportData
    {
        public string ExportedAtUtc;
        public string ProductName;
        public string CompanyName;
        public string UnityVersion;
        public string SceneName;
        public bool IsPlaying;
        public string CurrentDebugTab;
        public bool SelectedNodeOnly;
        public string SelectedNodeId;
        public string SelectedNodeTitle;
        public string AssetName;
        public string AssetRootNodeId;
        public string AssetRootNodeTitle;
        public string RunnerName;
        public bool DebugFreeze;
        public bool DebugBreakpointsEnabled;
        public BtDebugFrameExport CurrentFrame;
        public BtDebugBreakInfoExport LastBreak;
        public List<BtDebugEventExport> Events = new();
        public List<BtDebugMetricExport> Metrics = new();
        public List<BtDebugHistoryFrameExport> History = new();
        public List<BtDebugBreakpointExport> Breakpoints = new();
    }

    [Serializable]
    public sealed class BtDebugFrameExport
    {
        public int TickIndex;
        public float Time;
        public string RootStatus;
        public string RootNodeId;
        public string RootNodeTitle;
        public string ActiveNodeId;
        public string ActiveNodeTitle;
        public string ActiveExecutionKey;
        public List<string> ActivePathNodeIds = new();
        public List<string> ActivePathTitles = new();
        public List<string> ActiveExecutionPath = new();
    }

    [Serializable]
    public sealed class BtDebugBreakInfoExport
    {
        public bool IsValid;
        public int TickIndex;
        public string NodeId;
        public string NodeTitle;
        public string Status;
        public string Reason;
        public string Summary;
    }

    [Serializable]
    public sealed class BtDebugEventExport
    {
        public string Kind;
        public string NodeId;
        public string NodeTitle;
        public string ExecutionKey;
        public string Status;
        public string Reason;
        public string Summary;
    }

    [Serializable]
    public sealed class BtDebugMetricExport
    {
        public string NodeId;
        public string NodeTitle;
        public string ExecutionKey;
        public string Key;
        public float Value;
        public string Text;
    }

    [Serializable]
    public sealed class BtDebugHistoryFrameExport
    {
        public int TickIndex;
        public float Time;
        public string RootStatus;
        public string RootNodeId;
        public string RootNodeTitle;
        public string ActiveNodeId;
        public string ActiveNodeTitle;
        public string ActiveExecutionKey;
        public List<string> ActivePathNodeIds = new();
        public List<string> ActivePathTitles = new();
        public List<string> ActiveExecutionPath = new();
        public int EventCount;
        public int MetricCount;
    }

    [Serializable]
    public sealed class BtDebugBreakpointExport
    {
        public string NodeId;
        public string NodeTitle;
        public string TypeId;
        public bool IsConfigured;
        public bool BreakOnVisit;
        public bool BreakOnSuccess;
        public bool BreakOnFailure;
        public bool BreakOnRunning;
    }
}
#endif
