#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using GGemCo2DAiBt;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GGemCo2DAiBtEditor
{
    internal static class BtDebugExportBuilder
    {
        public static BtDebugExportData Build(MonsterBtRunner runner, MonsterBehaviorTreeAsset asset, string selectedNodeId, string currentDebugTab, bool selectedNodeOnly)
        {
            if (runner == null)
                throw new ArgumentNullException(nameof(runner));
            if (asset == null)
                throw new ArgumentNullException(nameof(asset));

            var data = new BtDebugExportData
            {
                ExportedAtUtc = DateTime.UtcNow.ToString("O"),
                ProductName = Application.productName,
                CompanyName = Application.companyName,
                UnityVersion = Application.unityVersion,
                SceneName = runner.gameObject.scene.IsValid() ? runner.gameObject.scene.name : SceneManager.GetActiveScene().name,
                IsPlaying = EditorApplication.isPlaying,
                CurrentDebugTab = currentDebugTab ?? string.Empty,
                SelectedNodeOnly = selectedNodeOnly,
                SelectedNodeId = selectedNodeId ?? string.Empty,
                SelectedNodeTitle = ResolveNodeTitle(asset, selectedNodeId),
                AssetName = asset.name,
                AssetRootNodeId = asset.rootNodeId ?? string.Empty,
                AssetRootNodeTitle = ResolveNodeTitle(asset, asset.rootNodeId),
                RunnerName = runner.name,
                DebugFreeze = runner.DebugFreeze,
                DebugBreakpointsEnabled = runner.DebugBreakpointsEnabled,
                CurrentFrame = BuildFrameExport(asset, runner.DebugLastFrame),
                LastBreak = BuildBreakInfoExport(asset, runner.DebugLastBreakInfo),
            };

            var frame = runner.DebugLastFrame;
            IEnumerable<BtDebugEvent> events = frame != null ? frame.Events : Array.Empty<BtDebugEvent>();
            IEnumerable<BtDebugMetric> metrics = frame != null ? frame.Metrics : Array.Empty<BtDebugMetric>();

            if (selectedNodeOnly && !string.IsNullOrEmpty(selectedNodeId))
            {
                events = events.Where(x => string.Equals(x.NodeId, selectedNodeId, StringComparison.Ordinal));
                metrics = metrics.Where(x => string.Equals(x.NodeId, selectedNodeId, StringComparison.Ordinal));
            }

            foreach (var ev in events)
            {
                data.Events.Add(new BtDebugEventExport
                {
                    Kind = ev.Kind.ToString(),
                    NodeId = ev.NodeId ?? string.Empty,
                    NodeTitle = !string.IsNullOrEmpty(ev.Title) ? ev.Title : ResolveNodeTitle(asset, ev.NodeId),
                    ExecutionKey = ev.ExecutionKey ?? string.Empty,
                    Status = ev.Status.ToString(),
                    Reason = ev.Reason.ToString(),
                    Summary = ev.Summary ?? string.Empty,
                });
            }

            foreach (var metric in metrics)
            {
                data.Metrics.Add(new BtDebugMetricExport
                {
                    NodeId = metric.NodeId ?? string.Empty,
                    NodeTitle = ResolveNodeTitle(asset, metric.NodeId),
                    ExecutionKey = metric.ExecutionKey ?? string.Empty,
                    Key = metric.Key ?? string.Empty,
                    Value = metric.Value,
                    Text = metric.Text ?? string.Empty,
                });
            }

            var history = runner.DebugHistory;
            if (history != null)
            {
                foreach (var historyFrame in history)
                    data.History.Add(BuildHistoryFrameExport(asset, historyFrame));
            }

            if (asset.nodes != null)
            {
                foreach (var node in asset.nodes)
                {
                    if (node == null || string.IsNullOrEmpty(node.id))
                        continue;

                    bool hasBreakpoint = runner.TryGetBreakpoint(node.id, out var breakpoint) && !breakpoint.IsEmpty;
                    data.Breakpoints.Add(new BtDebugBreakpointExport
                    {
                        NodeId = node.id,
                        NodeTitle = string.IsNullOrEmpty(node.title) ? node.id : node.title,
                        TypeId = node.typeId ?? string.Empty,
                        IsConfigured = hasBreakpoint,
                        BreakOnVisit = hasBreakpoint && breakpoint.BreakOnVisit,
                        BreakOnSuccess = hasBreakpoint && breakpoint.BreakOnSuccess,
                        BreakOnFailure = hasBreakpoint && breakpoint.BreakOnFailure,
                        BreakOnRunning = hasBreakpoint && breakpoint.BreakOnRunning,
                    });
                }
            }

            return data;
        }

        private static BtDebugFrameExport BuildFrameExport(MonsterBehaviorTreeAsset asset, BtDebugFrame frame)
        {
            if (frame == null)
                return null;

            var export = new BtDebugFrameExport
            {
                TickIndex = frame.TickIndex,
                Time = frame.Time,
                RootStatus = frame.RootStatus.ToString(),
                RootNodeId = frame.RootNodeId ?? string.Empty,
                RootNodeTitle = ResolveNodeTitle(asset, frame.RootNodeId),
                ActiveNodeId = frame.ActiveNodeId ?? string.Empty,
                ActiveNodeTitle = ResolveNodeTitle(asset, frame.ActiveNodeId),
                ActiveExecutionKey = frame.ActiveExecutionKey ?? string.Empty,
            };

            CopyPath(asset, frame.ActivePath, export.ActivePathNodeIds, export.ActivePathTitles);
            if (frame.ActiveExecutionPath != null)
                export.ActiveExecutionPath.AddRange(frame.ActiveExecutionPath);
            return export;
        }

        private static BtDebugHistoryFrameExport BuildHistoryFrameExport(MonsterBehaviorTreeAsset asset, BtDebugFrame frame)
        {
            var export = new BtDebugHistoryFrameExport
            {
                TickIndex = frame.TickIndex,
                Time = frame.Time,
                RootStatus = frame.RootStatus.ToString(),
                RootNodeId = frame.RootNodeId ?? string.Empty,
                RootNodeTitle = ResolveNodeTitle(asset, frame.RootNodeId),
                ActiveNodeId = frame.ActiveNodeId ?? string.Empty,
                ActiveNodeTitle = ResolveNodeTitle(asset, frame.ActiveNodeId),
                ActiveExecutionKey = frame.ActiveExecutionKey ?? string.Empty,
                EventCount = frame.Events != null ? frame.Events.Count : 0,
                MetricCount = frame.Metrics != null ? frame.Metrics.Count : 0,
            };

            CopyPath(asset, frame.ActivePath, export.ActivePathNodeIds, export.ActivePathTitles);
            if (frame.ActiveExecutionPath != null)
                export.ActiveExecutionPath.AddRange(frame.ActiveExecutionPath);
            return export;
        }

        private static BtDebugBreakInfoExport BuildBreakInfoExport(MonsterBehaviorTreeAsset asset, BtDebugBreakInfo breakInfo)
        {
            return new BtDebugBreakInfoExport
            {
                IsValid = breakInfo.IsValid,
                TickIndex = breakInfo.TickIndex,
                NodeId = breakInfo.NodeId ?? string.Empty,
                NodeTitle = ResolveNodeTitle(asset, breakInfo.NodeId),
                Status = breakInfo.Status.ToString(),
                Reason = breakInfo.Reason.ToString(),
                Summary = breakInfo.Summary ?? string.Empty,
            };
        }

        private static void CopyPath(MonsterBehaviorTreeAsset asset, IReadOnlyList<string> sourcePath, List<string> idTarget, List<string> titleTarget)
        {
            if (sourcePath == null)
                return;

            for (int i = 0; i < sourcePath.Count; i++)
            {
                string nodeId = sourcePath[i] ?? string.Empty;
                idTarget.Add(nodeId);
                titleTarget.Add(ResolveNodeTitle(asset, nodeId));
            }
        }

        private static string ResolveNodeTitle(MonsterBehaviorTreeAsset asset, string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
                return string.Empty;

            var node = asset != null ? asset.FindNode(nodeId) : null;
            if (node == null)
                return nodeId;

            return string.IsNullOrEmpty(node.title) ? node.id : node.title;
        }
    }
}
#endif
