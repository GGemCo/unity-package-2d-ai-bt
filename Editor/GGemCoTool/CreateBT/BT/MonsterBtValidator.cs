#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using GGemCo2DAiBt;
using UnityEngine;

namespace GGemCo2DAiBtEditor
{
    internal static class MonsterBtValidator
    {
        public enum Severity { Info, Warning, Error }

        public readonly struct Issue
        {
            public readonly Severity Severity;
            public readonly string Code;
            public readonly string Message;
            public readonly string NodeId;

            public Issue(Severity severity, string code, string message, string nodeId = null)
            {
                this.Severity = severity;
                this.Code = code;
                this.Message = message;
                this.NodeId = nodeId;
            }
        }

        public static List<Issue> Validate(MonsterBehaviorTreeAsset asset)
        {
            var issues = new List<Issue>();
            if (asset == null)
            {
                issues.Add(new Issue(Severity.Error, "BT000", "Asset is null"));
                return issues;
            }

            if (string.IsNullOrEmpty(asset.rootNodeId))
                issues.Add(new Issue(Severity.Error, "BT001", "Root node is missing."));

            var nodeById = new Dictionary<string, BtNodeRecord>(StringComparer.Ordinal);
            if (asset.nodes != null)
            {
                foreach (var n in asset.nodes)
                {
                    if (n == null) continue;
                    if (string.IsNullOrEmpty(n.id))
                    {
                        issues.Add(new Issue(Severity.Error, "BT002", "A node has empty id."));
                        continue;
                    }
                    if (!nodeById.TryAdd(n.id, n))
                        issues.Add(new Issue(Severity.Error, "BT003", $"Duplicate node id: {n.id}", n.id));
                }
            }

            if (!string.IsNullOrEmpty(asset.rootNodeId) && !nodeById.ContainsKey(asset.rootNodeId))
                issues.Add(new Issue(Severity.Error, "BT004", $"Root node not found: {asset.rootNodeId}", asset.rootNodeId));

            var incomingCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var id in nodeById.Keys)
                incomingCount[id] = 0;

            // children rule + missing reference
            foreach (var kv in nodeById)
            {
                var n = kv.Value;
                int childCount = n.children?.Count ?? 0;
                switch (n.kind)
                {
                    case BtNodeKind.Composite:
                        if (childCount <= 0)
                            issues.Add(new Issue(Severity.Error, "BT010", "Composite node must have at least one child.", n.id));
                        break;
                    case BtNodeKind.Decorator:
                        if (childCount != 1)
                            issues.Add(new Issue(Severity.Error, "BT011", "Decorator node must have exactly one child.", n.id));
                        break;
                    case BtNodeKind.Condition:
                    case BtNodeKind.Action:
                        if (childCount != 0)
                            issues.Add(new Issue(Severity.Error, "BT012", "Condition/Action node must have no children.", n.id));
                        break;
                }

                if (n.children == null) continue;
                foreach (var childId in n.children)
                {
                    if (string.IsNullOrEmpty(childId))
                    {
                        issues.Add(new Issue(Severity.Error, "BT020", "Child id is empty.", n.id));
                        continue;
                    }
                    if (!nodeById.ContainsKey(childId))
                    {
                        issues.Add(new Issue(Severity.Error, "BT021", $"Child node not found: {childId}", n.id));
                        continue;
                    }

                    incomingCount[childId] = incomingCount.TryGetValue(childId, out var count) ? count + 1 : 1;
                }
            }

            foreach (var kv in nodeById)
            {
                var node = kv.Value;
                incomingCount.TryGetValue(node.id, out var count);

                if (string.Equals(node.id, asset.rootNodeId, StringComparison.Ordinal))
                {
                    if (count > 0)
                        issues.Add(new Issue(Severity.Error, "BT040", "Root node must not have any parent.", node.id));
                    continue;
                }

                if (count == 0)
                    issues.Add(new Issue(Severity.Warning, "BT041", "Node is not reachable from any parent.", node.id));

                int maxParentCount = BtNodeParentPolicy.GetMaxParentCount(node);
                if (maxParentCount >= 0 && count > maxParentCount)
                {
                    string reason = BtNodeParentPolicy.GetMultipleParentsBlockedReason(node);
                    issues.Add(new Issue(Severity.Error, "BT042", $"Parent count exceeded ({count}). {reason}", node.id));
                }
            }

            // cycle detection (all connected/unconnected graphs)
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var stack = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in nodeById.Keys)
            {
                if (!visited.Contains(id))
                    DfsDetectCycle(id, nodeById, visited, stack, issues);
            }

            return issues;
        }

        /// <summary>
        /// 기존 호출부 호환을 위한 유틸.
        /// 내부적으로 <see cref="Validate"/> 후 <see cref="LogIssues"/>를 호출한다.
        /// </summary>
        public static void ValidateAndLog(MonsterBehaviorTreeAsset asset)
        {
            var issues = Validate(asset);
            LogIssues(asset, issues);
        }

        private static void DfsDetectCycle(string id, Dictionary<string, BtNodeRecord> nodeById, HashSet<string> visited, HashSet<string> stack, List<Issue> issues)
        {
            if (!visited.Add(id)) return;
            stack.Add(id);

            var n = nodeById[id];
            if (n.children != null)
            {
                foreach (var childId in n.children)
                {
                    if (string.IsNullOrEmpty(childId) || !nodeById.ContainsKey(childId)) continue;
                    if (stack.Contains(childId))
                    {
                        issues.Add(new Issue(Severity.Error, "BT030", $"Cycle detected: {id} -> {childId}", id));
                        continue;
                    }
                    DfsDetectCycle(childId, nodeById, visited, stack, issues);
                }
            }

            stack.Remove(id);
        }

        public static void LogIssues(UnityEngine.Object context, List<Issue> issues)
        {
            if (issues == null || issues.Count == 0)
            {
                Debug.Log("[BT] Validate OK (no issues)", context);
                return;
            }

            int error = 0;
            for (int i = 0; i < issues.Count; i++)
            {
                var it = issues[i];
                string head = $"[BT] {it.Severity} {it.Code}";
                string msg = string.IsNullOrEmpty(it.NodeId) ? it.Message : $"{it.Message} (node={it.NodeId})";
                if (it.Severity == Severity.Error)
                {
                    error++;
                    Debug.LogError($"{head}: {msg}", context);
                }
                else if (it.Severity == Severity.Warning)
                {
                    Debug.LogWarning($"{head}: {msg}", context);
                }
                else
                {
                    Debug.Log($"{head}: {msg}", context);
                }
            }
            Debug.Log($"[BT] Validate finished. issues={issues.Count}, errors={error}", context);
        }
    }
}
#endif
