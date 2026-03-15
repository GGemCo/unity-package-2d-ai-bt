#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
    /// GraphView + UI Toolkit 기반 BT 디자이너.
    /// 편집용 Inspector와 런타임 Debug 패널을 분리해 가독성과 유지보수성을 높인다.
    /// </summary>
    public sealed class CreateBtWindow : EditorWindow
    {
        private const string Title = "BT 생성/테스트 툴";
        private const float InspectorWidth = 360f;
        private const float BottomDebugHeight = 300f;

        private MonsterBehaviorTreeAsset _asset;

        private BtGraphView _graphView;
        private VisualElement _inspectorRoot;
        private VisualElement _debugRoot;
        private VisualElement _debugTabContent;
        private Label _statusLabel;

        private string _selectedNodeId;

        // Debug attach
        private MonsterBtRunner _runner;

        private ToolbarButton _applyTreeToRunnerButton;
        private ToolbarButton _freezeButton;
        private ToolbarButton _step1Button;
        private ToolbarButton _step5Button;
        private ToolbarButton _clearHistoryButton;
        private ToolbarButton _saveDebugButton;
        private ToolbarToggle _breakpointsEnabledToggle;
        private ToolbarToggle _selectedNodeOnlyToggle;
        private Label _debugRuntimeStateLabel;

        private ListView _eventsListView;
        private ListView _metricsListView;
        private ListView _historyListView;
        private ListView _breakpointsListView;

        private string _debugTab = "Overview";

        // Inspector runtime summary
        private Label _inspectorSummaryLabel;
        private Label _inspectorExecutionLabel;
        private Label _inspectorMetricsLabel;

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
            UpdateDebugView();
            UpdateStatus();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.flexDirection = FlexDirection.Column;

            root.Add(BuildTopToolbar());

            var content = new VisualElement
            {
                style =
                {
                    flexGrow = 1,
                    flexDirection = FlexDirection.Column,
                }
            };
            root.Add(content);

            var topArea = new VisualElement
            {
                style =
                {
                    flexGrow = 1,
                    flexDirection = FlexDirection.Row,
                }
            };
            content.Add(topArea);

            _graphView = new BtGraphView(this);
            _graphView.style.flexGrow = 1;
            _graphView.OnSelectionChanged += id =>
            {
                _selectedNodeId = id;
                RefreshInspector(id);
            };
            topArea.Add(_graphView);

            root.schedule.Execute(() =>
            {
                _graphView?.PollSelectionChange();
            }).Every(50);

            var inspectorScroll = new ScrollView(ScrollViewMode.Vertical)
            {
                style =
                {
                    width = InspectorWidth,
                    flexShrink = 0,
                    flexGrow = 0,
                    borderLeftWidth = 1,
                    borderLeftColor = new Color(0, 0, 0, 0.25f)
                }
            };
            inspectorScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            inspectorScroll.verticalScrollerVisibility = ScrollerVisibility.Auto;

            _inspectorRoot = new VisualElement
            {
                style =
                {
                    flexGrow = 1,
                    paddingLeft = 10,
                    paddingRight = 10,
                    paddingTop = 8,
                    paddingBottom = 8
                }
            };
            inspectorScroll.Add(_inspectorRoot);
            topArea.Add(inspectorScroll);

            _debugRoot = new VisualElement
            {
                style =
                {
                    height = BottomDebugHeight,
                    flexShrink = 0,
                    borderTopWidth = 1,
                    borderTopColor = new Color(0, 0, 0, 0.25f),
                    paddingLeft = 8,
                    paddingRight = 8,
                    paddingTop = 6,
                    paddingBottom = 6,
                    flexDirection = FlexDirection.Column,
                }
            };
            content.Add(_debugRoot);

            BuildBottomDebugPanel();

            _statusLabel = new Label { style = { paddingLeft = 6, paddingTop = 3, paddingBottom = 3 } };
            root.Add(_statusLabel);

            _graphView.SetAsset(_asset);
            _graphView.PopulateFromAsset();
            _graphView.ApplyDebug(_runner);

            AttachRunnerEvents();
            RefreshInspector(_selectedNodeId);
            UpdateDebugView();
            UpdateStatus();
        }

        private Toolbar BuildTopToolbar()
        {
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
                UpdateDebugView();
            });
            toolbar.Add(new ToolbarSpacer());
            toolbar.Add(runnerField);

            _applyTreeToRunnerButton = new ToolbarButton(ApplyTreeAssetToRunner)
            {
                text = "Apply Tree To Runner"
            };
            toolbar.Add(_applyTreeToRunnerButton);

            return toolbar;
        }

        private void BuildBottomDebugPanel()
        {
            _debugRoot.Clear();
            _debugRoot.Add(BuildGlobalDebugToolbar());
            _debugRoot.Add(BuildDebugTabBar());

            _debugTabContent = new VisualElement
            {
                style =
                {
                    flexGrow = 1,
                    marginTop = 6,
                }
            };
            _debugRoot.Add(_debugTabContent);
        }

        private VisualElement BuildGlobalDebugToolbar()
        {
            var root = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexShrink = 0,
                }
            };

            var controls = new Toolbar();

            _freezeButton = new ToolbarButton(() =>
            {
                if (_runner == null)
                    return;

                _runner.SetDebugFreeze(!_runner.DebugFreeze);
                _graphView?.ApplyDebug(_runner);
                UpdateDebugView();
            });
            controls.Add(_freezeButton);

            _step1Button = new ToolbarButton(() =>
            {
                if (_runner == null)
                    return;

                _runner.RequestDebugStep(1);
                _runner.SetDebugFreeze(true);
                UpdateStatus("Requested 1 debug step.");
                UpdateDebugView();
            }) { text = "Step 1" };
            controls.Add(_step1Button);

            _step5Button = new ToolbarButton(() =>
            {
                if (_runner == null)
                    return;

                _runner.RequestDebugStep(5);
                _runner.SetDebugFreeze(true);
                UpdateStatus("Requested 5 debug steps.");
                UpdateDebugView();
            }) { text = "Step 5" };
            controls.Add(_step5Button);

            _clearHistoryButton = new ToolbarButton(() =>
            {
                if (_runner == null)
                    return;

                _runner.ClearDebugHistory();
                _graphView?.ApplyDebug(_runner);
                UpdateStatus("Debug history cleared.");
                UpdateDebugView();
            }) { text = "Clear History" };
            controls.Add(_clearHistoryButton);

            _saveDebugButton = new ToolbarButton(SaveDebugSnapshot)
            {
                text = "Save Debug"
            };
            controls.Add(_saveDebugButton);

            controls.Add(new ToolbarSpacer());

            _breakpointsEnabledToggle = new ToolbarToggle { text = "Breakpoints" };
            _breakpointsEnabledToggle.RegisterValueChangedCallback(evt =>
            {
                if (_runner == null)
                    return;

                _runner.DebugBreakpointsEnabled = evt.newValue;
                _graphView?.ApplyDebug(_runner);
                UpdateDebugView();
            });
            controls.Add(_breakpointsEnabledToggle);

            _selectedNodeOnlyToggle = new ToolbarToggle { text = "Selected Node Only" };
            _selectedNodeOnlyToggle.RegisterValueChangedCallback(_ => UpdateDebugView());
            controls.Add(_selectedNodeOnlyToggle);

            root.Add(controls);

            _debugRuntimeStateLabel = new Label
            {
                style =
                {
                    marginTop = 4,
                    opacity = 0.85f,
                    whiteSpace = WhiteSpace.Normal,
                }
            };
            root.Add(_debugRuntimeStateLabel);

            return root;
        }

        private VisualElement BuildDebugTabBar()
        {
            var tabs = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexShrink = 0,
                    marginTop = 6,
                }
            };

            tabs.Add(CreateDebugTabButton("Overview"));
            tabs.Add(CreateDebugTabButton("Events"));
            tabs.Add(CreateDebugTabButton("Metrics"));
            tabs.Add(CreateDebugTabButton("History"));
            tabs.Add(CreateDebugTabButton("Breakpoints"));

            return tabs;
        }

        private Button CreateDebugTabButton(string tabName)
        {
            var button = new Button(() =>
            {
                _debugTab = tabName;
                UpdateDebugView();
            })
            {
                text = tabName
            };
            button.style.marginRight = 4;
            return button;
        }

        private static bool TryInvokeRunnerSetTree(MonsterBtRunner runner, MonsterBehaviorTreeAsset asset)
        {
            if (runner == null) return false;

            var t = runner.GetType();
            var m1 = t.GetMethod("SetTree", new[] { typeof(MonsterBehaviorTreeAsset) });
            if (m1 != null)
            {
                m1.Invoke(runner, new object[] { asset });
                return true;
            }

            var enumType = t.GetNestedType("BtTreeSwitchMode");
            if (enumType != null && enumType.IsEnum)
            {
                var m2 = t.GetMethod("SetTree", new[] { typeof(MonsterBehaviorTreeAsset), enumType });
                if (m2 != null)
                {
                    object mode;
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

            if (EditorApplication.isPlaying)
            {
                var type = typeof(MonsterBtRunner);
                var m2 = type.GetMethods()
                    .FirstOrDefault(m =>
                        m.Name == "SetTree" &&
                        m.GetParameters().Length == 2 &&
                        m.GetParameters()[0].ParameterType == typeof(MonsterBehaviorTreeAsset));

                if (m2 != null)
                {
                    var modeType = m2.GetParameters()[1].ParameterType;
                    object modeValue = null;
                    var preserve = Enum.GetNames(modeType).FirstOrDefault(n => n.Contains("PreserveBlackboard"));
                    modeValue = preserve != null ? Enum.Parse(modeType, preserve) : Enum.GetValues(modeType).GetValue(0);
                    m2.Invoke(_runner, new[] { (object)_asset, modeValue });
                }
                else
                {
                    _runner.SetTree(_asset);
                }

                UpdateStatus("Applied tree to runner (Play Mode).");
                _graphView?.ApplyDebug(_runner);
                UpdateDebugView();
                return;
            }

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
            UpdateStatus("Applied tree to runner (Edit Mode).");
            UpdateDebugView();
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
                UpdateDebugView();
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
            UpdateDebugView();
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
            _graphView?.ApplyDebug(runner);
            UpdateInspectorDebugSummary();
            UpdateDebugView();
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

            UpdateDebugView();
            UpdateStatus(message);
        }

        private void RefreshInspector(string selectedNodeId)
        {
            if (_inspectorRoot == null)
                return;

            _inspectorRoot.Clear();
            ResetInspectorDebugSummaryReferences();

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
                _inspectorRoot.Add(BuildNodeInspectorDebugSummary(null));
                UpdateInspectorDebugSummary();
                return;
            }

            var node = _asset.FindNode(selectedNodeId);
            if (node == null)
            {
                _inspectorRoot.Add(new Label($"Node not found: {selectedNodeId}"));
                return;
            }

            var header = new VisualElement { style = { flexDirection = FlexDirection.Column, marginBottom = 8 } };
            header.Add(new Label(node.title) { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 13 } });
            header.Add(new Label(node.typeId) { style = { opacity = 0.75f } });
            _inspectorRoot.Add(header);

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
                    rootToggle.SetValueWithoutNotify(true);
                }
            });
            rootRow.Add(rootToggle);
            _inspectorRoot.Add(rootRow);

            var titleField = new TextField("Title") { value = node.title, isDelayed = true };
            titleField.RegisterValueChangedCallback(evt =>
            {
                BtUndoUtility.RecordDelta(_asset, "Edit BT Title");
                node.title = evt.newValue;
                BtUndoUtility.SetDirty(_asset);
                _graphView.RefreshNodeView(node.id);
                _graphView.SelectNode(node.id);
                RefreshInspector(node.id);
                UpdateStatus("Title updated.");
            });
            _inspectorRoot.Add(titleField);

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

            _inspectorRoot.Add(new Label("Parameters") { style = { marginTop = 10, unityFontStyleAndWeight = FontStyle.Bold } });
            BtEditorParamUtility.EnsureParams(node, _asset);
            _inspectorRoot.Add(BtEditorParamUtility.CreateParamEditor(this, _asset, node, () =>
            {
                UpdateStatus("Param updated.");
            }));

            _inspectorRoot.Add(new Label("Children Order") { style = { marginTop = 10, unityFontStyleAndWeight = FontStyle.Bold } });
            _inspectorRoot.Add(BuildChildrenOrderUI(node));

            _inspectorRoot.Add(new VisualElement { style = { height = 8 } });
            var deleteBtn = new Button(() => DeleteNode(node.id)) { text = "Delete Node" };
            deleteBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            _inspectorRoot.Add(deleteBtn);

            _inspectorRoot.Add(new VisualElement { style = { height = 12 } });
            _inspectorRoot.Add(BuildNodeInspectorDebugSummary(node.id));

            if (_runner != null)
            {
                _inspectorRoot.Add(new VisualElement { style = { height = 8 } });
                _inspectorRoot.Add(BuildBreakpointEditor(node.id));
            }

            UpdateInspectorDebugSummary();
        }

        private VisualElement BuildChildrenOrderUI(BtNodeRecord node)
        {
            var root = new VisualElement();

            if (node.kind == BtNodeKind.Action || node.kind == BtNodeKind.Condition)
            {
                root.Add(new Label("This node type cannot have children.") { style = { opacity = 0.75f } });
                return root;
            }

            node.children ??= new List<string>();

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

        private VisualElement BuildNodeInspectorDebugSummary(string nodeId)
        {
            var root = new VisualElement
            {
                style =
                {
                    paddingTop = 6,
                    paddingBottom = 6,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderTopColor = new Color(0f, 0f, 0f, 0.2f),
                    borderBottomColor = new Color(0f, 0f, 0f, 0.2f)
                }
            };

            root.Add(new Label("Runtime Summary") { style = { unityFontStyleAndWeight = FontStyle.Bold } });

            _inspectorSummaryLabel = new Label { style = { whiteSpace = WhiteSpace.Normal, opacity = 0.92f } };
            _inspectorExecutionLabel = new Label { style = { whiteSpace = WhiteSpace.Normal, opacity = 0.85f, marginTop = 2 } };
            _inspectorMetricsLabel = new Label { style = { whiteSpace = WhiteSpace.Normal, opacity = 0.8f, marginTop = 2 } };

            root.Add(_inspectorSummaryLabel);
            root.Add(_inspectorExecutionLabel);
            root.Add(_inspectorMetricsLabel);

            return root;
        }

        private void ResetInspectorDebugSummaryReferences()
        {
            _inspectorSummaryLabel = null;
            _inspectorExecutionLabel = null;
            _inspectorMetricsLabel = null;
        }

        private void UpdateInspectorDebugSummary()
        {
            if (_inspectorSummaryLabel == null || _inspectorExecutionLabel == null || _inspectorMetricsLabel == null)
                return;

            if (_runner == null)
            {
                _inspectorSummaryLabel.text = "Attach Runner to inspect live runtime state.";
                _inspectorExecutionLabel.text = string.Empty;
                _inspectorMetricsLabel.text = string.Empty;
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                _inspectorSummaryLabel.text = "Enter Play Mode to inspect runtime debug data.";
                _inspectorExecutionLabel.text = string.Empty;
                _inspectorMetricsLabel.text = string.Empty;
                return;
            }

            var frame = _runner.DebugLastFrame;
            if (frame == null)
            {
                _inspectorSummaryLabel.text = "No debug frame captured yet.";
                _inspectorExecutionLabel.text = string.Empty;
                _inspectorMetricsLabel.text = string.Empty;
                return;
            }

            if (string.IsNullOrEmpty(_selectedNodeId))
            {
                _inspectorSummaryLabel.text = $"Tick #{frame.TickIndex} | Root={frame.RootStatus} | Active={GetNodeDisplayTitleWithFallback(frame.ActiveNodeId)}";
                _inspectorExecutionLabel.text = $"Active Key={frame.ActiveExecutionKey ?? "-"} | Freeze={_runner.DebugFreeze}";
                _inspectorMetricsLabel.text = frame.ActivePath.Count > 0
                    ? $"Active Path: {string.Join(" -> ", frame.ActivePath.Select(GetNodeDisplayTitleWithFallback))}"
                    : "Active Path: -";
                return;
            }

            var nodeEvents = frame.Events.Where(e => e.NodeId == _selectedNodeId).ToList();
            var nodeMetrics = frame.Metrics.Where(m => m.NodeId == _selectedNodeId).ToList();
            var lastEvent = nodeEvents.LastOrDefault();

            if (string.IsNullOrEmpty(lastEvent.NodeId))
            {
                _inspectorSummaryLabel.text = $"Tick #{frame.TickIndex} | Selected node has no runtime event yet.";
                _inspectorExecutionLabel.text = string.Empty;
                _inspectorMetricsLabel.text = string.Empty;
                return;
            }

            _inspectorSummaryLabel.text = $"Tick #{frame.TickIndex} | Last Status={lastEvent.Status} | Kind={lastEvent.Kind}";
            _inspectorExecutionLabel.text = $"Execution Key={lastEvent.ExecutionKey ?? "-"} | Reason={lastEvent.Reason}";
            _inspectorMetricsLabel.text = nodeMetrics.Count > 0
                ? $"Metrics: {string.Join(" | ", nodeMetrics.Take(3).Select(m => $"{m.Key}={m.Value:0.###}"))}"
                : "Metrics: -";
        }

        private void UpdateDebugView()
        {
            UpdateDebugToolbarState();
            UpdateInspectorDebugSummary();
            RebuildDebugTabContent();
        }

        private void UpdateDebugToolbarState()
        {
            bool hasRunner = _runner != null;
            bool isPlaying = EditorApplication.isPlaying;
            bool enabled = hasRunner && isPlaying;

            _freezeButton?.SetEnabled(enabled);
            _step1Button?.SetEnabled(enabled);
            _step5Button?.SetEnabled(enabled);
            _clearHistoryButton?.SetEnabled(hasRunner);
            _saveDebugButton?.SetEnabled(hasRunner && _asset != null);
            _breakpointsEnabledToggle?.SetEnabled(hasRunner);
            _selectedNodeOnlyToggle?.SetEnabled(enabled);

            if (_freezeButton != null)
                _freezeButton.text = hasRunner && _runner.DebugFreeze ? "Resume" : "Freeze";

            if (_breakpointsEnabledToggle != null && hasRunner)
                _breakpointsEnabledToggle.SetValueWithoutNotify(_runner.DebugBreakpointsEnabled);

            if (_debugRuntimeStateLabel == null)
                return;

            if (!hasRunner)
            {
                _debugRuntimeStateLabel.text = "Runner를 Attach 하면 하단 Debug 패널에서 전체 런타임 흐름을 확인할 수 있습니다.";
                return;
            }

            if (!isPlaying)
            {
                _debugRuntimeStateLabel.text = "Play Mode에서 Events / Metrics / History / Breakpoints 탭이 활성화됩니다.";
                return;
            }

            var frame = _runner.DebugLastFrame;
            if (frame == null)
            {
                _debugRuntimeStateLabel.text = $"Freeze={_runner.DebugFreeze} | Breakpoints={_runner.DebugBreakpointsEnabled} | Waiting for first debug frame...";
                return;
            }

            _debugRuntimeStateLabel.text =
                $"Tick #{frame.TickIndex} | Root={frame.RootStatus} | Active={GetNodeDisplayTitleWithFallback(frame.ActiveNodeId)} | ActiveKey={frame.ActiveExecutionKey ?? "-"} | Freeze={_runner.DebugFreeze}";
        }

        private void RebuildDebugTabContent()
        {
            if (_debugTabContent == null)
                return;

            _debugTabContent.Clear();

            if (_runner == null)
            {
                _debugTabContent.Add(new Label("Attach Runner to see debug data.") { style = { opacity = 0.75f } });
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                _debugTabContent.Add(new Label("Enter Play Mode to populate runtime debug data.") { style = { opacity = 0.75f } });
                _debugTabContent.Add(BuildBreakpointsOverview());
                return;
            }

            switch (_debugTab)
            {
                case "Events":
                    _debugTabContent.Add(BuildDebugEventsList());
                    break;
                case "Metrics":
                    _debugTabContent.Add(BuildDebugMetricsList());
                    break;
                case "History":
                    _debugTabContent.Add(BuildDebugHistoryList());
                    break;
                case "Breakpoints":
                    _debugTabContent.Add(BuildBreakpointsOverview());
                    break;
                default:
                    _debugTabContent.Add(BuildOverviewPanel());
                    break;
            }
        }
        private VisualElement BuildOverviewPanel()
        {
            var root = new ScrollView(ScrollViewMode.Vertical);
            var frame = _runner.DebugLastFrame;
            if (frame == null)
            {
                root.Add(new Label("No debug frame captured yet.") { style = { opacity = 0.75f } });
                return root;
            }

            root.Add(CreateInfoBox("Frame", $"TickIndex: {frame.TickIndex}\nRoot: {GetNodeDisplayTitleWithFallback(frame.RootNodeId)}\nRootStatus: {frame.RootStatus}\nTime: {frame.Time:0.###}"));
            root.Add(CreateInfoBox("Active", $"Node: {GetNodeDisplayTitleWithFallback(frame.ActiveNodeId)}\nExecution Key: {frame.ActiveExecutionKey ?? "-"}\nFreeze: {_runner.DebugFreeze}"));

            string activePath = frame.ActivePath.Count > 0 ? string.Join(" -> ", frame.ActivePath.Select(GetNodeDisplayTitleWithFallback)) : "-";
            string executionPath = frame.ActiveExecutionPath.Count > 0 ? string.Join(" -> ", frame.ActiveExecutionPath) : "-";
            root.Add(CreateInfoBox("Paths", $"Active Path: {activePath}\nExecution Path: {executionPath}"));

            var breakInfo = _runner.DebugLastBreakInfo;
            if (breakInfo.IsValid)
            {
                root.Add(CreateInfoBox("Last Break", $"Tick: {breakInfo.TickIndex}\nNode: {GetNodeDisplayTitleWithFallback(breakInfo.NodeId)}\nStatus: {breakInfo.Status}\nReason: {breakInfo.Reason}\nSummary: {breakInfo.Summary}"));
            }
            else
            {
                root.Add(CreateInfoBox("Last Break", "No breakpoint matched yet."));
            }

            return root;
        }

        private VisualElement BuildDebugEventsList()
        {
            var rows = GetFilteredEvents()
                .Select(ev => new DebugRowData
                {
                    Col1 = ev.Kind.ToString(),
                    Col2 = string.IsNullOrEmpty(ev.Title) ? GetNodeDisplayTitleWithFallback(ev.NodeId) : ev.Title,
                    Col3 = ev.Status.ToString(),
                    Col4 = string.IsNullOrEmpty(ev.ExecutionKey) ? "-" : ev.ExecutionKey,
                    Col5 = BuildEventSummary(ev),
                })
                .ToList();

            var view = CreateDebugListView(
                rows,
                "Kind", "Title", "Status", "ExecutionKey", "Summary",
                out _eventsListView);

            return WrapListView(view, rows.Count == 0 ? "No events recorded for current filter." : null);
        }

        private VisualElement BuildDebugMetricsList()
        {
            var rows = GetFilteredMetrics()
                .Select(metric => new DebugRowData
                {
                    Col1 = string.IsNullOrEmpty(metric.NodeId) ? "-" : metric.NodeId,
                    Col2 = metric.Key,
                    Col3 = metric.Value.ToString("0.###"),
                    Col4 = string.IsNullOrEmpty(metric.ExecutionKey) ? "-" : metric.ExecutionKey,
                    Col5 = string.IsNullOrEmpty(metric.Text) ? "-" : metric.Text,
                })
                .ToList();

            var view = CreateDebugListView(
                rows,
                "Node", "Key", "Value", "ExecutionKey", "Text",
                out _metricsListView);

            return WrapListView(view, rows.Count == 0 ? "No metrics recorded for current filter." : null);
        }

        private VisualElement BuildDebugHistoryList()
        {
            var history = _runner.DebugHistory;
            var rows = new List<DebugRowData>();
            if (history != null)
            {
                for (int i = history.Count - 1; i >= 0; i--)
                {
                    var frame = history[i];
                    BtDebugEvent nodeEvent = default;
                    if (!string.IsNullOrEmpty(_selectedNodeId))
                        nodeEvent = frame.Events.LastOrDefault(e => e.NodeId == _selectedNodeId);

                    rows.Add(new DebugRowData
                    {
                        Col1 = frame.TickIndex.ToString(),
                        Col2 = frame.RootStatus.ToString(),
                        Col3 = GetNodeDisplayTitleWithFallback(frame.ActiveNodeId),
                        Col4 = frame.ActiveExecutionKey ?? "-",
                        Col5 = !string.IsNullOrEmpty(nodeEvent.NodeId)
                            ? $"Selected={nodeEvent.Status} | {nodeEvent.Summary}"
                            : (frame.ActivePath.Count > 0 ? string.Join(" -> ", frame.ActivePath.Select(GetNodeDisplayTitleWithFallback)) : "-"),
                    });
                }
            }

            var view = CreateDebugListView(
                rows,
                "Tick", "Root", "Active Node", "ExecutionKey", "Summary",
                out _historyListView);

            return WrapListView(view, rows.Count == 0 ? "No frame history." : null);
        }

        private VisualElement BuildBreakpointsOverview()
        {
            var root = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Column } };

            if (_asset == null)
            {
                root.Add(new Label("No asset selected."));
                return root;
            }

            var rows = new List<DebugRowData>();
            foreach (var node in _asset.nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.id))
                    continue;

                var breakpoint = default(BtDebugBreakpoint);
                bool hasBreakpoint = _runner != null && _runner.TryGetBreakpoint(node.id, out breakpoint) && !breakpoint.IsEmpty;
                rows.Add(new DebugRowData
                {
                    Col1 = node.title,
                    Col2 = node.id,
                    Col3 = hasBreakpoint ? "Configured" : "-",
                    Col4 = hasBreakpoint ? BuildBreakpointFlags(breakpoint) : "-",
                    Col5 = node.typeId,
                });
            }

            var view = CreateDebugListView(rows, "Title", "NodeId", "State", "Flags", "Type", out _breakpointsListView);
            root.Add(WrapListView(view, rows.Count == 0 ? "No nodes available." : null));
            return root;
        }

        private VisualElement CreateDebugListView(IList<DebugRowData> rows, string h1, string h2, string h3, string h4, string h5, out ListView listView)
        {
            var container = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Column } };
            container.Add(BuildListHeader(h1, h2, h3, h4, h5));

            listView = new ListView
            {
                itemsSource = (System.Collections.IList)rows,
                makeItem = MakeDebugRow,
                bindItem = (element, index) => BindDebugRow(element, index, rows),
                fixedItemHeight = 22,
                selectionType = SelectionType.None,
                style = { flexGrow = 1 }
            };
            container.Add(listView);
            return container;
        }

        private VisualElement BuildListHeader(string h1, string h2, string h3, string h4, string h5)
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    height = 22,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    borderBottomWidth = 1,
                    borderBottomColor = new Color(0f, 0f, 0f, 0.2f),
                    marginBottom = 2,
                }
            };
            row.Add(CreateCell(h1, 90, true));
            row.Add(CreateCell(h2, 150, true));
            row.Add(CreateCell(h3, 80, true));
            row.Add(CreateCell(h4, 180, true));
            row.Add(CreateCell(h5, 0, true, true));
            return row;
        }

        private VisualElement MakeDebugRow()
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    height = 22,
                }
            };
            row.Add(CreateCell(string.Empty, 90));
            row.Add(CreateCell(string.Empty, 150));
            row.Add(CreateCell(string.Empty, 80));
            row.Add(CreateCell(string.Empty, 180));
            row.Add(CreateCell(string.Empty, 0, false, true));
            return row;
        }

        private void BindDebugRow(VisualElement element, int index, IList<DebugRowData> rows)
        {
            if (rows == null || index < 0 || index >= rows.Count)
                return;

            var row = rows[index];
            SetCellText(element, 0, row.Col1);
            SetCellText(element, 1, row.Col2);
            SetCellText(element, 2, row.Col3);
            SetCellText(element, 3, row.Col4);
            SetCellText(element, 4, row.Col5);
        }

        private static Label CreateCell(string text, float width, bool bold = false, bool grow = false)
        {
            var label = new Label(text)
            {
                style =
                {
                    unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal,
                    whiteSpace = WhiteSpace.NoWrap,
                    overflow = Overflow.Hidden,
                    textOverflow = TextOverflow.Ellipsis,
                    paddingLeft = 4,
                    paddingRight = 4,
                }
            };

            if (grow)
            {
                label.style.flexGrow = 1;
            }
            else
            {
                label.style.width = width;
                label.style.flexShrink = 0;
            }

            return label;
        }

        private static void SetCellText(VisualElement row, int index, string text)
        {
            if (index < 0 || index >= row.childCount)
                return;

            if (row[index] is Label label)
                label.text = text ?? string.Empty;
        }

        private VisualElement WrapListView(VisualElement view, string emptyMessage)
        {
            var root = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Column } };
            root.Add(view);

            if (!string.IsNullOrEmpty(emptyMessage))
            {
                root.Add(new Label(emptyMessage)
                {
                    style =
                    {
                        opacity = 0.75f,
                        marginTop = 4,
                    }
                });
            }

            return root;
        }

        private VisualElement CreateInfoBox(string title, string content)
        {
            var box = new VisualElement
            {
                style =
                {
                    marginBottom = 6,
                    paddingLeft = 8,
                    paddingRight = 8,
                    paddingTop = 6,
                    paddingBottom = 6,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopColor = new Color(0f, 0f, 0f, 0.15f),
                    borderBottomColor = new Color(0f, 0f, 0f, 0.15f),
                    borderLeftColor = new Color(0f, 0f, 0f, 0.15f),
                    borderRightColor = new Color(0f, 0f, 0f, 0.15f),
                }
            };
            box.Add(new Label(title) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            box.Add(new Label(content) { style = { whiteSpace = WhiteSpace.Normal, marginTop = 2 } });
            return box;
        }

        private string BuildBreakpointFlags(BtDebugBreakpoint breakpoint)
        {
            var flags = new List<string>(4);
            if (breakpoint.BreakOnVisit) flags.Add("Visit");
            if (breakpoint.BreakOnSuccess) flags.Add("Success");
            if (breakpoint.BreakOnFailure) flags.Add("Failure");
            if (breakpoint.BreakOnRunning) flags.Add("Running");
            return flags.Count > 0 ? string.Join(", ", flags) : "-";
        }

        private string BuildEventSummary(BtDebugEvent ev)
        {
            string summary = string.IsNullOrEmpty(ev.Summary) ? string.Empty : ev.Summary;
            if (ev.Reason != BtDebugReason.None)
            {
                if (!string.IsNullOrEmpty(summary))
                    summary += " | ";
                summary += ev.Reason;
            }

            return string.IsNullOrEmpty(summary) ? (ev.Title ?? "-") : summary;
        }

        private List<BtDebugEvent> GetFilteredEvents()
        {
            var frame = _runner.DebugLastFrame;
            if (frame == null)
                return new List<BtDebugEvent>();

            IEnumerable<BtDebugEvent> query = frame.Events;
            if (_selectedNodeOnlyToggle != null && _selectedNodeOnlyToggle.value && !string.IsNullOrEmpty(_selectedNodeId))
                query = query.Where(e => e.NodeId == _selectedNodeId);

            return query.ToList();
        }

        private List<BtDebugMetric> GetFilteredMetrics()
        {
            var frame = _runner.DebugLastFrame;
            if (frame == null)
                return new List<BtDebugMetric>();

            IEnumerable<BtDebugMetric> query = frame.Metrics;
            if (_selectedNodeOnlyToggle != null && _selectedNodeOnlyToggle.value && !string.IsNullOrEmpty(_selectedNodeId))
                query = query.Where(m => m.NodeId == _selectedNodeId);

            return query.ToList();
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
                UpdateInspectorDebugSummary();
                UpdateDebugView();
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
            UpdateDebugView();
            Repaint();
        }


        private void SaveDebugSnapshot()
        {
            if (_runner == null)
            {
                EditorUtility.DisplayDialog("BT Debug Export", "활성 Runner가 없습니다.", "OK");
                return;
            }

            if (_asset == null)
            {
                EditorUtility.DisplayDialog("BT Debug Export", "Tree Asset이 선택되어 있지 않습니다.", "OK");
                return;
            }

            var exportData = BtDebugExportBuilder.Build(_runner, _asset, _selectedNodeId, _debugTab, _selectedNodeOnlyToggle != null && _selectedNodeOnlyToggle.value);
            string defaultFileName = BuildDefaultDebugExportFileName();
            string path = EditorUtility.SaveFilePanel("Save BT Debug Snapshot", string.Empty, defaultFileName, "json");
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                BtDebugExportWriter.WriteJson(path, exportData);
                UpdateStatus($"Debug snapshot saved: {System.IO.Path.GetFileName(path)}");
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("BT Debug Export", $"디버그 저장 중 오류가 발생했습니다.\n{ex.Message}", "OK");
            }
        }

        private string BuildDefaultDebugExportFileName()
        {
            string assetName = SanitizeFileName(_asset != null ? _asset.name : "bt_asset");
            string runnerName = SanitizeFileName(_runner != null ? _runner.name : "runner");
            return $"{assetName}_{runnerName}_bt_debug_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "untitled";

            var invalidChars = System.IO.Path.GetInvalidFileNameChars();
            var chars = value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray();
            return new string(chars);
        }

        private string GetNodeDisplayTitleWithFallback(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
                return "-";

            var node = _asset?.FindNode(nodeId);
            if (node == null)
                return nodeId;

            return string.IsNullOrEmpty(node.title) ? node.id : node.title;
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

            _statusLabel.text = string.IsNullOrEmpty(message)
                ? $"Asset: {_asset.name} | Nodes: {_asset.nodes.Count}"
                : $"Asset: {_asset.name} | {message}";
        }

        public void MarkDirty(string reason) => UpdateStatus(reason);

        private sealed class DebugRowData
        {
            public string Col1;
            public string Col2;
            public string Col3;
            public string Col4;
            public string Col5;
        }
    }
}
#endif
