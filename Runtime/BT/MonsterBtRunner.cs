using System;
using System.Collections.Generic;
using UnityEngine;
using GGemCo2DCore;
using GGemCo2DSkill;
using Config;

namespace GGemCo2DAiBt
{
    /// <summary>
    /// 몬스터 전투 Behavior Tree 런너.
    /// </summary>
    /// <remarks>
    /// - 결정(Decision): 본 컴포넌트가 BT를 평가한다.
    /// - 실행(Execution): Core의 <see cref="IMonsterCombatDriver"/> 구현체(기본은 <c>ControllerMonster</c>)가 수행한다.
    /// - Core 패키지는 본 BT 패키지를 참조하지 않는다(의존성 단방향).
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MonsterBtRunner : MonoBehaviour, IMonsterBrainTickable, IMonsterPoolLifecycle
    {
        private MonsterBehaviorTreeAsset _treeAsset;

        // 0 이면 Update 프레임마다 평가한다. 0보다 크면 해당 Hz로 평가한다.
        private float _tickRateHz;

        private bool _enableDebug;
        private bool _enableDebugLog;
        // 디자이너/디버그 창에서 실행 노드 하이라이트를 위해 트레이스를 수집한다.
        private bool _enableDebugTrace;
        // 디버그 트레이스의 최대 방문 노드 기록 개수(한 틱 기준).
        private int _debugTraceCapacity;
        // 디버그 메트릭의 최대 기록 개수(한 틱 기준).
        private int _debugMetricCapacity;
        // 에디터 디버그 타임라인용 최근 프레임 보관 개수.
        private int _debugHistoryCapacity;
        // 디버그 브레이크포인트 사용 여부.
        private bool _enableDebugBreakpoints;

        public string DebugActiveNodeId { get; private set; }
        public string DebugActiveExecutionKey { get; private set; }
        public IReadOnlyList<string> DebugActivePath => _debugActivePath;
        public IReadOnlyList<string> DebugActiveExecutionPath => _debugActiveExecutionPath;
        public IReadOnlyList<BtDebugNodeResult> DebugLastTick => _debugLastTick;
        public IReadOnlyList<BtDebugMetric> DebugLastMetrics => _debugMetrics;
        public BtDebugFrame DebugLastFrame { get; private set; }
        public IReadOnlyList<BtDebugFrame> DebugHistory => _debugHistory;
        public bool DebugFreeze { get; private set; }
        public bool DebugBreakpointsEnabled
        {
            get => _enableDebugBreakpoints;
            set => _enableDebugBreakpoints = value;
        }
        public BtDebugBreakInfo DebugLastBreakInfo { get; private set; }
        public event Action<MonsterBtRunner> DebugTicked;

        private readonly List<string> _debugActivePath = new();
        private readonly List<string> _debugActiveExecutionPath = new();
        private readonly List<BtDebugNodeResult> _debugLastTick = new();
        private readonly List<BtDebugMetric> _debugMetrics = new();
        private readonly List<string> _execStack = new();
        private readonly List<string> _execNodeStack = new();
        private readonly List<BtDebugFrame> _debugHistory = new();
        private readonly BtDebugFrame _currentDebugFrame = new();
        private readonly Dictionary<string, BtDebugBreakpoint> _breakpoints = new(StringComparer.Ordinal);

        private readonly Dictionary<string, BtNodeRecord> _nodeById = new(StringComparer.Ordinal);
        private BtRuntimeState _runtime;
        private RuntimeBlackboard _blackboard;
        private float _nextTickTime;

        private IMonsterCombatDriver _driver;
        private IMonsterSkillDriver _skillDriver;
        private IMonsterBrainSuspendProvider _suspendProvider;
        private CharacterBase _ownerCharacterBase;

        private bool _isExecuting;
        private bool _breakTriggeredThisTick;
        private int _debugStepRequestCount;
        private bool _hasPendingTreeChange;
        private MonsterBehaviorTreeAsset _pendingTreeAsset;
        private BtTreeSwitchMode _pendingSwitchMode = BtTreeSwitchMode.ResetAll;

        public int Priority => 100;

        public bool IsActive => enabled && isActiveAndEnabled && _treeAsset != null &&
                                !string.IsNullOrEmpty(_treeAsset.rootNodeId);

        /// <summary>
        /// 런타임 BT가 교체되었을 때 호출된다.
        /// </summary>
        public event Action<MonsterBtRunner, MonsterBehaviorTreeAsset, MonsterBehaviorTreeAsset> TreeChanged;

        /// <summary>
        /// 런타임에 본 몬스터의 Behavior Tree 에셋을 교체한다.
        /// </summary>
        /// <remarks>
        /// - 교체 시 노드 캐시/런타임 상태/블랙보드가 재구성된다.
        /// - 실행 중(틱 평가 중) 호출되면 다음 프레임 틱 시작 전에 지연 적용된다.
        /// </remarks>
        public void SetTree(MonsterBehaviorTreeAsset asset, BtTreeSwitchMode mode = BtTreeSwitchMode.ResetAll)
        {
            if (asset == _treeAsset && !_hasPendingTreeChange)
                return;

            if (_isExecuting)
            {
                _pendingTreeAsset = asset;
                _pendingSwitchMode = mode;
                _hasPendingTreeChange = true;
                return;
            }

            ApplyTreeChange(asset, mode);
        }

        /// <summary>
        /// BT 교체 시 런타임 상태 보존 정책.
        /// </summary>
        public enum BtTreeSwitchMode
        {
            /// <summary>런타임 상태/블랙보드/스킬 사용 횟수까지 모두 초기화한다.</summary>
            ResetAll = 0,

            /// <summary>블랙보드의 공통 키 값을 복사한다(스킬 사용 횟수 캐시는 초기화).</summary>
            PreserveBlackboardValues = 1,

            /// <summary>블랙보드의 공통 키 값 + 스킬 사용 횟수 캐시를 유지한다.</summary>
            PreserveBlackboardAndSkillUseCounts = 2,
        }

        public void SetDebugFreeze(bool freeze)
        {
            DebugFreeze = freeze;
        }

        public void ToggleDebugFreeze()
        {
            DebugFreeze = !DebugFreeze;
        }

        public void RequestDebugStep(int count = 1)
        {
            _debugStepRequestCount += Mathf.Max(1, count);
        }

        public void ClearDebugHistory()
        {
            _debugHistory.Clear();
            DebugLastFrame = null;
            DebugLastBreakInfo = default;
        }

        public bool TryGetBreakpoint(string nodeId, out BtDebugBreakpoint breakpoint)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                breakpoint = default;
                return false;
            }

            return _breakpoints.TryGetValue(nodeId, out breakpoint);
        }

        public bool HasBreakpoint(string nodeId)
        {
            return !string.IsNullOrEmpty(nodeId) && _breakpoints.ContainsKey(nodeId);
        }

        public void SetBreakpoint(BtDebugBreakpoint breakpoint)
        {
            if (string.IsNullOrEmpty(breakpoint.NodeId))
                return;

            if (breakpoint.IsEmpty)
            {
                _breakpoints.Remove(breakpoint.NodeId);
                return;
            }

            _breakpoints[breakpoint.NodeId] = breakpoint;
        }

        public void RemoveBreakpoint(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
                return;

            _breakpoints.Remove(nodeId);
        }

        private void ApplyTreeChange(MonsterBehaviorTreeAsset newAsset, BtTreeSwitchMode mode)
        {
            var prev = _treeAsset;
            var prevBlackboard = _blackboard;

            _treeAsset = newAsset;

            // 기본은 전체 초기화
            RebuildCache();

            if (newAsset != null && prevBlackboard != null)
            {
                if (mode == BtTreeSwitchMode.PreserveBlackboardValues)
                {
                    _blackboard.CopyCommonValuesFrom(prevBlackboard, includeSkillUseCounts: false);
                }
                else if (mode == BtTreeSwitchMode.PreserveBlackboardAndSkillUseCounts)
                {
                    _blackboard.CopyCommonValuesFrom(prevBlackboard, includeSkillUseCounts: true);
                }
            }

            _hasPendingTreeChange = false;
            _pendingTreeAsset = null;

            TreeChanged?.Invoke(this, prev, newAsset);
        }

        private void Awake()
        {
            RebuildCache();
            _ownerCharacterBase = GetComponent<CharacterBase>();

            var aiBtSettings = AddressableLoaderSettingsAiBt.Instance.aiBtSettings;
            if (GcLogger.IsNull(aiBtSettings, $"{nameof(GGemCoAiBtSettings)}이 설정되어 있지 않습니다."))
            {
                enabled = false;
                return;
            }
            
            _tickRateHz = aiBtSettings.tickRateHz;
            _enableDebug = aiBtSettings.EnableDebug;
            _enableDebugLog = aiBtSettings.enableDebugLog;
            _enableDebugTrace = aiBtSettings.enableDebugTrace;
            _debugTraceCapacity = aiBtSettings.debugTraceCapacity;
            _debugMetricCapacity = aiBtSettings.debugMetricCapacity;
            _debugHistoryCapacity = aiBtSettings.debugHistoryCapacity;
            _enableDebugBreakpoints = aiBtSettings.enableDebugBreakpoints;
            if (!_enableDebug)
            {
                _enableDebugLog = false;
                _enableDebugTrace = false;
                _enableDebugBreakpoints = false;
            }
        }

        private void OnEnable()
        {
            _nextTickTime = 0f;
        }

        public void ResetForPoolReturn()
        {
            _hasPendingTreeChange = false;
            _pendingTreeAsset = null;
            _pendingSwitchMode = BtTreeSwitchMode.ResetAll;
            _isExecuting = false;
            _breakTriggeredThisTick = false;
            _debugStepRequestCount = 0;
            _nextTickTime = 0f;
            DebugFreeze = false;
            DebugLastBreakInfo = default;
            DebugActiveNodeId = null;
            DebugActiveExecutionKey = null;
            _driver = null;
            _skillDriver = null;
            _suspendProvider = null;
            ClearDebugHistory();
            RebuildCache();
            enabled = false;
        }

        public void OnPoolRent(Monster owner)
        {
            ResetForPoolReturn();
        }

        public void OnPoolReturn(Monster owner)
        {
            ResetForPoolReturn();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
                RebuildCache();
        }

        private void RebuildCache()
        {
            _nodeById.Clear();
            _runtime = new BtRuntimeState();
            _blackboard = new RuntimeBlackboard(_treeAsset != null ? _treeAsset.blackboardSchema : null);

            if (_treeAsset == null || _treeAsset.nodes == null) return;
            foreach (var node in _treeAsset.nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.id)) continue;
                _nodeById[node.id] = node;
            }
        }

        public void Tick()
        {
            if (_hasPendingTreeChange)
            {
                ApplyTreeChange(_pendingTreeAsset, _pendingSwitchMode);
            }

            if (!IsActive) return;
            if (!MonsterBrainSelector.IsHighestPriority(this, gameObject)) return;

            // Monster가 런타임에 ControllerMonster를 AddComponent 하는 구조이므로,
            // 최초 몇 프레임은 드라이버가 아직 없을 수 있다. 매 틱 느슨하게 획득한다.
            if (_driver == null)
                _driver = GetComponent<IMonsterCombatDriver>();
            if (_skillDriver == null)
                _skillDriver = GetComponent<IMonsterSkillDriver>();
            if (_suspendProvider == null)
                _suspendProvider = GetComponent<IMonsterBrainSuspendProvider>();
            if (_ownerCharacterBase == null)
                _ownerCharacterBase = GetComponent<CharacterBase>();

            if (_driver == null) return;

            // 히트 스톱 중에는 BT 시간 진행 자체를 멈춘다.
            if (_ownerCharacterBase != null && _ownerCharacterBase.IsHitStopped) return;

            // 그로기/기절/컷씬 등으로 Brain 평가를 중지해야 하는 경우, 이번 틱은 스킵한다.
            if (_suspendProvider != null && _suspendProvider.ShouldSuspendBrain) return;

            float now = Time.time;
            if (_tickRateHz > 0f)
            {
                if (now < _nextTickTime) return;
                _nextTickTime = now + (1f / _tickRateHz);
            }

            if (!CanRunThisTick())
                return;

            bool appliedRootRestart = _runtime.ConsumeRestartRootRequest(out var restartNodeId, out var restartExecutionKey, out var restartReason);
            if (appliedRootRestart)
            {
                _runtime.ClearExecutionStateForRootRestart();
            }

            _runtime.TickIndex++;
            _runtime.LastTickTime = now;

            _breakTriggeredThisTick = false;

            if (_enableDebugTrace)
            {
                _debugActivePath.Clear();
                _debugActiveExecutionPath.Clear();
                _debugLastTick.Clear();
                _debugMetrics.Clear();
                _execStack.Clear();
                _execNodeStack.Clear();
                DebugActiveNodeId = null;
                DebugActiveExecutionKey = null;
                _currentDebugFrame.Reset(_runtime.TickIndex, now, _treeAsset.rootNodeId);

                if (appliedRootRestart)
                {
                    string appliedSummary = $"requestedByNode={restartNodeId}, requestedByExecutionKey={restartExecutionKey}, reason={restartReason}";
                    AddEvent(string.IsNullOrEmpty(restartNodeId) ? _treeAsset.rootNodeId : restartNodeId,
                        string.IsNullOrEmpty(restartExecutionKey) ? _treeAsset.rootNodeId : restartExecutionKey,
                        BtDebugEventKind.System,
                        "RestartRootApplied",
                        BtStatus.Success,
                        BtDebugReason.RootRestartApplied,
                        appliedSummary);
                }
            }

            if (!_nodeById.ContainsKey(_treeAsset.rootNodeId))
            {
                if (_enableDebugLog) Debug.LogWarning($"[BT] Root node not found. root={_treeAsset.rootNodeId}", this);
                return;
            }

            var ctx = new BtContext(this, _driver, _skillDriver, _blackboard, _runtime, _enableDebugLog);
            BtStatus rootStatus = BtStatus.Failure;
            try
            {
                _isExecuting = true;
                rootStatus = ExecuteNode(_treeAsset.rootNodeId, ctx, depth: 0, executionKey: _treeAsset.rootNodeId);
            }
            finally
            {
                _isExecuting = false;
            }

            if (_enableDebugTrace)
            {
                DebugActiveNodeId = _debugActivePath.Count > 0 ? _debugActivePath[_debugActivePath.Count - 1] : null;
                DebugActiveExecutionKey = _debugActiveExecutionPath.Count > 0 ? _debugActiveExecutionPath[_debugActiveExecutionPath.Count - 1] : null;
                CompleteDebugFrame(rootStatus);
                DebugTicked?.Invoke(this);
            }
        }

        private BtStatus ExecuteNode(string nodeId, BtContext ctx, int depth, string executionKey)
        {
            if (depth > 64) return BtStatus.Failure; // 순환/과도한 깊이 방어
            if (!_nodeById.TryGetValue(nodeId, out var node) || node == null) return BtStatus.Failure;

            if (_enableDebugTrace)
            {
                _execStack.Add(executionKey);
                _execNodeStack.Add(nodeId);
            }

            BtStatus status = node.kind switch
            {
                BtNodeKind.Composite => ExecuteComposite(node, ctx, depth, executionKey),
                BtNodeKind.Decorator => ExecuteDecorator(node, ctx, depth, executionKey),
                BtNodeKind.Condition => ExecuteCondition(node, ctx, executionKey),
                BtNodeKind.Action => ExecuteAction(node, ctx, executionKey),
                _ => BtStatus.Failure,
            };

            if (_enableDebugTrace)
            {
                if (_debugLastTick.Count < _debugTraceCapacity)
                    _debugLastTick.Add(new BtDebugNodeResult(nodeId, executionKey, status, depth));

                // 가장 깊은 Running 경로를 최초 1회만 캡처한다.
                if (status == BtStatus.Running && _debugActivePath.Count == 0)
                {
                    _debugActivePath.AddRange(_execNodeStack);
                    _debugActiveExecutionPath.AddRange(_execStack);
                    DebugActiveExecutionKey = executionKey;
                }

                // pop
                if (_execStack.Count > 0)
                    _execStack.RemoveAt(_execStack.Count - 1);
                if (_execNodeStack.Count > 0)
                    _execNodeStack.RemoveAt(_execNodeStack.Count - 1);
            }

            return status;
        }


        private bool CanRunThisTick()
        {
            if (!_enableDebugTrace)
                return true;

            if (!DebugFreeze)
                return true;

            if (_debugStepRequestCount > 0)
            {
                _debugStepRequestCount--;
                return true;
            }

            return false;
        }

        private void CompleteDebugFrame(BtStatus rootStatus)
        {
            _currentDebugFrame.RootStatus = rootStatus;
            _currentDebugFrame.ActiveNodeId = DebugActiveNodeId;
            _currentDebugFrame.ActiveExecutionKey = DebugActiveExecutionKey;
            _currentDebugFrame.ActivePath.AddRange(_debugActivePath);
            _currentDebugFrame.ActiveExecutionPath.AddRange(_debugActiveExecutionPath);
            _currentDebugFrame.Visits.AddRange(_debugLastTick);
            _currentDebugFrame.Metrics.AddRange(_debugMetrics);

            DebugLastFrame = _currentDebugFrame.Clone();
            _debugHistory.Add(DebugLastFrame);
            while (_debugHistory.Count > _debugHistoryCapacity)
                _debugHistory.RemoveAt(0);
        }

        private void AddEvent(string nodeId, string executionKey, BtDebugEventKind kind, string title, BtStatus status, BtDebugReason reason, string summary)
        {
            if (!_enableDebugTrace) return;

            var ev = new BtDebugEvent(nodeId, executionKey, kind, title, status, reason, summary);
            _currentDebugFrame.Events.Add(ev);
            EvaluateBreakpoint(ev);
        }

        private void EvaluateBreakpoint(BtDebugEvent ev)
        {
            if (!_enableDebugBreakpoints)
                return;
            if (_breakTriggeredThisTick)
                return;
            if (string.IsNullOrEmpty(ev.NodeId))
                return;
            if (!_breakpoints.TryGetValue(ev.NodeId, out var breakpoint))
                return;

            bool shouldBreak = breakpoint.BreakOnVisit;
            if (breakpoint.BreakOnSuccess && ev.Status == BtStatus.Success)
                shouldBreak = true;
            if (breakpoint.BreakOnFailure && ev.Status == BtStatus.Failure)
                shouldBreak = true;
            if (breakpoint.BreakOnRunning && ev.Status == BtStatus.Running)
                shouldBreak = true;

            if (!shouldBreak)
                return;

            _breakTriggeredThisTick = true;
            DebugFreeze = true;
            DebugLastBreakInfo = new BtDebugBreakInfo(_runtime != null ? _runtime.TickIndex : 0, string.IsNullOrEmpty(ev.ExecutionKey) ? ev.NodeId : ev.ExecutionKey, ev.Status, ev.Reason == BtDebugReason.None ? BtDebugReason.BreakpointMatched : ev.Reason, ev.Summary);
        }

        private void AddMetric(string nodeId, string executionKey, string key, float value, string text = null)
        {
            if (!_enableDebugTrace) return;
            if (_debugMetrics.Count >= _debugMetricCapacity) return;
            _debugMetrics.Add(new BtDebugMetric(nodeId, executionKey, key, value, text));
        }

        private static void ResetCompositeState(BtNodeState nodeState)
        {
            if (nodeState == null)
                return;

            nodeState.RunningChildIndex = 0;
            nodeState.SelectedChildIndex = -1;
            nodeState.LastStatus = BtStatus.Failure;
        }

        private void ResetSubtreeRuntime(string executionKey)
        {
            if (string.IsNullOrEmpty(executionKey) || _runtime == null)
                return;

            var keys = ListExecutionScopeKeys(executionKey, includeSelf: false);
            for (int i = 0; i < keys.Count; i++)
            {
                _runtime.NodeStates.Remove(keys[i]);
                _runtime.Timeouts.Remove(keys[i]);
            }
        }

        private List<string> ListExecutionScopeKeys(string executionKeyPrefix, bool includeSelf)
        {
            var keys = new List<string>();
            if (string.IsNullOrEmpty(executionKeyPrefix) || _runtime == null)
                return keys;

            foreach (var kv in _runtime.NodeStates)
            {
                if (string.Equals(kv.Key, executionKeyPrefix, StringComparison.Ordinal))
                {
                    if (includeSelf)
                        keys.Add(kv.Key);
                    continue;
                }

                if (kv.Key.StartsWith(executionKeyPrefix + "/", StringComparison.Ordinal))
                    keys.Add(kv.Key);
            }

            foreach (var kv in _runtime.Timeouts)
            {
                if (string.Equals(kv.Key, executionKeyPrefix, StringComparison.Ordinal))
                {
                    if (includeSelf && !keys.Contains(kv.Key))
                        keys.Add(kv.Key);
                    continue;
                }

                if (kv.Key.StartsWith(executionKeyPrefix + "/", StringComparison.Ordinal) && !keys.Contains(kv.Key))
                    keys.Add(kv.Key);
            }

            return keys;
        }

        private static string GetChildExecutionKey(string parentExecutionKey, string childId, int childIndex)
        {
            if (string.IsNullOrEmpty(parentExecutionKey))
                return $"{childId}[{childIndex}]";

            return $"{parentExecutionKey}/{childId}[{childIndex}]";
        }

        #region Composite
        private BtStatus ExecuteComposite(BtNodeRecord node, BtContext ctx, int depth, string executionKey)
        {
            if (node.children == null || node.children.Count == 0) return BtStatus.Failure;

            switch (node.typeId)
            {
                case BtTypeIds.Composite.Selector:
                {
                    var nodeState = ctx.Runtime.GetOrCreateNodeState(executionKey, node.id);
                    int startIndex = Mathf.Clamp(nodeState.RunningChildIndex, 0, node.children.Count - 1);

                    for (int i = startIndex; i < node.children.Count; i++)
                    {
                        string childExecutionKey = GetChildExecutionKey(executionKey, node.children[i], i);
                        var st = ExecuteNode(node.children[i], ctx, depth + 1, childExecutionKey);
                        if (_breakTriggeredThisTick)
                        {
                            AddEvent(node.id, executionKey, BtDebugEventKind.Composite, "Selector", st, BtDebugReason.BreakpointMatched, $"break after child[{i}] => {st}");
                            return st;
                        }

                        if (st == BtStatus.Failure)
                            continue;

                        nodeState.LastStatus = st;

                        if (st == BtStatus.Running)
                        {
                            nodeState.RunningChildIndex = i;
                            AddEvent(node.id, executionKey, BtDebugEventKind.Composite, "Selector", BtStatus.Running, BtDebugReason.None, $"resume child[{i}] => Running");
                            return BtStatus.Running;
                        }

                        ResetCompositeState(nodeState);
                        AddEvent(node.id, executionKey, BtDebugEventKind.Composite, "Selector", st, BtDebugReason.None, $"child[{i}] => {st}");
                        return st;
                    }

                    ResetCompositeState(nodeState);
                    AddEvent(node.id, executionKey, BtDebugEventKind.Composite, "Selector", BtStatus.Failure, BtDebugReason.ChildMissing, $"all {node.children.Count} children failed (startIndex={startIndex})");
                    return BtStatus.Failure;
                }

                case BtTypeIds.Composite.Sequence:
                {
                    var nodeState = ctx.Runtime.GetOrCreateNodeState(executionKey, node.id);
                    int startIndex = Mathf.Clamp(nodeState.RunningChildIndex, 0, node.children.Count - 1);

                    for (int i = startIndex; i < node.children.Count; i++)
                    {
                        string childExecutionKey = GetChildExecutionKey(executionKey, node.children[i], i);
                        var st = ExecuteNode(node.children[i], ctx, depth + 1, childExecutionKey);
                        if (_breakTriggeredThisTick)
                        {
                            AddEvent(node.id, executionKey, BtDebugEventKind.Composite, "Sequence", st, BtDebugReason.BreakpointMatched, $"break after child[{i}] => {st}");
                            return st;
                        }

                        nodeState.LastStatus = st;

                        if (st == BtStatus.Success)
                            continue;

                        if (st == BtStatus.Running)
                        {
                            nodeState.RunningChildIndex = i;
                            AddEvent(node.id, executionKey, BtDebugEventKind.Composite, "Sequence", BtStatus.Running, BtDebugReason.None, $"resume child[{i}] => Running");
                            return BtStatus.Running;
                        }

                        ResetCompositeState(nodeState);
                        ResetSubtreeRuntime(executionKey);
                        AddEvent(node.id, executionKey, BtDebugEventKind.Composite, "Sequence", BtStatus.Failure, BtDebugReason.None, $"child[{i}] => Failure");
                        return BtStatus.Failure;
                    }

                    ResetCompositeState(nodeState);
                    ResetSubtreeRuntime(executionKey);
                    AddEvent(node.id, executionKey, BtDebugEventKind.Composite, "Sequence", BtStatus.Success, BtDebugReason.None, $"all {node.children.Count} children succeeded (startIndex={startIndex})");
                    return BtStatus.Success;
                }

                case BtTypeIds.Composite.RandomWeighted:
                {
                    var nodeState = ctx.Runtime.GetOrCreateNodeState(executionKey, node.id);
                    int pick = nodeState.SelectedChildIndex;
                    bool reusedSelection = pick >= 0 && pick < node.children.Count;

                    if (!reusedSelection)
                    {
                        pick = ctx.RandomPickWeighted(node.children, node.parameters);
                        if (pick < 0 || pick >= node.children.Count)
                        {
                            ResetCompositeState(nodeState);
                            AddEvent(node.id, executionKey, BtDebugEventKind.Composite, "RandomWeighted", BtStatus.Failure, BtDebugReason.RandomPickFailed, $"pick={pick}, childCount={node.children.Count}");
                            return BtStatus.Failure;
                        }

                        nodeState.SelectedChildIndex = pick;
                    }

                    var pickedExecutionKey = GetChildExecutionKey(executionKey, node.children[pick], pick);
                    var pickedStatus = ExecuteNode(node.children[pick], ctx, depth + 1, pickedExecutionKey);
                    if (_breakTriggeredThisTick)
                    {
                        AddEvent(node.id, executionKey, BtDebugEventKind.Composite, "RandomWeighted", pickedStatus, BtDebugReason.BreakpointMatched, $"break after child[{pick}] => {pickedStatus}");
                        return pickedStatus;
                    }

                    nodeState.LastStatus = pickedStatus;
                    if (pickedStatus == BtStatus.Running)
                    {
                        AddEvent(node.id, executionKey, BtDebugEventKind.Composite, "RandomWeighted", BtStatus.Running, BtDebugReason.None, $"selected child[{pick}] => Running / reusedSelection={reusedSelection}");
                        return BtStatus.Running;
                    }

                    ResetCompositeState(nodeState);
                    if (pickedStatus != BtStatus.Running)
                        ResetSubtreeRuntime(executionKey);

                    AddEvent(node.id, executionKey, BtDebugEventKind.Composite, "RandomWeighted", pickedStatus, BtDebugReason.None, $"selected child[{pick}] => {pickedStatus} / reusedSelection={reusedSelection} / childCount={node.children.Count}");
                    return pickedStatus;
                }

                default:
                    return BtStatus.Failure;
            }
        }
        #endregion

        #region Decorator
        private BtStatus ExecuteDecorator(BtNodeRecord node, BtContext ctx, int depth, string executionKey)
        {
            if (node.children == null || node.children.Count != 1) return BtStatus.Failure;
            string childId = node.children[0];

            switch (node.typeId)
            {
                case BtTypeIds.Decorator.Cooldown:
                {
                    string key = ctx.GetStringParam(node, "key", fallback: "");
                    float seconds = ctx.GetFloatParam(node, "sec", fallback: 0f);
                    if (string.IsNullOrEmpty(key) || seconds <= 0f)
                    {
                        var bypassExecutionKey = GetChildExecutionKey(executionKey, childId, 0);
                        var bypass = ExecuteNode(childId, ctx, depth + 1, bypassExecutionKey);
                        AddEvent(node.id, executionKey, BtDebugEventKind.Decorator, "Cooldown", bypass, BtDebugReason.InvalidParameter, $"bypass key={key}, sec={seconds:0.###}");
                        return bypass;
                    }

                    if (!ctx.IsCooldownReady(key))
                    {
                        AddEvent(node.id, executionKey, BtDebugEventKind.Decorator, "Cooldown", BtStatus.Failure, BtDebugReason.CooldownNotReady, $"key={key}, sec={seconds:0.###}");
                        return BtStatus.Failure;
                    }

                    var childExecutionKey = GetChildExecutionKey(executionKey, childId, 0);
                    var st = ExecuteNode(childId, ctx, depth + 1, childExecutionKey);
                    if (_breakTriggeredThisTick)
                    {
                        AddEvent(node.id, executionKey, BtDebugEventKind.Decorator, "Cooldown", st, BtDebugReason.BreakpointMatched, $"break after child => {st}, key={key}");
                        return st;
                    }
                    if (st != BtStatus.Failure)
                        ctx.ConsumeCooldown(key, seconds);
                    AddEvent(node.id, executionKey, BtDebugEventKind.Decorator, "Cooldown", st, BtDebugReason.None, $"key={key}, sec={seconds:0.###}");
                    return st;
                }

                case BtTypeIds.Decorator.Timeout:
                {
                    float seconds = ctx.GetFloatParam(node, "sec", fallback: 0f);
                    if (seconds <= 0f)
                    {
                        var bypassExecutionKey = GetChildExecutionKey(executionKey, childId, 0);
                        var bypass = ExecuteNode(childId, ctx, depth + 1, bypassExecutionKey);
                        AddEvent(node.id, executionKey, BtDebugEventKind.Decorator, "Timeout", bypass, BtDebugReason.InvalidParameter, $"bypass sec={seconds:0.###}");
                        return bypass;
                    }

                    if (ctx.IsTimeoutExceeded(executionKey, seconds))
                    {
                        AddEvent(node.id, executionKey, BtDebugEventKind.Decorator, "Timeout", BtStatus.Failure, BtDebugReason.TimeoutExceeded, $"sec={seconds:0.###}");
                        return BtStatus.Failure;
                    }

                    var childExecutionKey = GetChildExecutionKey(executionKey, childId, 0);
                    var st = ExecuteNode(childId, ctx, depth + 1, childExecutionKey);
                    if (_breakTriggeredThisTick)
                    {
                        AddEvent(node.id, executionKey, BtDebugEventKind.Decorator, "Timeout", st, BtDebugReason.BreakpointMatched, $"break after child => {st}, sec={seconds:0.###}");
                        return st;
                    }
                    if (st == BtStatus.Success || st == BtStatus.Failure)
                        ctx.ResetTimeout(executionKey);
                    AddEvent(node.id, executionKey, BtDebugEventKind.Decorator, "Timeout", st, BtDebugReason.None, $"sec={seconds:0.###}");
                    return st;
                }

                default:
                    return BtStatus.Failure;
            }
        }
        #endregion

        #region Condition
        private BtStatus ExecuteCondition(BtNodeRecord node, BtContext ctx, string executionKey)
        {
            bool ok;

            switch (node.typeId)
            {
                case BtTypeIds.Condition.HasAggroTarget:
                {
                    ok = ctx.HasAggroTarget();
                    AddMetric(node.id, executionKey, "HasAggroTarget", ok ? 1f : 0f);
                    AddEvent(node.id, executionKey, BtDebugEventKind.Condition, "HasAggroTarget", ok ? BtStatus.Success : BtStatus.Failure, ok ? BtDebugReason.None : BtDebugReason.NoTarget, $"HasAggroTarget => {ok}");
                    break;
                }

                case BtTypeIds.Condition.InAttackRange:
                {
                    ok = ctx.Driver.IsTargetInAttackRange();
                    AddMetric(node.id, executionKey, "InAttackRange", ok ? 1f : 0f);
                    AddEvent(node.id, executionKey, BtDebugEventKind.Condition, "InAttackRange", ok ? BtStatus.Success : BtStatus.Failure, ok ? BtDebugReason.None : BtDebugReason.OutOfRange, $"InAttackRange => {ok}");
                    break;
                }

                case BtTypeIds.Condition.HpPercentBelow:
                {
                    float threshold = Mathf.Clamp01(ctx.GetFloatParam(node, "threshold", 0.25f));
                    float hp = Mathf.Clamp01(ctx.Driver.HpPercent);
                    ok = hp < threshold;
                    AddMetric(node.id, executionKey, "HpPercent", hp);
                    AddMetric(node.id, executionKey, "Threshold", threshold);
                    AddMetric(node.id, executionKey, "Result", ok ? 1f : 0f);
                    AddEvent(node.id, executionKey, BtDebugEventKind.Condition, "HpPercentBelow", ok ? BtStatus.Success : BtStatus.Failure, ok ? BtDebugReason.None : BtDebugReason.HpNotBelowThreshold, $"{hp:0.###} < {threshold:0.###} => {ok}");
                    break;
                }

                case BtTypeIds.Condition.TargetWithinDistance:
                {
                    float max = Mathf.Max(0f, ctx.GetFloatParam(node, "max", 12f));
                    float dist = ctx.GetTargetDistance(out bool hasTarget);
                    ok = hasTarget && dist <= max;
                    AddMetric(node.id, executionKey, "Distance", hasTarget ? dist : -1f, hasTarget ? null : "No target");
                    AddMetric(node.id, executionKey, "Max", max);
                    AddMetric(node.id, executionKey, "Result", ok ? 1f : 0f);
                    AddEvent(node.id, executionKey, BtDebugEventKind.Condition, "TargetWithinDistance", ok ? BtStatus.Success : BtStatus.Failure, ok ? BtDebugReason.None : (hasTarget ? BtDebugReason.OutOfRange : BtDebugReason.NoTarget), hasTarget ? $"{dist:0.###} <= {max:0.###} => {ok}" : "target missing");
                    break;
                }
                case BtTypeIds.Condition.CanUseSkill:
                {
                    int skillUid = ctx.GetIntParam(node, "skillUid", fallback: 0);
                    bool requireTarget = ctx.GetBoolParam(node, "requireTarget", fallback: true);

                    bool can =
                        ctx.SkillDriver != null &&
                        !ctx.SkillDriver.IsSkillBusy &&
                        skillUid > 0 &&
                        (!requireTarget || ctx.Driver.TryGetTarget(out var t) && t != null);

                    AddMetric(node.id, executionKey, "CanUseSkill", can ? 1f : 0f, $"{skillUid}");
                    AddEvent(node.id, executionKey, BtDebugEventKind.Condition, "CanUseSkill", can ? BtStatus.Success : BtStatus.Failure, can ? BtDebugReason.None : (skillUid <= 0 ? BtDebugReason.SkillUidInvalid : (ctx.SkillDriver != null && ctx.SkillDriver.IsSkillBusy ? BtDebugReason.SkillBusy : BtDebugReason.NoTarget)), $"skillUid={skillUid}, requireTarget={requireTarget}, can={can}");
                    ok = can;
                    break;
                }
                case BtTypeIds.Condition.IsSkillInCastRange:
                {
                    int skillUid = ctx.GetIntParam(node, "skillUid", fallback: 0);
                    bool requireTarget = ctx.GetBoolParam(node, "requireTarget", fallback: true);
                    float extraMargin = ctx.GetFloatParam(node, "extraMargin", fallback: 0f);

                    ok = TryIsSkillInCastRange(ctx, skillUid, requireTarget, extraMargin,
                        out float distance, out float castRange, out BtDebugReason failReason, out string detail);

                    AddMetric(node.id, executionKey, "SkillUid", skillUid);
                    AddMetric(node.id, executionKey, "Distance", distance, distance < 0f ? "No target" : null);
                    AddMetric(node.id, executionKey, "CastRange", castRange);
                    AddMetric(node.id, executionKey, "ExtraMargin", extraMargin);
                    AddMetric(node.id, executionKey, "Result", ok ? 1f : 0f);

                    AddEvent(
                        node.id,
                        executionKey,
                        BtDebugEventKind.Condition,
                        "IsSkillInCastRange",
                        ok ? BtStatus.Success : BtStatus.Failure,
                        ok ? BtDebugReason.None : failReason,
                        detail);
                    break;
                }
                case BtTypeIds.Condition.LastSkillResult:
                {
                    if (ctx.SkillDriver is not IMonsterSkillDriverFeedback feedback)
                    {
                        AddEvent(node.id, executionKey, BtDebugEventKind.Condition, "LastSkillResult", BtStatus.Failure, BtDebugReason.InvalidParameter, "Skill feedback driver missing");
                        ok = false;
                        break;
                    }

                    int skillUid = ctx.GetIntParam(node, "skillUid", fallback: 0);
                    string expected = ctx.GetEnumStringParam(node, "result", fallback: nameof(MonsterSkillExecutionState.Succeeded));
                    bool consume = ctx.GetBoolParam(node, "consume", fallback: true);

                    if (skillUid <= 0)
                    {
                        AddEvent(node.id, executionKey, BtDebugEventKind.Condition, "LastSkillResult", BtStatus.Failure, BtDebugReason.SkillUidInvalid, $"skillUid={skillUid}");
                        ok = false;
                        break;
                    }

                    MonsterSkillExecutionResult result;
                    bool hasResult = consume
                        ? feedback.ConsumeLastSkillResult(skillUid, out result)
                        : feedback.TryGetLastSkillResult(skillUid, out result);
                    if (!hasResult)
                    {
                        AddMetric(node.id, executionKey, "SkillUid", skillUid);
                        AddMetric(node.id, executionKey, "Result", 0f, "No result");
                        AddEvent(node.id, executionKey, BtDebugEventKind.Condition, "LastSkillResult", BtStatus.Failure, BtDebugReason.None, $"skillUid={skillUid}, no result");
                        ok = false;
                        break;
                    }


                    bool parsed = Enum.TryParse(expected, true, out MonsterSkillExecutionState expectedState);
                    if (!parsed)
                        expectedState = MonsterSkillExecutionState.Succeeded;

                    ok = result.State == expectedState;
                    AddMetric(node.id, executionKey, "SkillUid", skillUid);
                    AddMetric(node.id, executionKey, "Sequence", result.Sequence);
                    AddMetric(node.id, executionKey, "Result", ok ? 1f : 0f, result.State.ToString());
                    AddEvent(node.id, executionKey, BtDebugEventKind.Condition, "LastSkillResult", ok ? BtStatus.Success : BtStatus.Failure, ok ? BtDebugReason.None : BtDebugReason.InvalidParameter, $"skillUid={skillUid}, actual={result.State}, expected={expectedState}, sequence={result.Sequence}");
                    break;
                }
                case BtTypeIds.Condition.LastSkillCombatOutcome:
                {
                    if (ctx.SkillDriver is not IMonsterSkillDriverFeedback feedback)
                    {
                        AddEvent(node.id, executionKey, BtDebugEventKind.Condition, "LastSkillCombatOutcome", BtStatus.Failure, BtDebugReason.InvalidParameter, "Skill feedback driver missing");
                        ok = false;
                        break;
                    }

                    int skillUid = ctx.GetIntParam(node, "skillUid", fallback: 0);
                    string expected = ctx.GetEnumStringParam(node, "outcome", fallback: nameof(MonsterSkillCombatOutcome.Hit));
                    bool consume = ctx.GetBoolParam(node, "consume", fallback: true);

                    if (skillUid <= 0)
                    {
                        AddEvent(node.id, executionKey, BtDebugEventKind.Condition, "LastSkillCombatOutcome", BtStatus.Failure, BtDebugReason.SkillUidInvalid, $"skillUid={skillUid}");
                        ok = false;
                        break;
                    }

                    MonsterSkillCombatReport report;
                    bool hasReport = consume
                        ? feedback.ConsumeLastSkillCombatReport(skillUid, out report)
                        : feedback.TryGetLastSkillCombatReport(skillUid, out report);
                    if (!hasReport)
                    {
                        AddMetric(node.id, executionKey, "SkillUid", skillUid);
                        AddMetric(node.id, executionKey, "Result", 0f, "No combat report");
                        AddEvent(node.id, executionKey, BtDebugEventKind.Condition, "LastSkillCombatOutcome", BtStatus.Failure, BtDebugReason.None, $"skillUid={skillUid}, no combat report");
                        ok = false;
                        break;
                    }

                    bool parsed = Enum.TryParse(expected, true, out MonsterSkillCombatOutcome expectedOutcome);
                    if (!parsed)
                        expectedOutcome = MonsterSkillCombatOutcome.Hit;

                    ok = report.Outcome == expectedOutcome;
                    AddMetric(node.id, executionKey, "SkillUid", skillUid);
                    AddMetric(node.id, executionKey, "Sequence", report.Sequence);
                    AddMetric(node.id, executionKey, "AttackId", report.AttackId);
                    AddMetric(node.id, executionKey, "Result", ok ? 1f : 0f, report.Outcome.ToString());
                    AddEvent(node.id, executionKey, BtDebugEventKind.Condition, "LastSkillCombatOutcome", ok ? BtStatus.Success : BtStatus.Failure, ok ? BtDebugReason.None : BtDebugReason.InvalidParameter, $"skillUid={skillUid}, actual={report.Outcome}, expected={expectedOutcome}, sequence={report.Sequence}, attackId={report.AttackId}");
                    break;
                }
                case BtTypeIds.Condition.SkillUseCountCompare:
                {
                    int skillUid = ctx.GetIntParam(node, "skillUid", fallback: 0);
                    int value = ctx.GetIntParam(node, "value", fallback: 0);
                    string op = ctx.GetEnumStringParam(node, "op", fallback: ">=");
                    bool resetOnSuccess = ctx.GetBoolParam(node, "resetOnSuccess", fallback: false);

                    int count = ctx.Blackboard != null ? ctx.Blackboard.GetSkillUseCount(skillUid) : 0;

                    bool result = op switch
                    {
                        ">" => count > value,
                        ">=" => count >= value,
                        "==" => count == value,
                        "!=" => count != value,
                        "<" => count < value,
                        "<=" => count <= value,
                        _ => count >= value,
                    };

                    AddMetric(node.id, executionKey, "SkillUid", skillUid, null);
                    AddMetric(node.id, executionKey, "Count", count, null);
                    AddMetric(node.id, executionKey, "Value", value, op);
                    AddMetric(node.id, executionKey, "Result", result ? 1f : 0f);
                    AddEvent(node.id, executionKey, BtDebugEventKind.Condition, "SkillUseCountCompare", result ? BtStatus.Success : BtStatus.Failure, BtDebugReason.None, $"count({count}) {op} value({value}) => {result}, skillUid={skillUid}");

                    if (result && resetOnSuccess && ctx.Blackboard != null)
                    {
                        ctx.Blackboard.ResetSkillUseCount(skillUid);
                        AddEvent(node.id, executionKey, BtDebugEventKind.Blackboard, "SkillUseCountReset", BtStatus.Success, BtDebugReason.None, $"skillUid={skillUid} => 0 (resetOnSuccess)");
                    }

                    ok = result;
                    break;
                }
                case BtTypeIds.Condition.HasAffect:
                {
                    int affectUid = ctx.GetIntParam(node, "affectUid", 0);
                    ok = affectUid > 0 && AffectApi.Has(gameObject, affectUid);

                    AddMetric(node.id, executionKey, "AffectUid", affectUid);
                    AddMetric(node.id, executionKey, "HasAffect", ok ? 1f : 0f);
                    AddMetric(node.id, executionKey, "Result", ok ? 1f : 0f);

                    AddEvent(
                        node.id,
                        executionKey,
                        BtDebugEventKind.Condition,
                        "HasAffect",
                        ok ? BtStatus.Success : BtStatus.Failure,
                        ok ? BtDebugReason.None : BtDebugReason.None,
                        $"affectUid={affectUid}, has={ok}");

                    break;
                }
                default:
                    ok = false;
                    AddMetric(node.id, executionKey, "UnknownCondition", 0f, node.typeId);
                    AddEvent(node.id, executionKey, BtDebugEventKind.Condition, "UnknownCondition", BtStatus.Failure, BtDebugReason.UnknownType, node.typeId);
                    break;
            }

            return ok ? BtStatus.Success : BtStatus.Failure;
        }

        private bool TryIsSkillInCastRange(
            BtContext ctx,
            int skillUid,
            bool requireTarget,
            float extraMargin,
            out float distance,
            out float castRange,
            out BtDebugReason failReason,
            out string detail)
        {
            distance = -1f;
            castRange = 0f;
            failReason = BtDebugReason.None;
            detail = string.Empty;

            if (skillUid <= 0)
            {
                failReason = BtDebugReason.SkillUidInvalid;
                detail = $"invalid skillUid={skillUid}";
                return false;
            }

            if (!SkillDefinitionResolver.TryResolve(skillUid, ConfigCommon.SkillTableSource.Monster, out var skill) || skill == null)
            {
                failReason = BtDebugReason.SkillUidInvalid;
                detail = $"skill not found. skillUid={skillUid}";
                return false;
            }

            float resolvedCastRange = SkillRangeResolver.GetCastRange(skill);
            castRange = Mathf.Max(0f, resolvedCastRange + extraMargin);

            var targetingMode = (ConfigCommonSkill.SkillTargetingMode)Mathf.Clamp((int)skill.TargetingMode, 0, int.MaxValue);
            if (resolvedCastRange <= 0f)
            {
                detail = $"cast range unlimited. skillUid={skillUid}, mode={targetingMode}";
                return true;
            }

            if (targetingMode == ConfigCommonSkill.SkillTargetingMode.Self)
            {
                detail = $"self target skill. castRange={castRange:0.###}";
                return true;
            }

            bool hasTarget = ctx.Driver.TryGetTarget(out var targetTransform) && targetTransform != null;
            if (!hasTarget)
            {
                if (requireTarget)
                {
                    failReason = BtDebugReason.NoTarget;
                    detail = $"target missing. skillUid={skillUid}, mode={targetingMode}";
                    return false;
                }

                detail = $"target missing but requireTarget=false. skillUid={skillUid}, mode={targetingMode}";
                return true;
            }

            Vector3 origin = ctx.Owner.transform.position;
            Vector3 targetPoint = targetTransform.position;

            switch (targetingMode)
            {
                case ConfigCommonSkill.SkillTargetingMode.GroundTarget:
                case ConfigCommonSkill.SkillTargetingMode.LockOnGuaranteedHit:
                case ConfigCommonSkill.SkillTargetingMode.TargetCenteredArea:
                case ConfigCommonSkill.SkillTargetingMode.FollowTargetArea:
                    distance = Vector2.Distance(new Vector2(origin.x, origin.y), new Vector2(targetPoint.x, targetPoint.y));
                    break;

                default:
                    detail = $"mode={targetingMode} does not require cast range check. castRange={castRange:0.###}";
                    return true;
            }

            bool inRange = distance <= castRange;
            if (!inRange)
                failReason = BtDebugReason.OutOfRange;

            detail = $"skillUid={skillUid}, mode={targetingMode}, distance={distance:0.###}, castRange={castRange:0.###}, extraMargin={extraMargin:0.###}, result={inRange}";
            return inRange;
        }
        #endregion

        #region Action
        private BtStatus ExecuteAction(BtNodeRecord node, BtContext ctx, string executionKey)
        {
            switch (node.typeId)
            {
                case BtTypeIds.Action.Wait:
                {
                    float sec = ctx.GetFloatParam(node, "sec", 0.2f);
                    var st = ctx.WaitForSeconds(executionKey, sec);
                    AddEvent(node.id, executionKey, BtDebugEventKind.Action, "Wait", st, BtDebugReason.None, $"sec={sec:0.###}");
                    return st;
                }

                case BtTypeIds.Action.WaitOneTick:
                    AddEvent(node.id, executionKey, BtDebugEventKind.Action, "WaitOneTick", BtStatus.Running, BtDebugReason.None, "return Running for one tick");
                    return BtStatus.Running;

                case BtTypeIds.Action.Stop:
                    ctx.Driver.RequestWait();
                    AddEvent(node.id, executionKey, BtDebugEventKind.Action, "Stop", BtStatus.Success, BtDebugReason.None, "RequestWait()");
                    return BtStatus.Success;

                case BtTypeIds.Action.FaceToTarget:
                    ctx.Driver.RequestFaceToTarget();
                    AddEvent(node.id, executionKey, BtDebugEventKind.Action, "FaceToTarget", BtStatus.Success, BtDebugReason.None, "RequestFaceToTarget()");
                    return BtStatus.Success;

                case BtTypeIds.Action.MoveToTarget:
                {
                    var st = ctx.MoveToTarget(out var failReason, out var detail);
                    AddEvent(node.id, executionKey, BtDebugEventKind.Action, "MoveToTarget", st, failReason, detail);
                    return st;
                }

                case BtTypeIds.Action.AttackBasic:
                    ctx.Driver.RequestAttackOnce();
                    AddEvent(node.id, executionKey, BtDebugEventKind.Action, "AttackBasic", BtStatus.Success, BtDebugReason.None, "RequestAttackOnce()");
                    return BtStatus.Success;

                case BtTypeIds.Action.RequestRestartRoot:
                {
                    string reason = ctx.GetStringParam(node, "reason", string.Empty);
                    ctx.Runtime.RequestRestartRoot(node.id, executionKey, reason);
                    AddEvent(node.id, executionKey, BtDebugEventKind.Action, "RequestRestartRoot", BtStatus.Success, BtDebugReason.RootRestartRequested, $"reason={reason}");
                    return BtStatus.Success;
                }
                
                case BtTypeIds.Action.UseSkillAndWait:
                {
                    if (ctx.SkillDriver is not IMonsterSkillDriverFeedback feedback)
                    {
                        AddEvent(node.id, executionKey, BtDebugEventKind.Action, "UseSkillAndWait", BtStatus.Failure, BtDebugReason.InvalidParameter, "Skill feedback driver missing");
                        return BtStatus.Failure;
                    }

                    int skillUid = ctx.GetIntParam(node, "skillUid", fallback: 0);
                    bool requireTarget = ctx.GetBoolParam(node, "requireTarget", fallback: true);
                    bool restartRoot = ctx.GetBoolParam(node, "restartRoot", fallback: false);

                    if (skillUid <= 0)
                    {
                        AddEvent(node.id, executionKey, BtDebugEventKind.Action, "UseSkillAndWait", BtStatus.Failure, BtDebugReason.SkillUidInvalid, $"skillUid={skillUid}");
                        return BtStatus.Failure;
                    }

                    var nodeState = ctx.Runtime.GetOrCreateNodeState(executionKey, node.id);

                    if (!nodeState.SkillStarted)
                    {
                        if (ctx.SkillDriver.IsSkillBusy)
                        {
                            AddEvent(node.id, executionKey, BtDebugEventKind.Action, "UseSkillAndWait", BtStatus.Running, BtDebugReason.SkillBusy, $"skillUid={skillUid}, waiting other skill");
                            return BtStatus.Running;
                        }

                        bool hasTarget = ctx.Driver.TryGetTarget(out var targetTr) && targetTr != null;
                        if (requireTarget && !hasTarget)
                        {
                            AddEvent(node.id, executionKey, BtDebugEventKind.Action, "UseSkillAndWait", BtStatus.Failure, BtDebugReason.NoTarget, $"skillUid={skillUid}, requireTarget=true");
                            return BtStatus.Failure;
                        }

                        Vector3 ground = hasTarget ? targetTr.position : ctx.Owner.transform.position;
                        Vector3 raw = hasTarget ? (targetTr.position - ctx.Owner.transform.position) : Vector3.right;
                        var forward = new Vector2(raw.x, raw.y);

                        var startResult = feedback.TryUseSkill(skillUid, new MonsterSkillTarget(targetTr, ground, forward));
                        if (!startResult.IsStarted)
                        {
                            AddEvent(node.id, executionKey, BtDebugEventKind.Action, "UseSkillAndWait", BtStatus.Failure, BtDebugReason.InvalidParameter, $"skillUid={skillUid}, result={startResult}");
                            return BtStatus.Failure;
                        }

                        nodeState.SkillStarted = true;
                        nodeState.RunningSkillUid = skillUid;
                        AddEvent(node.id, executionKey, BtDebugEventKind.Action, "UseSkillAndWait", BtStatus.Running, BtDebugReason.None, $"skillUid={skillUid}, Started");
                        return BtStatus.Running;
                    }

                    if (feedback.IsRunningSkill(nodeState.RunningSkillUid))
                    {
                        AddEvent(node.id, executionKey, BtDebugEventKind.Action, "UseSkillAndWait", BtStatus.Running, BtDebugReason.None, $"skillUid={nodeState.RunningSkillUid}, Running");
                        return BtStatus.Running;
                    }

                    if (feedback.ConsumeLastSkillResult(nodeState.RunningSkillUid, out var execResult))
                    {
                        bool success = execResult.State == MonsterSkillExecutionState.Succeeded;
                        int finishedSkillUid = nodeState.RunningSkillUid;
                        nodeState.SkillStarted = false;
                        nodeState.RunningSkillUid = 0;

                        if (success)
                        {
                            ctx.Blackboard?.IncrementSkillUseCount(finishedSkillUid);
                            AddEvent(node.id, executionKey, BtDebugEventKind.Action, "UseSkillAndWait", BtStatus.Success, BtDebugReason.None, $"skillUid={finishedSkillUid}, completed={execResult.State}, sequence={execResult.Sequence}, restartRoot={restartRoot}");
                            AddEvent(node.id, executionKey, BtDebugEventKind.Blackboard, "SkillUseCountIncrement", BtStatus.Success, BtDebugReason.None, $"skillUid={finishedSkillUid}, count={ctx.Blackboard?.GetSkillUseCount(finishedSkillUid) ?? 0}");

                            if (restartRoot)
                            {
                                string restartReason = $"UseSkillAndWait success skillUid={finishedSkillUid}, sequence={execResult.Sequence}";
                                ctx.Runtime.RequestRestartRoot(node.id, executionKey, restartReason);
                                AddEvent(node.id, executionKey, BtDebugEventKind.System, "RestartRootRequested", BtStatus.Success, BtDebugReason.RootRestartRequested, restartReason);
                            }

                            return BtStatus.Success;
                        }

                        AddEvent(node.id, executionKey, BtDebugEventKind.Action, "UseSkillAndWait", BtStatus.Failure, BtDebugReason.InvalidParameter, $"skillUid={finishedSkillUid}, completed={execResult.State}, sequence={execResult.Sequence}");
                        return BtStatus.Failure;
                    }

                    AddEvent(node.id, executionKey, BtDebugEventKind.Action, "UseSkillAndWait", BtStatus.Running, BtDebugReason.None, $"skillUid={nodeState.RunningSkillUid}, awaiting completion result");
                    return BtStatus.Running;
                }

                case BtTypeIds.Action.UseSkill:
                {
                    if (ctx.SkillDriver == null)
                    {
                        AddEvent(node.id, executionKey, BtDebugEventKind.Action, "UseSkill", BtStatus.Failure, BtDebugReason.InvalidParameter, "SkillDriver missing");
                        return BtStatus.Failure;
                    }

                    int skillUid = ctx.GetIntParam(node, "skillUid", fallback: 0);
                    if (skillUid <= 0)
                    {
                        AddEvent(node.id, executionKey, BtDebugEventKind.Action, "UseSkill", BtStatus.Failure, BtDebugReason.SkillUidInvalid, $"skillUid={skillUid}");
                        return BtStatus.Failure;
                    }

                    bool requireTarget = ctx.GetBoolParam(node, "requireTarget", fallback: true);
                    string busyReturn = ctx.GetEnumStringParam(node, "busyReturn", fallback: "Running"); // Running / Success

                    if (ctx.SkillDriver.IsSkillBusy)
                    {
                        var busyStatus = (busyReturn == "Success") ? BtStatus.Success : BtStatus.Running;
                        AddEvent(node.id, executionKey, BtDebugEventKind.Action, "UseSkill", busyStatus, BtDebugReason.SkillBusy, $"skillUid={skillUid}, busyReturn={busyReturn}");
                        return busyStatus;
                    }

                    bool hasTarget = ctx.Driver.TryGetTarget(out var targetTr) && targetTr != null;
                    if (requireTarget && !hasTarget)
                    {
                        AddEvent(node.id, executionKey, BtDebugEventKind.Action, "UseSkill", BtStatus.Failure, BtDebugReason.NoTarget, $"skillUid={skillUid}, requireTarget=true");
                        return BtStatus.Failure;
                    }

                    Vector3 ground = hasTarget ? targetTr.position : ctx.Owner.transform.position;
                    Vector3 raw = hasTarget ? (targetTr.position - ctx.Owner.transform.position) : Vector3.right;
                    var forward = new Vector2(raw.x, raw.y);

                    var st = ctx.SkillDriver.TryUseSkill(skillUid, new MonsterSkillTarget(targetTr, ground, forward));
                    if (st.IsStarted)
                    {
                        // 성공(Started) 시에만 1회 증가
                        ctx.Blackboard?.IncrementSkillUseCount(skillUid);
                        AddEvent(node.id, executionKey, BtDebugEventKind.Action, "UseSkill", BtStatus.Running, BtDebugReason.None, $"skillUid={skillUid}, Started");
                        AddEvent(node.id, executionKey, BtDebugEventKind.Blackboard, "SkillUseCountIncrement", BtStatus.Success, BtDebugReason.None, $"skillUid={skillUid}, count={ctx.Blackboard?.GetSkillUseCount(skillUid) ?? 0}");
                        return BtStatus.Running;
                    }
                    AddEvent(node.id, executionKey, BtDebugEventKind.Action, "UseSkill", BtStatus.Failure, BtDebugReason.InvalidParameter, $"skillUid={skillUid}, result={st}");
                    return BtStatus.Failure;
                }

                case BtTypeIds.Action.ClearAggro:
                    ctx.Driver.RequestClearAggro();
                    AddEvent(node.id, executionKey, BtDebugEventKind.Action, "ClearAggro", BtStatus.Success, BtDebugReason.None, "RequestClearAggro()");
                    return BtStatus.Success;
                case BtTypeIds.Action.ResetSkillUseCount:
                {
                    if (ctx.Blackboard == null)
                    {
                        AddEvent(node.id, executionKey, BtDebugEventKind.Action, "ResetSkillUseCount", BtStatus.Failure, BtDebugReason.BlackboardMissing, "Blackboard missing");
                        return BtStatus.Failure;
                    }

                    string mode = ctx.GetEnumStringParam(node, "mode", fallback: "AllReset");
                    int skillUid = ctx.GetIntParam(node, "skillUid", fallback: 0);
                    int value = ctx.GetIntParam(node, "value", fallback: 0);

                    switch (mode)
                    {
                        case "AllReset":
                            ctx.Blackboard.ClearAllSkillUseCounts();
                            AddMetric(node.id, executionKey, "Mode", 0f, "AllReset");
                            AddEvent(node.id, executionKey, BtDebugEventKind.Blackboard, "ResetSkillUseCount", BtStatus.Success, BtDebugReason.None, "AllReset");
                            return BtStatus.Success;

                        case "ResetOne":
                            if (skillUid <= 0)
                                return BtStatus.Failure;

                            ctx.Blackboard.ResetSkillUseCount(skillUid);
                            AddMetric(node.id, executionKey, "Mode", 1f, "ResetOne");
                            AddMetric(node.id, executionKey, "SkillUid", skillUid);
                            AddEvent(node.id, executionKey, BtDebugEventKind.Blackboard, "ResetSkillUseCount", BtStatus.Success, BtDebugReason.None, $"ResetOne skillUid={skillUid}");
                            return BtStatus.Success;

                        case "SetOne":
                            if (skillUid <= 0)
                                return BtStatus.Failure;

                            ctx.Blackboard.SetSkillUseCount(skillUid, Mathf.Max(0, value));
                            AddMetric(node.id, executionKey, "Mode", 2f, "SetOne");
                            AddMetric(node.id, executionKey, "SkillUid", skillUid);
                            AddMetric(node.id, executionKey, "Value", value);
                            AddEvent(node.id, executionKey, BtDebugEventKind.Blackboard, "ResetSkillUseCount", BtStatus.Success, BtDebugReason.None, $"SetOne skillUid={skillUid}, value={Mathf.Max(0, value)}");
                            return BtStatus.Success;

                        default:
                            return BtStatus.Failure;
                    }
                }
                default:
                    return BtStatus.Failure;
            }
        }
        #endregion

        public bool TryGetDebugExecutionEvents(string nodeId, string executionKey, out List<BtDebugEvent> events)
        {
            events = new List<BtDebugEvent>();
            var frame = DebugLastFrame;
            if (frame == null || string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(executionKey))
                return false;

            for (int i = 0; i < frame.Events.Count; i++)
            {
                var ev = frame.Events[i];
                if (ev.NodeId == nodeId && ev.ExecutionKey == executionKey)
                    events.Add(ev);
            }

            return events.Count > 0;
        }

        public bool TryGetDebugExecutionMetrics(string nodeId, string executionKey, out List<BtDebugMetric> metrics)
        {
            metrics = new List<BtDebugMetric>();
            var frame = DebugLastFrame;
            if (frame == null || string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(executionKey))
                return false;

            for (int i = 0; i < frame.Metrics.Count; i++)
            {
                var metric = frame.Metrics[i];
                if (metric.NodeId == nodeId && metric.ExecutionKey == executionKey)
                    metrics.Add(metric);
            }

            return metrics.Count > 0;
        }

        /// <summary>
        /// BT 평가 컨텍스트.
        /// </summary>
        private readonly struct BtContext
        {
            public readonly MonoBehaviour Owner;
            public readonly IMonsterCombatDriver Driver;
            public readonly RuntimeBlackboard Blackboard;
            public readonly BtRuntimeState Runtime;
            public readonly bool DebugLog;
            public readonly IMonsterSkillDriver SkillDriver;
            
            public BtContext(MonoBehaviour owner, IMonsterCombatDriver driver, IMonsterSkillDriver skillDriver,
                RuntimeBlackboard blackboard, BtRuntimeState runtime, bool debugLog)
            {
                Owner = owner;
                Driver = driver;
                SkillDriver = skillDriver;
                Blackboard = blackboard;
                Runtime = runtime;
                DebugLog = debugLog;
            }

            public bool HasAggroTarget()
            {
                if (Driver.IsDead) return false;
                if (!Driver.IsAggro) return false;
                return Driver.TryGetTarget(out _);
            }

            public bool IsTargetWithinDistance(float max)
            {
                if (!Driver.TryGetTarget(out var target) || target == null) return false;
                var a = Owner.transform.position;
                var b = target.position;
                return (b - a).sqrMagnitude <= max * max;
            }

            public float GetTargetDistance(out bool hasTarget)
            {
                if (!Driver.TryGetTarget(out var target) || target == null)
                {
                    hasTarget = false;
                    return -1f;
                }
                hasTarget = true;
                return Vector3.Distance(Owner.transform.position, target.position);
            }

            /// <summary>
            /// 타겟 방향 이동을 요청하고, 실패 시 원인을 디버그 코드로 함께 반환한다.
            /// </summary>
            /// <param name="failureReason">액션 실패/관측 사유 코드.</param>
            /// <param name="detail">디버그 상세 메시지.</param>
            /// <returns>이동 요청 처리 결과 상태.</returns>
            public BtStatus MoveToTarget(out BtDebugReason failureReason, out string detail)
            {
                failureReason = BtDebugReason.None;
                detail = "Move request pending";

                if (!Driver.TryGetTarget(out var target) || target == null)
                {
                    failureReason = BtDebugReason.NoTarget;
                    detail = "target missing";
                    return BtStatus.Failure;
                }

                Vector3 raw = (target.position - Owner.transform.position);
                Vector2 dir = new Vector2(raw.x, raw.y);
                if (dir.sqrMagnitude <= 0.000001f)
                {
                    detail = "already at target";
                    return BtStatus.Success;
                }

                if (Driver.TryRequestMove(dir, out var moveFailure))
                {
                    detail = $"status=Running, dir=({dir.x:0.###},{dir.y:0.###}), target={target.name}";
                    return BtStatus.Running;
                }

                failureReason = ConvertMoveFailureToDebugReason(moveFailure);
                detail = $"move rejected. reason={moveFailure}, dir=({dir.x:0.###},{dir.y:0.###}), target={target.name}";
                return BtStatus.Failure;
            }

            /// <summary>
            /// Core 이동 거부 코드를 BT 디버그 코드로 변환한다.
            /// </summary>
            /// <param name="reason">Core 이동 거부 코드.</param>
            /// <returns>BT 디버그 이벤트에 기록할 사유 코드.</returns>
            private static BtDebugReason ConvertMoveFailureToDebugReason(MonsterMoveRequestFailureReason reason)
            {
                return reason switch
                {
                    MonsterMoveRequestFailureReason.None => BtDebugReason.None,
                    MonsterMoveRequestFailureReason.ZeroDirection => BtDebugReason.MoveBlockedByDirection,
                    MonsterMoveRequestFailureReason.AxisLocked => BtDebugReason.MoveBlockedByDirection,
                    MonsterMoveRequestFailureReason.StatusDontMove => BtDebugReason.MoveBlockedByStatus,
                    MonsterMoveRequestFailureReason.StatusAttack => BtDebugReason.MoveBlockedByStatus,
                    MonsterMoveRequestFailureReason.StatusDead => BtDebugReason.MoveBlockedByStatus,
                    MonsterMoveRequestFailureReason.SpeedNonPositive => BtDebugReason.MoveBlockedBySpeed,
                    MonsterMoveRequestFailureReason.CharacterMissing => BtDebugReason.MoveRequestRejected,
                    MonsterMoveRequestFailureReason.Unknown => BtDebugReason.MoveRequestRejected,
                    _ => BtDebugReason.MoveRequestRejected,
                };
            }

            public BtStatus WaitForSeconds(string executionKey, float sec)
            {
                if (sec <= 0f) return BtStatus.Success;

                if (!Runtime.Timeouts.TryGetValue(executionKey, out float start))
                {
                    Runtime.Timeouts[executionKey] = Time.time;
                    return BtStatus.Running;
                }

                if (Time.time - start >= sec)
                {
                    Runtime.Timeouts.Remove(executionKey);
                    return BtStatus.Success;
                }

                return BtStatus.Running;
            }

            public bool IsTimeoutExceeded(string executionKey, float sec)
            {
                if (sec <= 0f) return false;
                if (!Runtime.Timeouts.TryGetValue(executionKey, out float start))
                {
                    Runtime.Timeouts[executionKey] = Time.time;
                    return false;
                }
                return Time.time - start >= sec;
            }

            public void ResetTimeout(string executionKey)
            {
                Runtime.Timeouts.Remove(executionKey);
            }

            public bool IsCooldownReady(string key)
            {
                return !Runtime.Cooldowns.TryGetValue(key, out float readyAt) || Time.time >= readyAt;
            }

            public void ConsumeCooldown(string key, float sec)
            {
                if (sec <= 0f) return;
                Runtime.Cooldowns[key] = Time.time + sec;
            }

            public float GetFloatParam(BtNodeRecord node, string key, float fallback)
            {
                return BtParamValue.TryGetFloat(node.parameters, key, out float v) ? v : fallback;
            }

            public int GetIntParam(BtNodeRecord node, string key, int fallback)
            {
                return BtParamValue.TryGetInt(node.parameters, key, out int v) ? v : fallback;
            }

            public string GetStringParam(BtNodeRecord node, string key, string fallback)
            {
                return BtParamValue.TryGetString(node.parameters, key, out string v) ? v : fallback;
            }

            public int RandomPickWeighted(IReadOnlyList<string> children, List<BtParamValue> parameters)
            {
                if (children == null || children.Count == 0) return -1;

                // parameters: weight_0, weight_1, ... (없으면 1)
                float total = 0f;
                for (int i = 0; i < children.Count; i++)
                {
                    float w = 1f;
                    if (BtParamValue.TryGetFloat(parameters, $"weight_{i}", out float pv))
                        w = Mathf.Max(0f, pv);
                    total += w;
                }
                if (total <= 0f) return 0;

                float r = UnityEngine.Random.value * total;
                for (int i = 0; i < children.Count; i++)
                {
                    float w = 1f;
                    if (BtParamValue.TryGetFloat(parameters, $"weight_{i}", out float pv))
                        w = Mathf.Max(0f, pv);
                    r -= w;
                    if (r <= 0f) return i;
                }
                return children.Count - 1;
            }
            public bool GetBoolParam(BtNodeRecord node, string key, bool fallback)
            {
                return BtParamValue.TryGetBool(node.parameters, key, out bool v) ? v : fallback;
            }

            public string GetEnumStringParam(BtNodeRecord node, string key, string fallback)
            {
                return BtParamValue.TryGetEnumString(node.parameters, key, out string v) && !string.IsNullOrEmpty(v) ? v : fallback;
            }
        }
        public void OnCharacterTriggerEnter(Collider2D collision)
        {
        }

        public void OnCharacterTriggerExit(Collider2D collision)
        {
        }
    }
}
