#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using GGemCo2DAiBt;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace GGemCo2DAiBtEditor
{
    /// <summary>
    /// BT 그래프 편집 뷰.
    /// - 저장 데이터는 <see cref="MonsterBehaviorTreeAsset"/> / <see cref="BtNodeRecord"/>에 기록된다.
    /// - GraphView는 UI 레이어이며, 데이터 모델과 분리되어야 한다.
    /// </summary>
    public sealed class BtGraphView : GraphView
    {
        private const string ClipboardPrefix = "GGEMCO_BT_CLIPBOARD:";
        private const float RightPanStartThreshold = 4f;
        private const float DefaultPasteOffsetStep = 30f;

        private readonly CreateBtWindow _window;
        private MonsterBehaviorTreeAsset _asset;

        public event Action<string> OnSelectionChanged;

        private readonly Dictionary<string, BtNodeView> _views = new(StringComparer.Ordinal);

        private string _lastSelectedNodeId;

        // GraphView는 포커스를 잃을 때 selection이 비워지는 경우가 있어(특히 Inspector 편집 중),
        // '사용자가 그래프 배경을 클릭해 선택 해제를 의도한 경우'에만 null 선택을 반영한다.
        private bool _clearSelectionRequested;

        // PopulateFromAsset() 등 내부 리빌드 과정에서 발생하는 GraphViewChange를 데이터 삭제로 처리하지 않도록 억제한다.
        private bool _suppressGraphViewChanges;
        
        // 마우스 우클릭 드래그
        private bool _isRightPanning;
        private bool _rightMousePressed;
        private bool _suppressContextMenuOnce;
        private Vector2 _rightMouseDownPos;
        private Vector2 _lastMousePos;
        private Vector2 _lastPasteAnchorGraphPos;
        private int _pasteSerial;
        
        public BtGraphView(CreateBtWindow window)
        {
            _window = window;

            Insert(0, new GridBackground());
            this.AddManipulator(new ContentZoomer());
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            focusable = true;

            graphViewChanged += OnGraphViewChanged;

            // 배경 클릭 시에만 선택 해제(null selection)를 인정한다.
            RegisterCallback<MouseDownEvent>(OnMouseDown, TrickleDown.TrickleDown);
            RegisterCallback<MouseMoveEvent>(OnMouseMove, TrickleDown.TrickleDown);
            RegisterCallback<MouseUpEvent>(OnMouseUp, TrickleDown.TrickleDown);
            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            Focus();
            _lastMousePos = evt.mousePosition;

            if (evt.button == 0)
            {
                // Node/Port를 클릭한 경우는 selection이 유지/변경되므로 별도 처리 불필요.
                // 배경(GridBackground, contentViewContainer, GraphView itself)을 클릭한 경우에만
                // 다음 PollSelectionChange에서 null selection을 반영하도록 플래그를 켠다.
                var ve = evt.target as VisualElement;
                _clearSelectionRequested = IsBackgroundElement(ve);
                return;
            }

            if (evt.button == 1)
            {
                _rightMousePressed = true;
                _isRightPanning = false;
                _suppressContextMenuOnce = false;
                _rightMouseDownPos = evt.mousePosition;
                _lastMousePos = evt.mousePosition;
            }
        }

        private void OnMouseMove(MouseMoveEvent evt)
        {
            var prevMousePos = _lastMousePos;
            _lastMousePos = evt.mousePosition;

            if (!_rightMousePressed)
                return;

            if (!_isRightPanning)
            {
                float distance = Vector2.Distance(evt.mousePosition, _rightMouseDownPos);
                if (distance >= RightPanStartThreshold)
                {
                    _isRightPanning = true;
                    _suppressContextMenuOnce = true;
                }
            }

            if (!_isRightPanning)
                return;

            Vector2 delta = evt.mousePosition - prevMousePos;
            viewTransform.position += (Vector3)delta;
            _lastMousePos = evt.mousePosition;

            evt.StopImmediatePropagation();
        }

        private void OnMouseUp(MouseUpEvent evt)
        {
            _lastMousePos = evt.mousePosition;

            if (evt.button != 1)
                return;

            if (_isRightPanning)
                evt.StopImmediatePropagation();

            _rightMousePressed = false;
            _isRightPanning = false;
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            bool isActionKey = evt.ctrlKey || evt.commandKey;
            if (!isActionKey)
                return;

            switch (evt.keyCode)
            {
                case KeyCode.C:
                    if (CopySelectionToClipboard())
                        evt.StopImmediatePropagation();
                    break;

                case KeyCode.V:
                    if (PasteFromClipboard(GetNextPasteAnchorGraphPos()))
                        evt.StopImmediatePropagation();
                    break;

                case KeyCode.D:
                    if (DuplicateSelection())
                        evt.StopImmediatePropagation();
                    break;

                case KeyCode.Z:
                    if (evt.shiftKey)
                        Undo.PerformRedo();
                    else
                        Undo.PerformUndo();
                    evt.StopImmediatePropagation();
                    break;

                case KeyCode.Y:
                    Undo.PerformRedo();
                    evt.StopImmediatePropagation();
                    break;
            }
        }

        private bool IsBackgroundElement(VisualElement ve)
        {
            if (ve == null) return false;

            // GraphView 자체 또는 contentViewContainer(캔버스 영역)
            if (ReferenceEquals(ve, this) || ReferenceEquals(ve, contentViewContainer))
                return true;

            // GridBackground 등 배경 계층
            if (ve is GridBackground)
                return true;

            // 다른 배경 요소(버전에 따라 타입이 달라질 수 있음)
            // - Node/Port/Edge 등이 아닌 요소를 폭넓게 허용하되,
            //   title/port 등 노드 내부 클릭은 제외한다.
            if (ve.ClassListContains("unity-grid-background"))
                return true;

            return false;
        }

        /// <summary>
        /// 지정 노드를 GraphView에서 선택한다.
        /// </summary>
        public void SelectNode(string nodeId, bool frame = false)
        {
            if (string.IsNullOrEmpty(nodeId)) return;
            if (!_views.TryGetValue(nodeId, out var view)) return;

            ClearSelection();
            AddToSelection(view);
            if (frame) FrameSelection();
        }

        /// <summary>
        /// 데이터는 유지한 채, 노드 뷰(표시)만 갱신한다.
        /// </summary>
        public void RefreshNodeView(string nodeId)
        {
            if (_asset == null) return;
            if (string.IsNullOrEmpty(nodeId)) return;
            if (!_views.TryGetValue(nodeId, out var view)) return;

            var record = _asset.FindNode(nodeId);
            if (record == null) return;

            view.RefreshFromRecord(record);
        }

        /// <summary>
        /// GraphView 선택 변경을 폴링 기반으로 감지한다.
        /// (GraphView API가 버전별로 달라 selectionChanged/OnSelectionChange가 없을 수 있음)
        /// </summary>
        public void PollSelectionChange()
        {
            var node = selection?.OfType<BtNodeView>().FirstOrDefault();
            var id = node != null ? node.NodeId : null;

            // Inspector 편집 등으로 일시적으로 selection이 비는 상황에서는
            // 사용자 의도(배경 클릭)가 확인되기 전까지 기존 선택을 유지한다.
            if (id == null && _lastSelectedNodeId != null && !_clearSelectionRequested)
                return;

            // null selection 반영 후에는 플래그를 즉시 리셋한다.
            if (id == null)
                _clearSelectionRequested = false;

            if (id == _lastSelectedNodeId) return;
            _lastSelectedNodeId = id;
            OnSelectionChanged?.Invoke(id);
        }

        public void SetAsset(MonsterBehaviorTreeAsset asset) => _asset = asset;

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var compatible = new List<Port>();
            foreach (var port in ports.ToList())
            {
                if (port == startPort) continue;
                if (port.node == startPort.node) continue;
                if (port.direction == startPort.direction) continue;

                var outputNode = startPort.direction == Direction.Output ? startPort.node as BtNodeView : port.node as BtNodeView;
                var inputNode = startPort.direction == Direction.Input ? startPort.node as BtNodeView : port.node as BtNodeView;
                if (outputNode == null || inputNode == null)
                    continue;

                if (_asset == null)
                    continue;

                if (inputNode.NodeId == _asset.rootNodeId)
                    continue;

                if (WouldCreateCycle(outputNode.NodeId, inputNode.NodeId))
                    continue;

                var childRecord = _asset.FindNode(inputNode.NodeId);
                int parentCount = GetParentCount(inputNode.NodeId);
                int maxParentCount = BtNodeParentPolicy.GetMaxParentCount(childRecord);
                bool isAlreadyLinked = IsEdgeAlreadyLinked(outputNode.NodeId, inputNode.NodeId);
                if (!isAlreadyLinked && maxParentCount >= 0 && parentCount >= maxParentCount)
                    continue;

                compatible.Add(port);
            }
            return compatible;
        }

        private bool IsEdgeAlreadyLinked(string parentNodeId, string childNodeId)
        {
            var parent = _asset?.FindNode(parentNodeId);
            return parent != null && parent.children.Contains(childNodeId);
        }

        private int GetParentCount(string childNodeId)
        {
            if (_asset == null || string.IsNullOrEmpty(childNodeId))
                return 0;

            int count = 0;
            foreach (var node in _asset.nodes)
            {
                if (node?.children == null)
                    continue;
                for (int i = 0; i < node.children.Count; i++)
                {
                    if (node.children[i] == childNodeId)
                        count++;
                }
            }
            return count;
        }

        private bool WouldCreateCycle(string parentNodeId, string childNodeId)
        {
            if (string.IsNullOrEmpty(parentNodeId) || string.IsNullOrEmpty(childNodeId) || _asset == null)
                return false;
            if (parentNodeId == childNodeId)
                return true;

            var visited = new HashSet<string>(StringComparer.Ordinal);
            return WouldReachNode(childNodeId, parentNodeId, visited);
        }

        private bool WouldReachNode(string currentNodeId, string targetNodeId, HashSet<string> visited)
        {
            if (string.IsNullOrEmpty(currentNodeId) || string.IsNullOrEmpty(targetNodeId) || visited == null)
                return false;
            if (!visited.Add(currentNodeId))
                return false;
            if (currentNodeId == targetNodeId)
                return true;

            var node = _asset?.FindNode(currentNodeId);
            if (node?.children == null)
                return false;

            for (int i = 0; i < node.children.Count; i++)
            {
                if (WouldReachNode(node.children[i], targetNodeId, visited))
                    return true;
            }

            return false;
        }

        public void PopulateFromAsset()
        {
            // 리빌드 전 선택 상태를 보존해 둔다.
            var selectedId = selection?.OfType<BtNodeView>().FirstOrDefault()?.NodeId;

            _suppressGraphViewChanges = true;
            try
            {
                DeleteElements(graphElements.ToList());
                _views.Clear();

                if (_asset == null) return;

                foreach (var n in _asset.nodes)
                {
                    if (n == null || string.IsNullOrEmpty(n.id)) continue;

                    var view = new BtNodeView(n);
                    var size = n.graphSize.sqrMagnitude > 0.0001f ? n.graphSize : new Vector2(220, 140);
                    view.SetPosition(new Rect(n.graphPosition, size));
                    AddElement(view);
                    _views[n.id] = view;
                }

                // edges
                foreach (var parent in _asset.nodes)
                {
                    if (parent == null) continue;
                    if (!_views.TryGetValue(parent.id, out var pv)) continue;
                    if (pv.OutPort == null) continue;

                    for (int i = 0; i < parent.children.Count; i++)
                    {
                        var cid = parent.children[i];
                        if (!_views.TryGetValue(cid, out var cv)) continue;

                        AddElement(pv.OutPort.ConnectTo(cv.InPort));
                    }
                }

                // root badge
                foreach (var kv in _views)
                    kv.Value.MarkAsRoot(false);

                if (!string.IsNullOrEmpty(_asset.rootNodeId) && _views.TryGetValue(_asset.rootNodeId, out var root))
                    root.MarkAsRoot(true);
            }
            finally
            {
                _suppressGraphViewChanges = false;
            }

            // 리빌드 후 선택 복원(가능한 경우)
            if (!string.IsNullOrEmpty(selectedId))
                SelectNode(selectedId, frame: false);
        }

        private void ShowCreateNodeMenu(Vector2 graphPos)
        {
            var menu = new GenericMenu();

            if (_asset == null)
            {
                menu.AddDisabledItem(new GUIContent("Select a Tree Asset first"));
                menu.ShowAsContext();
                return;
            }

            foreach (var def in BtNodeTypeCatalog.All)
                menu.AddItem(new GUIContent($"{def.Kind}/{def.DisplayName}"), false, () => CreateNode(def, graphPos));

            menu.ShowAsContext();
        }

        private void CreateNode(BtNodeTypeDef def, Vector2 graphPos)
        {
            if (_asset == null) return;

            BtUndoUtility.RecordComplete(_asset, "Create BT Node");

            var n = new BtNodeRecord
            {
                id = Guid.NewGuid().ToString("N"),
                kind = def.Kind,
                typeId = def.TypeId,
                title = def.DisplayName,
                graphPosition = graphPos,
                graphSize = new Vector2(220, 140),
            };

            // typeId 기반 파라미터 기본값 자동 주입
            BtEditorParamUtility.EnsureParams(n, _asset);

            _asset.nodes.Add(n);

            if (string.IsNullOrEmpty(_asset.rootNodeId))
                _asset.rootNodeId = n.id;

            BtUndoUtility.SetDirty(_asset);

            PopulateFromAsset();
            _window.NotifyTreeChanged("Node created.", repopulateGraph: false, refreshInspector: true, selectNodeId: n.id);

            if (_views.TryGetValue(n.id, out var view))
            {
                ClearSelection();
                AddToSelection(view);
            }
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (_asset == null) return change;
            if (_suppressGraphViewChanges) return change;

            // Move node → save graphPosition/size
            if (change.movedElements != null)
            {
                BtUndoUtility.RecordDelta(_asset, "Move BT Node");

                foreach (var nv in change.movedElements.OfType<BtNodeView>())
                {
                    var node = _asset.FindNode(nv.NodeId);
                    if (node == null) continue;
                    var rect = nv.GetPosition();
                    node.graphPosition = rect.position;
                    node.graphSize = rect.size;
                }

                BtUndoUtility.SetDirty(_asset);
                _window.MarkDirty("Node moved.");
            }

            // Create edges → update children
            if (change.edgesToCreate != null && change.edgesToCreate.Count > 0)
            {
                BtUndoUtility.RecordComplete(_asset, "Create BT Edge");

                foreach (var e in change.edgesToCreate)
                {
                    if (!TryAddEdgeToAsset(e, out var reason))
                    {
                        // 규칙 위반 엣지는 즉시 제거
                        RemoveElement(e);
                        _window.MarkDirty(reason);
                    }
                }

                BtUndoUtility.SetDirty(_asset);
                _window.NotifyTreeChanged("Edge created.", repopulateGraph: false, refreshInspector: true);
            }

            // Remove elements → update children and/or delete nodes
            if (change.elementsToRemove != null)
            {
                BtUndoUtility.RecordComplete(_asset, "Remove BT Elements");

                foreach (var el in change.elementsToRemove)
                {
                    if (el is Edge edge) RemoveEdgeFromAsset(edge);
                    else if (el is BtNodeView nv) RemoveNodeFromAsset(nv.NodeId);
                }

                BtUndoUtility.SetDirty(_asset);
                _window.NotifyTreeChanged("Elements removed.", repopulateGraph: false, refreshInspector: true);
            }

            return change;
        }

        private bool TryAddEdgeToAsset(Edge edge, out string reason)
        {
            reason = "Edge created.";

            if (edge.output?.node is not BtNodeView parent) { reason = "Invalid edge: parent missing."; return false; }
            if (edge.input?.node is not BtNodeView child) { reason = "Invalid edge: child missing."; return false; }

            var p = _asset.FindNode(parent.NodeId);
            var c = _asset.FindNode(child.NodeId);
            if (p == null) { reason = "Invalid edge: parent node not found."; return false; }
            if (c == null) { reason = "Invalid edge: child node not found."; return false; }

            if (child.NodeId == _asset.rootNodeId)
            {
                reason = "Root node cannot have parents.";
                return false;
            }

            // Condition/Action은 자식 연결 금지
            if (p.kind == BtNodeKind.Condition || p.kind == BtNodeKind.Action)
            {
                reason = "Condition/Action nodes cannot have children.";
                return false;
            }

            if (WouldCreateCycle(parent.NodeId, child.NodeId))
            {
                reason = "This connection would create a cycle.";
                return false;
            }

            if (IsEdgeAlreadyLinked(parent.NodeId, child.NodeId))
            {
                reason = "This edge already exists.";
                return false;
            }

            // Decorator는 자식 1개만
            if (p.kind == BtNodeKind.Decorator)
            {
                p.children.Clear();

                foreach (var ev in edges.ToList())
                {
                    if (ev.output?.node == parent)
                        RemoveElement(ev);
                }
            }

            int maxParentCount = BtNodeParentPolicy.GetMaxParentCount(c);
            int parentCount = GetParentCount(child.NodeId);
            if (maxParentCount >= 0 && parentCount >= maxParentCount)
            {
                reason = BtNodeParentPolicy.GetMultipleParentsBlockedReason(c);
                if (string.IsNullOrEmpty(reason))
                    reason = $"This node allows up to {maxParentCount} parent(s).";
                return false;
            }

            p.children.Add(child.NodeId);

            if (p.typeId == BtTypeIds.Composite.RandomWeighted)
                BtEditorParamUtility.EnsureParams(p, _asset);

            return true;
        }

        private void RemoveEdgeFromAsset(Edge edge)
        {
            if (edge.output?.node is not BtNodeView parent) return;
            if (edge.input?.node is not BtNodeView child) return;

            var p = _asset.FindNode(parent.NodeId);
            if (p == null) return;

            p.children.RemoveAll(x => x == child.NodeId);

            if (p.typeId == BtTypeIds.Composite.RandomWeighted)
                BtEditorParamUtility.EnsureParams(p, _asset);
        }

        public void ApplyDebug(MonsterBtRunner runner)
        {
            foreach (var v in _views.Values)
            {
                v.style.borderLeftWidth = 0;
                v.style.borderRightWidth = 0;
                v.titleContainer.style.backgroundColor = StyleKeyword.Null;
                v.SetDebugInfo(null, null);
                v.SetBreakpointState(false);
            }

            if (runner == null)
                return;

            foreach (var kv in _views)
            {
                if (!runner.TryGetBreakpoint(kv.Key, out var breakpoint))
                    continue;

                kv.Value.SetBreakpointState(true, BuildBreakpointTooltip(breakpoint));
            }

            if (!EditorApplication.isPlaying)
                return;

            var frame = runner.DebugLastFrame;
            if (frame == null)
                return;

            var visitGroups = frame.Visits
                .Where(v => !string.IsNullOrEmpty(v.NodeId))
                .GroupBy(v => v.NodeId)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            foreach (var kv in visitGroups)
            {
                if (!_views.TryGetValue(kv.Key, out var view))
                    continue;

                var status = AggregateStatus(kv.Value.Select(x => x.Status));
                view.style.borderLeftWidth = 4;
                view.style.borderLeftColor = GetStatusColor(status);
            }

            var eventGroups = frame.Events
                .Where(e => !string.IsNullOrEmpty(e.NodeId))
                .GroupBy(e => e.NodeId)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            foreach (var kv in eventGroups)
            {
                if (!_views.TryGetValue(kv.Key, out var view))
                    continue;

                var events = kv.Value;
                int runningCount = events.Count(e => e.Status == BtStatus.Running);
                int successCount = events.Count(e => e.Status == BtStatus.Success);
                int failureCount = events.Count(e => e.Status == BtStatus.Failure);
                int executionCount = events.Select(e => e.ExecutionKey).Where(x => !string.IsNullOrEmpty(x)).Distinct().Count();
                if (executionCount <= 0)
                    executionCount = 1;

                string badge = runningCount > 0 ? $"Run x{executionCount}" : successCount > 0 && failureCount > 0 ? $"Mix x{executionCount}" : successCount > 0 ? $"Suc x{executionCount}" : failureCount > 0 ? $"Fail x{executionCount}" : $"Evt x{executionCount}";

                var groupedByExecution = events
                    .GroupBy(e => string.IsNullOrEmpty(e.ExecutionKey) ? e.NodeId : e.ExecutionKey)
                    .OrderBy(g => g.Key)
                    .ToList();

                var lines = new List<string>();
                foreach (var execution in groupedByExecution)
                {
                    var last = execution.Last();
                    string line = $"{execution.Key} => {last.Status}";
                    if (last.Reason != BtDebugReason.None)
                        line += $" / {last.Reason}";
                    if (!string.IsNullOrEmpty(last.Summary))
                        line += $" | {last.Summary}";
                    lines.Add(line);
                }

                if (runner.TryGetBreakpoint(kv.Key, out var breakpoint))
                    lines.Add($"Breakpoint: {BuildBreakpointTooltip(breakpoint)}");

                view.SetDebugInfo(badge, string.Join("\n", lines));
            }

            var executionPath = runner.DebugActiveExecutionPath;
            var nodePath = runner.DebugActivePath;
            if (nodePath != null)
            {
                for (int i = 0; i < nodePath.Count; i++)
                {
                    var id = nodePath[i];
                    if (string.IsNullOrEmpty(id)) continue;
                    if (!_views.TryGetValue(id, out var view)) continue;

                    view.style.borderRightWidth = 4;
                    view.style.borderRightColor = new Color(1f, 0.75f, 0.2f, 1f);
                    view.titleContainer.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);

                    var currentTip = view.tooltip ?? string.Empty;
                    if (executionPath != null && i < executionPath.Count && !string.IsNullOrEmpty(executionPath[i]))
                    {
                        string activeLine = $"ActivePath: {executionPath[i]}";
                        view.tooltip = string.IsNullOrEmpty(currentTip) ? activeLine : currentTip + "\n" + activeLine;
                    }
                }
            }
        }

        private static BtStatus AggregateStatus(IEnumerable<BtStatus> statuses)
        {
            bool hasRunning = false;
            bool hasFailure = false;
            bool hasSuccess = false;

            foreach (var status in statuses)
            {
                if (status == BtStatus.Running) hasRunning = true;
                else if (status == BtStatus.Failure) hasFailure = true;
                else if (status == BtStatus.Success) hasSuccess = true;
            }

            if (hasRunning) return BtStatus.Running;
            if (hasFailure) return BtStatus.Failure;
            if (hasSuccess) return BtStatus.Success;
            return BtStatus.Failure;
        }

        private static Color GetStatusColor(BtStatus status)
        {
            return status switch
            {
                BtStatus.Success => new Color(0.2f, 0.8f, 0.2f, 1f),
                BtStatus.Failure => new Color(0.9f, 0.2f, 0.2f, 1f),
                BtStatus.Running => new Color(0.2f, 0.8f, 0.9f, 1f),
                _ => new Color(0.9f, 0.9f, 0.2f, 1f),
            };
        }

        private static string BuildBreakpointTooltip(BtDebugBreakpoint breakpoint)
        {
            var parts = new List<string>();
            if (breakpoint.BreakOnVisit) parts.Add("Visit");
            if (breakpoint.BreakOnSuccess) parts.Add("Success");
            if (breakpoint.BreakOnFailure) parts.Add("Failure");
            if (breakpoint.BreakOnRunning) parts.Add("Running");
            return parts.Count > 0 ? string.Join(", ", parts) : "None";
        }

        private void RemoveNodeFromAsset(string nodeId)
        {
            _asset.nodes.RemoveAll(n => n != null && n.id == nodeId);

            foreach (var n in _asset.nodes)
                if (n != null) n.children.RemoveAll(c => c == nodeId);

            if (_asset.rootNodeId == nodeId)
                _asset.rootNodeId = _asset.nodes.Count > 0 ? _asset.nodes[0].id : string.Empty;
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (_suppressContextMenuOnce)
            {
                _suppressContextMenuOnce = false;
                return;
            }

            base.BuildContextualMenu(evt);

            if (_asset == null)
            {
                evt.menu.AppendAction("Select a Tree Asset first", _ => { }, DropdownMenuAction.Status.Disabled);
                return;
            }

            var graphPos = contentViewContainer.WorldToLocal(evt.mousePosition);
            _lastMousePos = contentViewContainer.WorldToLocal(evt.mousePosition);

            evt.menu.AppendSeparator();
            evt.menu.AppendAction(
                "Copy Selected",
                _ => CopySelectionToClipboard(),
                _ => GetSelectedNodeViews().Count > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction(
                "Paste",
                _ => PasteFromClipboard(graphPos),
                _ => CanPasteClipboard(EditorGUIUtility.systemCopyBuffer) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction(
                "Duplicate Selected",
                _ => DuplicateSelection(),
                _ => GetSelectedNodeViews().Count > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendSeparator();

            foreach (var def in BtNodeTypeCatalog.All)
            {
                evt.menu.AppendAction(
                    $"{def.Kind}/{def.DisplayName}",
                    _ => CreateNode(def, graphPos)
                );
            }
        }

        private List<BtNodeView> GetSelectedNodeViews()
        {
            if (_asset == null)
                return new List<BtNodeView>();

            var selectedIds = new HashSet<string>(selection?.OfType<BtNodeView>().Select(x => x.NodeId) ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            if (selectedIds.Count == 0)
                return new List<BtNodeView>();

            var ordered = new List<BtNodeView>();
            foreach (var node in _asset.nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.id))
                    continue;

                if (!selectedIds.Contains(node.id))
                    continue;

                if (_views.TryGetValue(node.id, out var view))
                    ordered.Add(view);
            }

            return ordered;
        }

        private BtClipboardData BuildClipboardDataFromSelection()
        {
            var selectedViews = GetSelectedNodeViews();
            if (selectedViews.Count == 0 || _asset == null)
                return null;

            var selectedIds = new HashSet<string>(selectedViews.Select(x => x.NodeId), StringComparer.Ordinal);
            var data = new BtClipboardData
            {
                schemaVersion = "1.0.0",
                sourceTreeGuid = _asset.treeGuid,
            };

            foreach (var view in selectedViews)
            {
                var node = _asset.FindNode(view.NodeId);
                if (node == null)
                    continue;

                var item = new BtClipboardNodeData
                {
                    oldId = node.id,
                    kind = node.kind,
                    typeId = node.typeId,
                    title = node.title,
                    abortMode = node.abortMode,
                    comment = node.comment,
                    colorIndex = node.colorIndex,
                    graphPosition = node.graphPosition,
                    graphSize = node.graphSize,
                    parameters = CloneParameters(node.parameters),
                };

                if (node.children != null)
                {
                    foreach (var childId in node.children)
                    {
                        if (!string.IsNullOrEmpty(childId) && selectedIds.Contains(childId))
                            item.children.Add(childId);
                    }
                }

                data.nodes.Add(item);
            }

            return data.nodes.Count > 0 ? data : null;
        }

        private bool CopySelectionToClipboard()
        {
            if (_asset == null)
            {
                _window.MarkDirty("No tree asset selected.");
                return false;
            }

            var data = BuildClipboardDataFromSelection();
            if (data == null)
            {
                _window.MarkDirty("No nodes selected to copy.");
                return false;
            }

            EditorGUIUtility.systemCopyBuffer = ClipboardPrefix + JsonUtility.ToJson(data);
            _window.MarkDirty($"Copied {data.nodes.Count} node(s).");
            return true;
        }

        private bool CanPasteClipboard(string raw)
        {
            return TryDeserializeClipboard(raw, out _);
        }

        private bool PasteFromClipboard(Vector2 anchorGraphPos)
        {
            if (!TryDeserializeClipboard(EditorGUIUtility.systemCopyBuffer, out var data))
            {
                _window.MarkDirty("Clipboard does not contain valid BT node data.");
                return false;
            }

            return PasteClipboardData(data, anchorGraphPos, incrementSerial: true);
        }

        private bool DuplicateSelection()
        {
            if (_asset == null)
            {
                _window.MarkDirty("No tree asset selected.");
                return false;
            }

            var data = BuildClipboardDataFromSelection();
            if (data == null)
            {
                _window.MarkDirty("No nodes selected to duplicate.");
                return false;
            }

            return PasteClipboardData(data, GetNextPasteAnchorGraphPos(), incrementSerial: true, copiedByDuplicate: true);
        }

        private bool PasteClipboardData(BtClipboardData data, Vector2 anchorGraphPos, bool incrementSerial, bool copiedByDuplicate = false)
        {
            if (_asset == null)
            {
                _window.MarkDirty("No tree asset selected.");
                return false;
            }

            if (data?.nodes == null || data.nodes.Count == 0)
            {
                _window.MarkDirty("Clipboard data is empty.");
                return false;
            }

            var copiedNodes = data.nodes.Where(x => x != null && !string.IsNullOrEmpty(x.oldId)).ToList();
            if (copiedNodes.Count == 0)
            {
                _window.MarkDirty("Clipboard data is empty.");
                return false;
            }

            Vector2 minPos = copiedNodes[0].graphPosition;
            for (int i = 1; i < copiedNodes.Count; i++)
            {
                minPos = Vector2.Min(minPos, copiedNodes[i].graphPosition);
            }

            Vector2 finalAnchor = anchorGraphPos;
            if (incrementSerial)
            {
                finalAnchor += Vector2.one * (DefaultPasteOffsetStep * _pasteSerial);
            }

            BtUndoUtility.RecordComplete(_asset, copiedByDuplicate ? "Duplicate BT Nodes" : "Paste BT Nodes");

            var oldToNewId = new Dictionary<string, string>(StringComparer.Ordinal);
            var addedNodeIds = new List<string>(copiedNodes.Count);

            foreach (var source in copiedNodes)
            {
                var newId = Guid.NewGuid().ToString("N");
                oldToNewId[source.oldId] = newId;
            }

            foreach (var source in copiedNodes)
            {
                var relativePos = source.graphPosition - minPos;
                var newNode = new BtNodeRecord
                {
                    id = oldToNewId[source.oldId],
                    kind = source.kind,
                    typeId = source.typeId,
                    title = source.title,
                    abortMode = source.abortMode,
                    comment = source.comment,
                    colorIndex = source.colorIndex,
                    graphPosition = finalAnchor + relativePos,
                    graphSize = source.graphSize.sqrMagnitude > 0.0001f ? source.graphSize : new Vector2(220, 140),
                    parameters = CloneParameters(source.parameters),
                    children = new List<string>(),
                };

                if (source.children != null)
                {
                    foreach (var oldChildId in source.children)
                    {
                        if (string.IsNullOrEmpty(oldChildId))
                            continue;

                        if (oldToNewId.TryGetValue(oldChildId, out var newChildId))
                            newNode.children.Add(newChildId);
                    }
                }

                NormalizePastedNode(newNode);
                _asset.nodes.Add(newNode);
                addedNodeIds.Add(newNode.id);
            }

            BtUndoUtility.SetDirty(_asset);
            PopulateFromAsset();
            SelectNodes(addedNodeIds, frame: true);

            _lastPasteAnchorGraphPos = finalAnchor;
            if (incrementSerial)
                _pasteSerial++;

            _window.NotifyTreeChanged($"Pasted {addedNodeIds.Count} node(s).", repopulateGraph: false, refreshInspector: true, selectNodeId: addedNodeIds.Count == 1 ? addedNodeIds[0] : null);
            return true;
        }

        private static void NormalizePastedNode(BtNodeRecord node)
        {
            if (node == null)
                return;

            node.children ??= new List<string>();
            node.parameters = CloneParameters(node.parameters);

            switch (node.kind)
            {
                case BtNodeKind.Action:
                case BtNodeKind.Condition:
                    node.children.Clear();
                    break;

                case BtNodeKind.Decorator:
                    if (node.children.Count > 1)
                        node.children = node.children.Take(1).ToList();
                    break;
            }

            BtEditorParamUtility.EnsureParams(node, null);
        }

        private bool TryDeserializeClipboard(string raw, out BtClipboardData data)
        {
            data = null;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            if (!raw.StartsWith(ClipboardPrefix, StringComparison.Ordinal))
                return false;

            var json = raw.Substring(ClipboardPrefix.Length);
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                data = JsonUtility.FromJson<BtClipboardData>(json);
            }
            catch
            {
                data = null;
                return false;
            }

            return data?.nodes != null && data.nodes.Count > 0;
        }

        private void SelectNodes(IReadOnlyList<string> nodeIds, bool frame)
        {
            if (nodeIds == null || nodeIds.Count == 0)
                return;

            ClearSelection();
            foreach (var nodeId in nodeIds)
            {
                if (string.IsNullOrEmpty(nodeId))
                    continue;

                if (_views.TryGetValue(nodeId, out var view))
                    AddToSelection(view);
            }

            if (frame && selection != null && selection.Count > 0)
                FrameSelection();
        }

        private Vector2 GetNextPasteAnchorGraphPos()
        {
            var mouseGraphPos = this.ChangeCoordinatesTo(contentViewContainer, _lastMousePos);
            if (!float.IsNaN(mouseGraphPos.x) && !float.IsNaN(mouseGraphPos.y))
                return mouseGraphPos;

            return GetViewportCenterGraphPosition();
        }

        private Vector2 GetViewportCenterGraphPosition()
        {
            Vector2 localCenter = layout.size * 0.5f;
            return this.ChangeCoordinatesTo(contentViewContainer, localCenter);
        }

        private static List<BtParamValue> CloneParameters(List<BtParamValue> source)
        {
            if (source == null || source.Count == 0)
                return new List<BtParamValue>();

            var cloned = new List<BtParamValue>(source.Count);
            for (int i = 0; i < source.Count; i++)
                cloned.Add(source[i]);

            return cloned;
        }
    }
}
#endif
