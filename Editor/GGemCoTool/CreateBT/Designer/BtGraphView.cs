#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using GGemCo2DAiBt;
using GGemCo2DAiBt.Editor;
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
        private const float RightPanStartThreshold = 4f;
        
        public BtGraphView(CreateBtWindow window)
        {
            _window = window;

            Insert(0, new GridBackground());
            this.AddManipulator(new ContentZoomer());
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);

            graphViewChanged += OnGraphViewChanged;

            // 배경 클릭 시에만 선택 해제(null selection)를 인정한다.
            RegisterCallback<MouseDownEvent>(OnMouseDown, TrickleDown.TrickleDown);
            RegisterCallback<MouseMoveEvent>(OnMouseMove, TrickleDown.TrickleDown);
            RegisterCallback<MouseUpEvent>(OnMouseUp, TrickleDown.TrickleDown);
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
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

            Vector2 delta = evt.mousePosition - _lastMousePos;
            viewTransform.position += (Vector3)delta;
            _lastMousePos = evt.mousePosition;

            evt.StopImmediatePropagation();
        }
        private void OnMouseUp(MouseUpEvent evt)
        {
            if (evt.button != 1)
                return;

            if (_isRightPanning)
                evt.StopImmediatePropagation();

            _rightMousePressed = false;
            _isRightPanning = false;
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

                // Condition/Action은 output 포트가 없으므로 여기서 추가 제약은 최소화한다.
                compatible.Add(port);
            }
            return compatible;
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

            Undo.RecordObject(_asset, "Create BT Node");

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

            EditorUtility.SetDirty(_asset);

            PopulateFromAsset();
            _window.MarkDirty("Node created.");

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
                Undo.RecordObject(_asset, "Move BT Node");

                foreach (var nv in change.movedElements.OfType<BtNodeView>())
                {
                    var node = _asset.FindNode(nv.NodeId);
                    if (node == null) continue;
                    var rect = nv.GetPosition();
                    node.graphPosition = rect.position;
                    node.graphSize = rect.size;
                }

                EditorUtility.SetDirty(_asset);
                _window.MarkDirty("Node moved.");
            }

            // Create edges → update children
            if (change.edgesToCreate != null && change.edgesToCreate.Count > 0)
            {
                Undo.RecordObject(_asset, "Create BT Edge");

                foreach (var e in change.edgesToCreate)
                {
                    if (!TryAddEdgeToAsset(e, out var reason))
                    {
                        // 규칙 위반 엣지는 즉시 제거
                        RemoveElement(e);
                        _window.MarkDirty(reason);
                    }
                }

                EditorUtility.SetDirty(_asset);
            }

            // Remove elements → update children and/or delete nodes
            if (change.elementsToRemove != null)
            {
                Undo.RecordObject(_asset, "Remove BT Elements");

                foreach (var el in change.elementsToRemove)
                {
                    if (el is Edge edge) RemoveEdgeFromAsset(edge);
                    else if (el is BtNodeView nv) RemoveNodeFromAsset(nv.NodeId);
                }

                EditorUtility.SetDirty(_asset);
                _window.MarkDirty("Elements removed.");
            }

            return change;
        }

        private bool TryAddEdgeToAsset(Edge edge, out string reason)
        {
            reason = "Edge created.";

            if (edge.output?.node is not BtNodeView parent) { reason = "Invalid edge: parent missing."; return false; }
            if (edge.input?.node is not BtNodeView child) { reason = "Invalid edge: child missing."; return false; }

            var p = _asset.FindNode(parent.NodeId);
            if (p == null) { reason = "Invalid edge: parent node not found."; return false; }

            // Condition/Action은 자식 연결 금지
            if (p.kind == BtNodeKind.Condition || p.kind == BtNodeKind.Action)
            {
                reason = "Condition/Action nodes cannot have children.";
                return false;
            }

            // Decorator는 자식 1개만
            if (p.kind == BtNodeKind.Decorator)
            {
                // 기존 연결 제거(데이터 + UI)
                p.children.Clear();

                foreach (var ev in edges.ToList())
                {
                    if (ev.output?.node == parent)
                        RemoveElement(ev);
                }
            }

            if (!p.children.Contains(child.NodeId))
                p.children.Add(child.NodeId);

            reason = "Edge created.";
            return true;
        }

        private void RemoveEdgeFromAsset(Edge edge)
        {
            if (edge.output?.node is not BtNodeView parent) return;
            if (edge.input?.node is not BtNodeView child) return;

            var p = _asset.FindNode(parent.NodeId);
            if (p == null) return;

            p.children.RemoveAll(x => x == child.NodeId);
        }

        public void ApplyDebug(MonsterBtRunner runner)
        {
            // Reset
            foreach (var v in _views.Values)
            {
                v.style.borderLeftWidth = 0;
                v.style.borderRightWidth = 0;
                v.titleContainer.style.backgroundColor = StyleKeyword.Null;
            }

            if (runner == null) return;
            if (!EditorApplication.isPlaying) return;

            // Visited nodes (status color)
            var visited = runner.DebugLastTick;
            if (visited != null)
            {
                for (int i = 0; i < visited.Count; i++)
                {
                    var r = visited[i];
                    if (string.IsNullOrEmpty(r.NodeId)) continue;
                    if (!_views.TryGetValue(r.NodeId, out var v)) continue;

                    v.style.borderLeftWidth = 4;
                    v.style.borderLeftColor = r.Status switch
                    {
                        BtStatus.Success => new Color(0.2f, 0.8f, 0.2f, 1f),
                        BtStatus.Failure => new Color(0.9f, 0.2f, 0.2f, 1f),
                        BtStatus.Running => new Color(0.2f, 0.8f, 0.9f, 1f),
                        _ => new Color(0.9f, 0.9f, 0.2f, 1f),
                    };
                }
            }

            // Active path highlight
            var path = runner.DebugActivePath;
            if (path != null)
            {
                for (int i = 0; i < path.Count; i++)
                {
                    var id = path[i];
                    if (string.IsNullOrEmpty(id)) continue;
                    if (!_views.TryGetValue(id, out var v)) continue;

                    v.titleContainer.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
                }
            }
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

            foreach (var def in BtNodeTypeCatalog.All)
            {
                evt.menu.AppendAction(
                    $"{def.Kind}/{def.DisplayName}",
                    _ => CreateNode(def, graphPos)
                );
            }
        }
    }
}
#endif
