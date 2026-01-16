#if UNITY_EDITOR
using System.Linq;
using GGemCo2DAiBt;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GGemCo2DAiBtEditor
{
    /// <summary>
    /// GraphView + UI Toolkit 기반 BT 디자이너(실전용 v1.1).
    /// - 자식 순서: ReorderableList(드래그&드롭)로 정렬 지원
    /// - 파라미터: typeId 기반 ParamDef로 자동 UI 생성
    /// - 디버그: Runner Attach 후 조건 평가 값(거리/HP 등) 표시 + 실행 경로 하이라이트
    /// </summary>
    public sealed class MonsterBtDesignerWindow : EditorWindow
    {
        private MonsterBehaviorTreeAsset _asset;

        private BtGraphView _graphView;
        private VisualElement _inspectorRoot;
        private Label _statusLabel;

        private string _selectedNodeId;

        // Debug attach
        private MonsterBtRunner _runner;

        // Children reorder
        private ReorderableList _childrenReorder;
        private IMGUIContainer _childrenReorderContainer;

        [MenuItem("GGemCo/AI/BT Designer")]
        public static void OpenMenu() => Open(null);

        public static void Open(MonsterBehaviorTreeAsset asset)
        {
            var wnd = GetWindow<MonsterBtDesignerWindow>();
            wnd.titleContent = new GUIContent("Monster BT Designer");
            wnd.minSize = new Vector2(1080, 640);
            wnd.SetAsset(asset);
            wnd.Show();
        }

        private void SetAsset(MonsterBehaviorTreeAsset asset)
        {
            _asset = asset;

            if (_graphView != null)
            {
                _graphView.SetAsset(_asset);
                _graphView.PopulateFromAsset();
                _graphView.ApplyDebug(_runner);
            }

            RefreshInspector(_selectedNodeId);
            UpdateStatus();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;

            // Toolbar
            var toolbar = new Toolbar();

            var assetField = new ObjectField("Tree Asset")
            {
                objectType = typeof(MonsterBehaviorTreeAsset),
                allowSceneObjects = false,
                value = _asset
            };
            assetField.RegisterValueChangedCallback(evt => SetAsset(evt.newValue as MonsterBehaviorTreeAsset));
            toolbar.Add(assetField);

            toolbar.Add(new ToolbarSpacer());

            toolbar.Add(new ToolbarButton(() =>
            {
                if (_asset == null) return;
                EditorUtility.SetDirty(_asset);
                AssetDatabase.SaveAssets();
                UpdateStatus("Saved.");
            }) { text = "Save" });

            toolbar.Add(new ToolbarButton(() =>
            {
                if (_asset == null) return;
                MonsterBtValidator.ValidateAndLog(_asset);
                UpdateStatus("Validated.");
            }) { text = "Validate" });

            toolbar.Add(new ToolbarButton(() => _graphView?.FrameAll()) { text = "Frame All" });

            // Debug attach (Runner)
            var runnerField = new ObjectField("Attach Runner")
            {
                objectType = typeof(MonsterBtRunner),
                allowSceneObjects = true,
                value = _runner
            };
            runnerField.RegisterValueChangedCallback(evt =>
            {
                DetachRunnerEvents();
                _runner = evt.newValue as MonsterBtRunner;
                AttachRunnerEvents();
                UpdateStatus("Runner attached.");
                RefreshInspector(_selectedNodeId);
            });
            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(runnerField);

            root.Add(toolbar);

            // Split
            var body = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Row } };
            root.Add(body);

            _graphView = new BtGraphView(this);
            _graphView.style.flexGrow = 1;
            _graphView.OnSelectionChanged += id =>
            {
                _selectedNodeId = id;
                RefreshInspector(id);
            };
            body.Add(_graphView);


            // GraphView 선택 이벤트가 제공되지 않는 Unity 버전 대응: 주기적으로 selection을 폴링한다.
            root.schedule.Execute(() =>
            {
                _graphView?.PollSelectionChange();
            }).Every(50);

            _inspectorRoot = new VisualElement
            {
                style =
                {
                    width = 360,
                    flexShrink = 0,
                    borderLeftWidth = 1,
                    borderLeftColor = new Color(0,0,0,0.25f),
                    paddingLeft = 10,
                    paddingRight = 10,
                    paddingTop = 8,
                    paddingBottom = 8
                }
            };
            body.Add(_inspectorRoot);

            // Status
            _statusLabel = new Label { style = { paddingLeft = 6, paddingTop = 3, paddingBottom = 3 } };
            root.Add(_statusLabel);

            _graphView.SetAsset(_asset);
            _graphView.PopulateFromAsset();
            _graphView.ApplyDebug(_runner);

            AttachRunnerEvents();
            RefreshInspector(_selectedNodeId);
            UpdateStatus();
        }

        private void OnDisable()
        {
            DetachRunnerEvents();
        }

        private void AttachRunnerEvents()
        {
            if (_runner == null) return;
            _runner.DebugTicked += OnRunnerDebugTicked;
        }

        private void DetachRunnerEvents()
        {
            if (_runner == null) return;
            _runner.DebugTicked -= OnRunnerDebugTicked;
        }

        private void OnRunnerDebugTicked(MonsterBtRunner runner)
        {
            // 실행 경로 하이라이트 업데이트는 GraphView에서 즉시 적용 가능.
            _graphView?.ApplyDebug(_runner);
            // Inspector의 Debug 패널도 최신 메트릭을 보도록 갱신
            RefreshInspector(_selectedNodeId);
        }

        private void RefreshInspector(string selectedNodeId)
        {
            _inspectorRoot.Clear();

            if (_asset == null)
            {
                _inspectorRoot.Add(new Label("Tree Asset를 선택하세요."));
                return;
            }

            if (string.IsNullOrEmpty(selectedNodeId))
            {
                _inspectorRoot.Add(new Label("노드를 선택하면 상세 설정이 표시됩니다."));
                _inspectorRoot.Add(new Button(() =>
                {
                    MonsterBtPresetBuilder.CreateMeleeBasicPreset(_asset);
                    EditorUtility.SetDirty(_asset);
                    _graphView.PopulateFromAsset();
                    _graphView.ApplyDebug(_runner);
                    UpdateStatus("Preset created.");
                }) { text = "Create Example BT" });

                _inspectorRoot.Add(new VisualElement { style = { height = 12 } });
                _inspectorRoot.Add(BuildDebugPanel(null));
                return;
            }

            var node = _asset.FindNode(selectedNodeId);
            if (node == null)
            {
                _inspectorRoot.Add(new Label($"Node not found: {selectedNodeId}"));
                return;
            }

            // Header
            var header = new VisualElement { style = { flexDirection = FlexDirection.Column, marginBottom = 8 } };
            header.Add(new Label(node.title) { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 13 } });
            header.Add(new Label(node.typeId) { style = { opacity = 0.75f } });
            _inspectorRoot.Add(header);

            // Root controls
            var rootRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 8 } };
            var isRoot = _asset.rootNodeId == node.id;
            var rootToggle = new Toggle("Set as Root") { value = isRoot };
            rootToggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    Undo.RecordObject(_asset, "Set BT Root");
                    _asset.rootNodeId = node.id;
                    EditorUtility.SetDirty(_asset);
                    _graphView.PopulateFromAsset();
                    UpdateStatus("Root updated.");
                }
                else
                {
                    // Root 해제는 UX 상 혼란이 많아 허용하지 않음(필요 시 다른 노드를 root로 지정)
                    rootToggle.SetValueWithoutNotify(true);
                }
            });
            rootRow.Add(rootToggle);
            _inspectorRoot.Add(rootRow);

            // Title field
            var titleField = new TextField("Title") { value = node.title };
            titleField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(_asset, "Edit BT Title");
                node.title = evt.newValue;
                EditorUtility.SetDirty(_asset);
                _graphView.PopulateFromAsset();
                UpdateStatus("Title updated.");
            });
            _inspectorRoot.Add(titleField);

            // Comment field
            var commentField = new TextField("Comment") { value = node.comment, multiline = true };
            commentField.style.minHeight = 60;
            commentField.RegisterValueChangedCallback(evt =>
            {
                Undo.RecordObject(_asset, "Edit BT Comment");
                node.comment = evt.newValue;
                EditorUtility.SetDirty(_asset);
                UpdateStatus("Comment updated.");
            });
            _inspectorRoot.Add(commentField);

            // Parameters (auto UI)
            _inspectorRoot.Add(new Label("Parameters") { style = { marginTop = 10, unityFontStyleAndWeight = FontStyle.Bold } });
            BtEditorParamUtility.EnsureParams(node, _asset);
            _inspectorRoot.Add(BtEditorParamUtility.CreateParamEditor(_asset, node, () =>
            {
                // 파라미터만 바뀐 경우 그래프 구조는 유지하므로 Populate는 필요 시에만
                UpdateStatus("Param updated.");
            }));

            // Children order (drag & drop)
            _inspectorRoot.Add(new Label("Children Order") { style = { marginTop = 10, unityFontStyleAndWeight = FontStyle.Bold } });
            _inspectorRoot.Add(BuildChildrenOrderUI(node));

            // Delete
            _inspectorRoot.Add(new VisualElement { style = { height = 8 } });
            var deleteBtn = new Button(() => DeleteNode(node.id)) { text = "Delete Node" };
            deleteBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            _inspectorRoot.Add(deleteBtn);

            // Debug
            _inspectorRoot.Add(new VisualElement { style = { height = 12 } });
            _inspectorRoot.Add(BuildDebugPanel(node.id));
        }

        private VisualElement BuildChildrenOrderUI(BtNodeRecord node)
        {
            var root = new VisualElement();

            if (node.kind == BtNodeKind.Action || node.kind == BtNodeKind.Condition)
            {
                root.Add(new Label("This node type cannot have children.") { style = { opacity = 0.75f } });
                return root;
            }

            node.children ??= new System.Collections.Generic.List<string>();

            // IMGUI ReorderableList (드래그&드롭 정렬) - UI Toolkit 안에 포함
            _childrenReorder = new ReorderableList(node.children, typeof(string), draggable: node.kind == BtNodeKind.Composite, displayHeader: false, displayAddButton: false, displayRemoveButton: false);

            _childrenReorder.elementHeight = 20;
            _childrenReorder.drawElementCallback = (rect, index, active, focused) =>
            {
                if (index < 0 || index >= node.children.Count) return;

                string childId = node.children[index];
                string title = childId;

                var child = _asset.FindNode(childId);
                if (child != null)
                    title = child.title;

                var labelRect = new Rect(rect.x, rect.y, rect.width - 70, rect.height);
                EditorGUI.LabelField(labelRect, title);

                var selectRect = new Rect(rect.x + rect.width - 68, rect.y, 32, rect.height);
                if (GUI.Button(selectRect, "Sel"))
                {
                    _selectedNodeId = childId;
                    RefreshInspector(childId);

                    // 그래프에서도 선택 처리
                    _graphView?.ClearSelection();
                }

                var removeRect = new Rect(rect.x + rect.width - 34, rect.y, 32, rect.height);
                if (GUI.Button(removeRect, "X"))
                {
                    Undo.RecordObject(_asset, "Remove BT Child");
                    node.children.RemoveAt(index);
                    EditorUtility.SetDirty(_asset);
                    _graphView.PopulateFromAsset();
                    _graphView.ApplyDebug(_runner);
                    UpdateStatus("Child removed.");
                }
            };

            _childrenReorder.onReorderCallback = _ =>
            {
                Undo.RecordObject(_asset, "Reorder BT Children");
                EditorUtility.SetDirty(_asset);
                _graphView.PopulateFromAsset();
                _graphView.ApplyDebug(_runner);
                UpdateStatus("Children reordered.");
            };

            // Decorator는 children 1개 제한 - 정렬 대신 현재 연결만 보여준다.
            if (node.kind == BtNodeKind.Decorator)
            {
                root.Add(new Label("Decorator: only one child is allowed.") { style = { opacity = 0.75f } });
            }

            _childrenReorderContainer = new IMGUIContainer(() =>
            {
                if (_childrenReorder == null) return;
                _childrenReorder.DoLayoutList();
            });

            root.Add(_childrenReorderContainer);

            if (node.typeId == BtTypeIds.Composite.RandomWeighted)
            {
                var syncBtn = new Button(() =>
                {
                    Undo.RecordObject(_asset, "Sync RandomWeighted Weights");
                    BtEditorParamUtility.EnsureParams(node, _asset);
                    EditorUtility.SetDirty(_asset);
                    UpdateStatus("Weights synced.");
                    RefreshInspector(node.id);
                }) { text = "Sync Weights (weight_0..n)" };
                root.Add(syncBtn);
            }

            return root;
        }

        private VisualElement BuildDebugPanel(string nodeId)
        {
            var fold = new Foldout { text = "Debug", value = true };

            if (_runner == null)
            {
                fold.Add(new Label("Attach Runner to see live data.") { style = { opacity = 0.75f } });
                return fold;
            }

            if (!EditorApplication.isPlaying)
            {
                fold.Add(new Label("Enter Play Mode to see runtime trace/metrics.") { style = { opacity = 0.75f } });
                return fold;
            }

            fold.Add(new Label($"Tick: {(_runner != null ? _runner.name : "-")} | Active: {_runner.DebugActiveNodeId ?? "-"}") { style = { opacity = 0.85f } });

            // Metrics
            var metrics = _runner.DebugLastMetrics;
            if (metrics == null || metrics.Count == 0)
            {
                fold.Add(new Label("No metrics captured. (enableDebugTrace=true?)") { style = { opacity = 0.75f } });
                return fold;
            }

            var listRoot = new VisualElement { style = { marginTop = 6 } };

            // Selected node filter
            var filtered = string.IsNullOrEmpty(nodeId)
                ? metrics
                : metrics.Where(m => m.NodeId == nodeId).ToList();

            if (filtered.Count == 0)
            {
                listRoot.Add(new Label("No metrics for selected node.") { style = { opacity = 0.75f } });
                fold.Add(listRoot);
                return fold;
            }

            foreach (var m in filtered)
            {
                string line = $"{m.Key}: {m.Value:0.###}";
                if (!string.IsNullOrEmpty(m.Text))
                    line += $" ({m.Text})";

                listRoot.Add(new Label(line) { style = { opacity = 0.9f } });
            }

            fold.Add(listRoot);
            return fold;
        }

        private void DeleteNode(string nodeId)
        {
            if (_asset == null) return;

            Undo.RecordObject(_asset, "Delete BT Node");

            _asset.nodes.RemoveAll(n => n != null && n.id == nodeId);

            foreach (var n in _asset.nodes)
                if (n != null) n.children.RemoveAll(c => c == nodeId);

            if (_asset.rootNodeId == nodeId)
                _asset.rootNodeId = _asset.nodes.Count > 0 ? _asset.nodes[0].id : string.Empty;

            EditorUtility.SetDirty(_asset);

            _selectedNodeId = null;
            _graphView.PopulateFromAsset();
            RefreshInspector(null);
            UpdateStatus("Node deleted.");
        }

        private void UpdateStatus(string message = null)
        {
            if (_statusLabel == null) return;
            if (_asset == null) { _statusLabel.text = "No asset selected."; return; }

            _statusLabel.text = string.IsNullOrEmpty(message)
                ? $"Asset: {_asset.name} | Nodes: {_asset.nodes.Count}"
                : $"Asset: {_asset.name} | {message}";
        }

        public void MarkDirty(string reason) => UpdateStatus(reason);
    }
}
#endif
