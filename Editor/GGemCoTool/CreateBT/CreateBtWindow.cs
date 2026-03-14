#if UNITY_EDITOR
using System;
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
    public sealed class CreateBtWindow : EditorWindow
    {
        private const string Title = "BT 생성/테스트 툴";
        private MonsterBehaviorTreeAsset _asset;

        private BtGraphView _graphView;
        private VisualElement _inspectorRoot;
        private Label _statusLabel;

        private string _selectedNodeId;

        // Debug attach
        private MonsterBtRunner _runner;

        private ToolbarButton _applyTreeToRunnerButton;

        // Children reorder
        private ReorderableList _childrenReorder;
        private IMGUIContainer _childrenReorderContainer;
        private bool _isUndoRedoSubscribed;

        [MenuItem(ConfigEditorAiBt.NameToolCreateBt, false, (int)ConfigEditorAiBt.ToolOrdering.CreateBt)]
        public static void OpenMenu() => Open(null);

        public static void Open(MonsterBehaviorTreeAsset asset)
        {
            var wnd = GetWindow<CreateBtWindow>();
            wnd.titleContent = new GUIContent(Title);
            wnd.minSize = new Vector2(1080, 640);
            wnd.SetAsset(asset);
            wnd.Show();
        }

        private void OnEnable()
        {
            RegisterUndoCallbacks();
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

            // Runner에 현재 디자이너의 Tree Asset을 적용(런타임/에디트 모드 모두 지원)
            _applyTreeToRunnerButton = new ToolbarButton(ApplyTreeAssetToRunner)
            {
                text = "Apply Tree To Runner"
            };
            toolbar.Add(_applyTreeToRunnerButton);

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

        private static bool TryInvokeRunnerSetTree(MonsterBtRunner runner, MonsterBehaviorTreeAsset asset)
        {
            if (runner == null) return false;

            var t = runner.GetType();

            // 1) public void SetTree(MonsterBehaviorTreeAsset asset)
            var m1 = t.GetMethod("SetTree", new[] { typeof(MonsterBehaviorTreeAsset) });
            if (m1 != null)
            {
                m1.Invoke(runner, new object[] { asset });
                return true;
            }

            // 2) public void SetTree(MonsterBehaviorTreeAsset asset, BtTreeSwitchMode mode)
            // 패키지 버전에 따라 switch enum이 중첩 타입일 수 있다.
            var enumType = t.GetNestedType("BtTreeSwitchMode");
            if (enumType != null && enumType.IsEnum)
            {
                var m2 = t.GetMethod("SetTree", new[] { typeof(MonsterBehaviorTreeAsset), enumType });
                if (m2 != null)
                {
                    object mode;
                    // 가능한 한 상태를 유지하는 모드를 우선 사용
                    if (Enum.GetNames(enumType).Contains("PreserveBlackboardValues"))
                        mode = Enum.Parse(enumType, "PreserveBlackboardValues");
                    else if (Enum.GetNames(enumType).Contains("PreserveBlackboardAndSkillUseCounts"))
                        mode = Enum.Parse(enumType, "PreserveBlackboardAndSkillUseCounts");
                    else
                        mode = Enum.GetValues(enumType).GetValue(0);

                    m2.Invoke(runner, new[] { asset, mode });
                    return true;
                }
            }

            return false;
        }

        private void ApplyTreeAssetToRunner()
        {
            if (_runner == null)
            {
                UpdateStatus("No runner attached.");
                return;
            }

            if (_asset == null)
            {
                UpdateStatus("No tree asset selected.");
                return;
            }

            // Play Mode: 안전한 런타임 교체 API를 우선 사용
            if (EditorApplication.isPlaying)
            {
                var type = typeof(MonsterBtRunner);

                // 최신 버전: SetTree(MonsterBehaviorTreeAsset, BtTreeSwitchMode)
                var m2 = type.GetMethods()
                    .FirstOrDefault(m =>
                        m.Name == "SetTree" &&
                        m.GetParameters().Length == 2 &&
                        m.GetParameters()[0].ParameterType == typeof(MonsterBehaviorTreeAsset));

                if (m2 != null)
                {
                    var modeType = m2.GetParameters()[1].ParameterType;
                    object modeValue = null;

                    // 기본값은 Blackboard 유지(가능하면)
                    var preserve = System.Enum.GetNames(modeType).FirstOrDefault(n => n.Contains("PreserveBlackboard"));
                    modeValue = preserve != null ? System.Enum.Parse(modeType, preserve) : System.Enum.GetValues(modeType).GetValue(0);

                    m2.Invoke(_runner, new[] { (object)_asset, modeValue });
                }
                else
                {
                    // 구버전: SetTree(MonsterBehaviorTreeAsset)
                    _runner.SetTree(_asset);
                }

                UpdateStatus("Applied tree to runner (Play Mode)." );
                _graphView?.ApplyDebug(_runner);
                return;
            }

            // Edit Mode: SerializedProperty로 treeAsset 교체
            var so = new SerializedObject(_runner);
            var prop = so.FindProperty("treeAsset");
            if (prop == null)
            {
                UpdateStatus("Runner does not expose serialized 'treeAsset'.");
                return;
            }

            Undo.RecordObject(_runner, "Apply BT Tree Asset");
            prop.objectReferenceValue = _asset;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(_runner);
            UpdateStatus("Applied tree to runner (Edit Mode)." );
        }

        private void OnDisable()
        {
            UnregisterUndoCallbacks();
            DetachRunnerEvents();
        }

        private void RegisterUndoCallbacks()
        {
            if (_isUndoRedoSubscribed)
                return;

            Undo.undoRedoPerformed += HandleUndoRedoPerformed;
            _isUndoRedoSubscribed = true;
        }

        private void UnregisterUndoCallbacks()
        {
            if (!_isUndoRedoSubscribed)
                return;

            Undo.undoRedoPerformed -= HandleUndoRedoPerformed;
            _isUndoRedoSubscribed = false;
        }

        private void HandleUndoRedoPerformed()
        {
            if (_asset == null)
            {
                RefreshInspector(null);
                UpdateStatus("Undo / Redo applied.");
                Repaint();
                return;
            }

            if (!string.IsNullOrEmpty(_selectedNodeId) && _asset.FindNode(_selectedNodeId) == null)
                _selectedNodeId = null;

            if (_graphView != null)
            {
                _graphView.SetAsset(_asset);
                _graphView.PopulateFromAsset();
                _graphView.ApplyDebug(_runner);

                if (!string.IsNullOrEmpty(_selectedNodeId))
                    _graphView.SelectNode(_selectedNodeId, frame: false);
            }

            RefreshInspector(_selectedNodeId);
            UpdateStatus("Undo / Redo applied.");
            Repaint();
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

        internal MonsterBehaviorTreeAsset Asset => _asset;

        internal void NotifyTreeChanged(string message, bool repopulateGraph = true, bool refreshInspector = true, string selectNodeId = null, bool applyDebug = true)
        {
            if (_asset == null)
                return;

            BtUndoUtility.SetDirty(_asset);

            if (_graphView != null && repopulateGraph)
            {
                _graphView.SetAsset(_asset);
                _graphView.PopulateFromAsset();
            }

            if (_graphView != null && applyDebug)
                _graphView.ApplyDebug(_runner);

            if (!string.IsNullOrEmpty(selectNodeId))
            {
                _selectedNodeId = selectNodeId;
                _graphView?.SelectNode(selectNodeId, frame: false);
            }
            else if (refreshInspector && !string.IsNullOrEmpty(_selectedNodeId) && _asset.FindNode(_selectedNodeId) == null)
            {
                _selectedNodeId = null;
            }

            if (refreshInspector)
                RefreshInspector(_selectedNodeId);

            UpdateStatus(message);
        }

        private void RefreshInspector(string selectedNodeId)
        {
            if (_inspectorRoot == null)
                return;

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
                    BtUndoUtility.RecordComplete(_asset, "Create BT Preset");
                    MonsterBtPresetBuilder.CreateMeleeBasicPreset(_asset);
                    NotifyTreeChanged("Preset created.", selectNodeId: _asset.rootNodeId);
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
                    BtUndoUtility.RecordComplete(_asset, "Set BT Root");
                    _asset.rootNodeId = node.id;
                    NotifyTreeChanged("Root updated.", selectNodeId: node.id);
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
            var titleField = new TextField("Title") { value = node.title, isDelayed = true };
            titleField.RegisterValueChangedCallback(evt =>
            {
                BtUndoUtility.RecordDelta(_asset, "Edit BT Title");
                node.title = evt.newValue;
                BtUndoUtility.SetDirty(_asset);
                // Title 변경은 그래프 구조 변경이 아니므로 전체 리빌드를 피한다.
                _graphView.RefreshNodeView(node.id);
                _graphView.SelectNode(node.id);
                RefreshInspector(node.id);
                UpdateStatus("Title updated.");
            });
            _inspectorRoot.Add(titleField);

            // Comment field
            var commentField = new TextField("Comment") { value = node.comment, multiline = true, isDelayed = true };
            commentField.style.minHeight = 60;
            commentField.RegisterValueChangedCallback(evt =>
            {
                BtUndoUtility.RecordDelta(_asset, "Edit BT Comment");
                node.comment = evt.newValue;
                BtUndoUtility.SetDirty(_asset);
                RefreshInspector(node.id);
                UpdateStatus("Comment updated.");
            });
            _inspectorRoot.Add(commentField);

            // Parameters (auto UI)
            _inspectorRoot.Add(new Label("Parameters") { style = { marginTop = 10, unityFontStyleAndWeight = FontStyle.Bold } });
            BtEditorParamUtility.EnsureParams(node, _asset);
            _inspectorRoot.Add(BtEditorParamUtility.CreateParamEditor(this, _asset, node, () =>
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
                    _graphView?.SelectNode(childId, frame: true);
                }

                var removeRect = new Rect(rect.x + rect.width - 34, rect.y, 32, rect.height);
                if (GUI.Button(removeRect, "X"))
                {
                    BtUndoUtility.RecordComplete(_asset, "Remove BT Child");
                    node.children.RemoveAt(index);

                    if (node.typeId == BtTypeIds.Composite.RandomWeighted)
                    {
                        BtEditorParamUtility.EnsureParams(node, _asset);
                    }

                    NotifyTreeChanged("Child removed.", selectNodeId: node.id);
                }
            };

            _childrenReorder.onReorderCallback = _ =>
            {
                BtUndoUtility.RecordComplete(_asset, "Reorder BT Children");
                NotifyTreeChanged("Children reordered.", selectNodeId: node.id);
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
                    BtUndoUtility.RecordComplete(_asset, "Sync RandomWeighted Weights");
                    BtEditorParamUtility.EnsureParams(node, _asset);
                    NotifyTreeChanged("Weights synced.", selectNodeId: node.id);
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

            var controls = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    marginTop = 4,
                    marginBottom = 6,
                }
            };

            var row1 = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            var freezeButton = new Button(() =>
            {
                _runner.SetDebugFreeze(!_runner.DebugFreeze);
                _graphView?.ApplyDebug(_runner);
                RefreshInspector(_selectedNodeId);
                Repaint();
            })
            {
                text = _runner.DebugFreeze ? "Resume" : "Freeze"
            };
            row1.Add(freezeButton);

            var step1Button = new Button(() =>
            {
                _runner.RequestDebugStep(1);
                _runner.SetDebugFreeze(true);
                UpdateStatus("Requested 1 debug step.");
                RefreshInspector(_selectedNodeId);
                Repaint();
            })
            {
                text = "Step 1"
            };
            row1.Add(step1Button);

            var step5Button = new Button(() =>
            {
                _runner.RequestDebugStep(5);
                _runner.SetDebugFreeze(true);
                UpdateStatus("Requested 5 debug steps.");
                RefreshInspector(_selectedNodeId);
                Repaint();
            })
            {
                text = "Step 5"
            };
            row1.Add(step5Button);

            var clearHistoryButton = new Button(() =>
            {
                _runner.ClearDebugHistory();
                _graphView?.ApplyDebug(_runner);
                RefreshInspector(_selectedNodeId);
                UpdateStatus("Debug history cleared.");
                Repaint();
            })
            {
                text = "Clear History"
            };
            row1.Add(clearHistoryButton);
            controls.Add(row1);

            var row2 = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 4 } };
            var breakpointsEnabled = new Toggle("Breakpoints") { value = _runner.DebugBreakpointsEnabled };
            breakpointsEnabled.RegisterValueChangedCallback(evt =>
            {
                _runner.DebugBreakpointsEnabled = evt.newValue;
                _graphView?.ApplyDebug(_runner);
                RefreshInspector(_selectedNodeId);
                Repaint();
            });
            row2.Add(breakpointsEnabled);
            controls.Add(row2);

            fold.Add(controls);

            if (!string.IsNullOrEmpty(nodeId))
            {
                fold.Add(BuildBreakpointEditor(nodeId));
            }

            if (!EditorApplication.isPlaying)
            {
                fold.Add(new Label("Enter Play Mode to see runtime trace/metrics.") { style = { opacity = 0.75f } });
                return fold;
            }

            var breakInfo = _runner.DebugLastBreakInfo;
            if (breakInfo.IsValid)
            {
                string breakText = $"Paused At: {breakInfo.NodeId} | Status: {breakInfo.Status} | Tick: {breakInfo.TickIndex}";
                if (breakInfo.Reason != BtDebugReason.None)
                    breakText += $" | Reason: {breakInfo.Reason}";
                fold.Add(new Label(breakText) { style = { unityFontStyleAndWeight = FontStyle.Bold, whiteSpace = WhiteSpace.Normal } });
                if (!string.IsNullOrEmpty(breakInfo.Summary))
                    fold.Add(new Label(breakInfo.Summary) { style = { opacity = 0.85f, whiteSpace = WhiteSpace.Normal } });
            }

            var frame = _runner.DebugLastFrame;
            if (frame == null)
            {
                fold.Add(new Label("No debug frame captured yet.") { style = { opacity = 0.75f } });
                return fold;
            }

            fold.Add(new Label($"TickIndex: {frame.TickIndex} | Root: {frame.RootNodeId} | RootStatus: {frame.RootStatus}") { style = { opacity = 0.9f } });
            fold.Add(new Label($"Active: {frame.ActiveNodeId ?? "-"} | ActiveKey: {frame.ActiveExecutionKey ?? "-"} | Time: {frame.Time:0.###} | Freeze: {_runner.DebugFreeze}") { style = { opacity = 0.85f, whiteSpace = WhiteSpace.Normal } });

            if (frame.ActivePath.Count > 0)
                fold.Add(new Label($"Active Path: {string.Join(" -> ", frame.ActivePath)}") { style = { opacity = 0.85f, whiteSpace = WhiteSpace.Normal } });
            if (frame.ActiveExecutionPath.Count > 0)
                fold.Add(new Label($"Active Execution Path: {string.Join(" -> ", frame.ActiveExecutionPath)}") { style = { opacity = 0.8f, whiteSpace = WhiteSpace.Normal } });

            var nodeEvents = string.IsNullOrEmpty(nodeId)
                ? frame.Events
                : frame.Events.Where(e => e.NodeId == nodeId).ToList();

            var nodeMetrics = string.IsNullOrEmpty(nodeId)
                ? frame.Metrics
                : frame.Metrics.Where(m => m.NodeId == nodeId).ToList();

            var body = new VisualElement { style = { marginTop = 6 } };

            if (!string.IsNullOrEmpty(nodeId))
            {
                body.Add(new Label("Execution Instances") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 4 } });

                var executionKeys = frame.Events
                    .Where(e => e.NodeId == nodeId && !string.IsNullOrEmpty(e.ExecutionKey))
                    .Select(e => e.ExecutionKey)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                if (executionKeys.Count == 0)
                {
                    body.Add(new Label("No execution instances for selected node.") { style = { opacity = 0.75f } });
                }
                else
                {
                    foreach (var executionKey in executionKeys)
                    {
                        var lastEvent = frame.Events.LastOrDefault(e => e.NodeId == nodeId && e.ExecutionKey == executionKey);
                        string line = $"{executionKey} => {lastEvent.Status}";
                        if (lastEvent.Reason != BtDebugReason.None)
                            line += $" ({lastEvent.Reason})";
                        if (!string.IsNullOrEmpty(lastEvent.Summary))
                            line += $" | {lastEvent.Summary}";
                        body.Add(new Label(line) { style = { opacity = 0.92f, whiteSpace = WhiteSpace.Normal } });

                        var executionMetrics = frame.Metrics.Where(m => m.NodeId == nodeId && m.ExecutionKey == executionKey).ToList();
                        if (executionMetrics.Count > 0)
                        {
                            for (int i = 0; i < executionMetrics.Count; i++)
                            {
                                var metric = executionMetrics[i];
                                string metricLine = $"    - {metric.Key}: {metric.Value:0.###}";
                                if (!string.IsNullOrEmpty(metric.Text))
                                    metricLine += $" ({metric.Text})";
                                body.Add(new Label(metricLine) { style = { opacity = 0.8f, whiteSpace = WhiteSpace.Normal } });
                            }
                        }
                    }
                }
            }

            body.Add(new Label("Events") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 4 } });
            if (nodeEvents == null || nodeEvents.Count == 0)
            {
                body.Add(new Label(string.IsNullOrEmpty(nodeId) ? "No events recorded." : "No events for selected node.") { style = { opacity = 0.75f } });
            }
            else
            {
                foreach (var ev in nodeEvents)
                {
                    string line = $"[{ev.Kind}] {ev.Title} => {ev.Status}";
                    if (!string.IsNullOrEmpty(ev.ExecutionKey))
                        line += $" | Key={ev.ExecutionKey}";
                    if (ev.Reason != BtDebugReason.None)
                        line += $" ({ev.Reason})";
                    if (!string.IsNullOrEmpty(ev.Summary))
                        line += $" | {ev.Summary}";
                    body.Add(new Label(line) { style = { opacity = 0.92f, whiteSpace = WhiteSpace.Normal } });
                }
            }

            body.Add(new Label("Metrics") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
            if (nodeMetrics == null || nodeMetrics.Count == 0)
            {
                body.Add(new Label(string.IsNullOrEmpty(nodeId) ? "No metrics recorded." : "No metrics for selected node.") { style = { opacity = 0.75f } });
            }
            else
            {
                foreach (var m in nodeMetrics)
                {
                    string line = $"{m.Key}: {m.Value:0.###}";
                    if (!string.IsNullOrEmpty(m.ExecutionKey))
                        line += $" | Key={m.ExecutionKey}";
                    if (!string.IsNullOrEmpty(m.Text))
                        line += $" ({m.Text})";
                    body.Add(new Label(line) { style = { opacity = 0.9f } });
                }
            }

            body.Add(new Label("Recent Frames") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8 } });
            var history = _runner.DebugHistory;
            if (history == null || history.Count == 0)
            {
                body.Add(new Label("No frame history.") { style = { opacity = 0.75f } });
            }
            else
            {
                int start = Mathf.Max(0, history.Count - 10);
                for (int i = history.Count - 1; i >= start; i--)
                {
                    var hist = history[i];
                    string line = $"#{hist.TickIndex} | Root={hist.RootStatus} | Active={hist.ActiveNodeId ?? "-"}";
                    if (!string.IsNullOrEmpty(nodeId))
                    {
                        var lastNodeEvent = hist.Events.LastOrDefault(e => e.NodeId == nodeId);
                        if (!string.IsNullOrEmpty(lastNodeEvent.NodeId))
                            line += $" | Node={lastNodeEvent.Status}";
                    }
                    body.Add(new Label(line) { style = { opacity = 0.85f } });
                }
            }

            fold.Add(body);
            return fold;
        }

        private VisualElement BuildBreakpointEditor(string nodeId)
        {
            var root = new VisualElement
            {
                style =
                {
                    marginTop = 6,
                    marginBottom = 6,
                    paddingTop = 4,
                    paddingBottom = 4,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderTopColor = new Color(0f, 0f, 0f, 0.2f),
                    borderBottomColor = new Color(0f, 0f, 0f, 0.2f)
                }
            };

            root.Add(new Label("Breakpoint") { style = { unityFontStyleAndWeight = FontStyle.Bold } });

            _runner.TryGetBreakpoint(nodeId, out var breakpoint);
            if (string.IsNullOrEmpty(breakpoint.NodeId))
                breakpoint = new BtDebugBreakpoint(nodeId, false, false, false, false);

            root.Add(CreateBreakpointToggle("Break On Visit", breakpoint.BreakOnVisit, value =>
            {
                breakpoint.BreakOnVisit = value;
                ApplyBreakpoint(nodeId, breakpoint);
            }));
            root.Add(CreateBreakpointToggle("Break On Success", breakpoint.BreakOnSuccess, value =>
            {
                breakpoint.BreakOnSuccess = value;
                ApplyBreakpoint(nodeId, breakpoint);
            }));
            root.Add(CreateBreakpointToggle("Break On Failure", breakpoint.BreakOnFailure, value =>
            {
                breakpoint.BreakOnFailure = value;
                ApplyBreakpoint(nodeId, breakpoint);
            }));
            root.Add(CreateBreakpointToggle("Break On Running", breakpoint.BreakOnRunning, value =>
            {
                breakpoint.BreakOnRunning = value;
                ApplyBreakpoint(nodeId, breakpoint);
            }));

            var clearButton = new Button(() =>
            {
                _runner.RemoveBreakpoint(nodeId);
                _graphView?.ApplyDebug(_runner);
                RefreshInspector(nodeId);
                Repaint();
            })
            {
                text = "Clear Breakpoint"
            };
            root.Add(clearButton);

            return root;
        }

        private VisualElement CreateBreakpointToggle(string label, bool initialValue, Action<bool> onChanged)
        {
            var toggle = new Toggle(label) { value = initialValue };
            toggle.RegisterValueChangedCallback(evt => onChanged?.Invoke(evt.newValue));
            return toggle;
        }

        private void ApplyBreakpoint(string nodeId, BtDebugBreakpoint breakpoint)
        {
            breakpoint.NodeId = nodeId;
            _runner.SetBreakpoint(breakpoint);
            _graphView?.ApplyDebug(_runner);
            RefreshInspector(nodeId);
            Repaint();
        }

        private void DeleteNode(string nodeId)
        {
            if (_asset == null) return;

            BtUndoUtility.RecordComplete(_asset, "Delete BT Node");

            _asset.nodes.RemoveAll(n => n != null && n.id == nodeId);

            foreach (var n in _asset.nodes)
                if (n != null) n.children.RemoveAll(c => c == nodeId);

            if (_asset.rootNodeId == nodeId)
                _asset.rootNodeId = _asset.nodes.Count > 0 ? _asset.nodes[0].id : string.Empty;

            _selectedNodeId = null;
            NotifyTreeChanged("Node deleted.", selectNodeId: _asset.rootNodeId);
        }

        private void UpdateStatus(string message = null)
        {
            _applyTreeToRunnerButton?.SetEnabled(_runner != null && _asset != null);

            if (_statusLabel == null) return;
            if (_asset == null) { _statusLabel.text = "No asset selected."; return; }

            _applyTreeToRunnerButton?.SetEnabled(_runner != null && _asset != null);

            _applyTreeToRunnerButton?.SetEnabled(_runner != null && _asset != null);

            _statusLabel.text = string.IsNullOrEmpty(message)
                ? $"Asset: {_asset.name} | Nodes: {_asset.nodes.Count}"
                : $"Asset: {_asset.name} | {message}";
        }

        public void MarkDirty(string reason) => UpdateStatus(reason);
    }
}
#endif
