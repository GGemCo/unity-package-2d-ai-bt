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
                Severity = severity;
                Code = code;
                Message = message;
                NodeId = nodeId;
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

            ValidateNodeDefinitions(nodeById, issues);

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
                        issues.Add(new Issue(Severity.Error, "BT021", $"Child node not found: {childId}", n.id));
                }
            }

            var incomingCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var id in nodeById.Keys)
                incomingCounts[id] = 0;

            foreach (var kv in nodeById)
            {
                var node = kv.Value;
                if (node.children == null)
                    continue;

                for (int i = 0; i < node.children.Count; i++)
                {
                    var childId = node.children[i];
                    if (string.IsNullOrEmpty(childId) || !incomingCounts.ContainsKey(childId))
                        continue;

                    incomingCounts[childId]++;
                }
            }

            foreach (var kv in nodeById)
            {
                var node = kv.Value;
                incomingCounts.TryGetValue(node.id, out int incoming);

                if (node.id == asset.rootNodeId)
                {
                    if (incoming > 0)
                        issues.Add(new Issue(Severity.Error, "BT040", "Root node must not have any parent.", node.id));
                    continue;
                }

                if (incoming == 0)
                    issues.Add(new Issue(Severity.Warning, "BT041", "Node is not connected from the root graph.", node.id));

                if (incoming > 1 && !BtNodeParentPolicy.SupportsMultipleParents(node))
                    issues.Add(new Issue(Severity.Error, "BT042", $"Node does not allow multiple parents. incoming={incoming}", node.id));
            }

            // cycle detection (DFS) : root 기준뿐 아니라 고립 서브그래프까지 전체 검사
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var stack = new HashSet<string>(StringComparer.Ordinal);
            foreach (var nodeId in nodeById.Keys)
            {
                if (visited.Contains(nodeId))
                    continue;

                DfsDetectCycle(nodeId, nodeById, visited, stack, issues);
            }

            return issues;
        }

        /// <summary>
        /// 런타임 타입 카탈로그와 노드 파라미터 정의를 기준으로 에셋 정합성을 검사합니다.
        /// </summary>
        private static void ValidateNodeDefinitions(
            Dictionary<string, BtNodeRecord> nodeById,
            List<Issue> issues)
        {
            var typeDefs = new Dictionary<string, BtNodeTypeDef>(StringComparer.Ordinal);
            for (int i = 0; i < BtNodeTypeCatalog.All.Count; i++)
            {
                BtNodeTypeDef def = BtNodeTypeCatalog.All[i];
                if (def != null && !string.IsNullOrEmpty(def.TypeId))
                    typeDefs[def.TypeId] = def;
            }

            foreach (KeyValuePair<string, BtNodeRecord> pair in nodeById)
            {
                BtNodeRecord node = pair.Value;
                if (string.IsNullOrEmpty(node.typeId) || !typeDefs.TryGetValue(node.typeId, out BtNodeTypeDef typeDef))
                {
                    issues.Add(new Issue(Severity.Error, "BT050", $"Unsupported node typeId: {node.typeId}", node.id));
                    continue;
                }

                if (node.kind != typeDef.Kind)
                {
                    issues.Add(new Issue(
                        Severity.Error,
                        "BT051",
                        $"Node kind does not match typeId. kind={node.kind}, expected={typeDef.Kind}, typeId={node.typeId}",
                        node.id));
                }

                ValidateParameters(node, issues);
                ValidateCombatNodeSemantics(node, issues);
            }
        }

        /// <summary>
        /// 카탈로그에 선언된 필수 파라미터와 실제 저장 타입을 검사합니다.
        /// </summary>
        private static void ValidateParameters(BtNodeRecord node, List<Issue> issues)
        {
            IReadOnlyList<BtParamDef> defs = BtNodeTypeCatalog.GetParamDefs(node.typeId);
            var actualByKey = new Dictionary<string, BtParamValue>(StringComparer.Ordinal);
            if (node.parameters != null)
            {
                for (int i = 0; i < node.parameters.Count; i++)
                {
                    BtParamValue parameter = node.parameters[i];
                    if (string.IsNullOrEmpty(parameter.key))
                    {
                        issues.Add(new Issue(Severity.Warning, "BT052", "Parameter key is empty.", node.id));
                        continue;
                    }

                    if (!actualByKey.TryAdd(parameter.key, parameter))
                    {
                        issues.Add(new Issue(Severity.Warning, "BT053", $"Duplicate parameter key: {parameter.key}", node.id));
                    }
                }
            }

            for (int i = 0; i < defs.Count; i++)
            {
                BtParamDef def = defs[i];
                if (!actualByKey.TryGetValue(def.Key, out BtParamValue actual))
                {
                    if (def.Required)
                        issues.Add(new Issue(Severity.Error, "BT054", $"Required parameter is missing: {def.Key}", node.id));
                    continue;
                }

                if (actual.valueType != def.ValueType)
                {
                    issues.Add(new Issue(
                        Severity.Error,
                        "BT055",
                        $"Parameter type mismatch. key={def.Key}, actual={actual.valueType}, expected={def.ValueType}",
                        node.id));
                }
            }
        }

        /// <summary>
        /// Threat, 거리, 스킬 및 Leash 노드의 의미상 잘못된 설정을 검사합니다.
        /// </summary>
        private static void ValidateCombatNodeSemantics(BtNodeRecord node, List<Issue> issues)
        {
            switch (node.typeId)
            {
                case BtTypeIds.Condition.HasAggroTarget:
                    issues.Add(new Issue(Severity.Info, "BT060", "HasAggroTarget is a legacy node. Use HasCombatTarget for new trees.", node.id));
                    break;

                case BtTypeIds.Action.ClearAggro:
                    issues.Add(new Issue(Severity.Info, "BT061", "ClearAggro is a legacy node. Use ReleaseCombatTarget for new trees.", node.id));
                    break;

                case BtTypeIds.Action.MoveToTarget:
                    issues.Add(new Issue(Severity.Info, "BT062", "MoveToTarget is a legacy approach node. Prefer MoveToPreferredRange or MoveToSkillRange.", node.id));
                    break;

                case BtTypeIds.Condition.CanUseSkill:
                case BtTypeIds.Condition.IsSkillInCastRange:
                case BtTypeIds.Action.MoveToSkillRange:
                case BtTypeIds.Action.UseSkill:
                case BtTypeIds.Action.UseSkillAndWait:
                    if (!BtParamValue.TryGetInt(node.parameters, "skillUid", out int skillUid) || skillUid <= 0)
                    {
                        issues.Add(new Issue(Severity.Error, "BT063", "skillUid must be greater than 0.", node.id));
                    }

                    if ((node.typeId == BtTypeIds.Action.UseSkill || node.typeId == BtTypeIds.Action.UseSkillAndWait) &&
                        (!BtParamValue.TryGetBool(node.parameters, "validateCastRange", out bool validateCastRange) || !validateCastRange))
                    {
                        issues.Add(new Issue(
                            Severity.Warning,
                            "BT064",
                            "Automatic CastRange validation is disabled. Add IsSkillInCastRange before this node or enable validateCastRange.",
                            node.id));
                    }
                    break;

                case BtTypeIds.Action.BeginEvade:
                    if (BtParamValue.TryGetEnumString(node.parameters, "trigger", out string triggerText) &&
                        !string.IsNullOrEmpty(triggerText) &&
                        !Enum.TryParse(triggerText, true, out GGemCo2DCore.MonsterLeashTrigger _))
                    {
                        issues.Add(new Issue(Severity.Error, "BT065", $"Invalid leash trigger: {triggerText}", node.id));
                    }
                    break;
            }
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
