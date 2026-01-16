using System;
using System.Collections.Generic;
using UnityEngine;
using GGemCo2DCore;

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
    public sealed class MonsterBtRunner : MonoBehaviour
    {
        [Header("Behavior Tree")]
        [SerializeField] private MonsterBehaviorTreeAsset treeAsset;

        [Header("Tick")]
        [SerializeField, Tooltip("0 이면 Update 프레임마다 평가한다. 0보다 크면 해당 Hz로 평가한다.")]
        private float tickRateHz = 10f;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLog;
        [SerializeField, Tooltip("디자이너/디버그 창에서 실행 노드 하이라이트를 위해 트레이스를 수집한다.")]
        private bool enableDebugTrace = true;
        [SerializeField, Min(16), Tooltip("디버그 트레이스의 최대 방문 노드 기록 개수(한 틱 기준).")]
        private int debugTraceCapacity = 256;
        [SerializeField, Min(16), Tooltip("디버그 메트릭의 최대 기록 개수(한 틱 기준).")]
        private int debugMetricCapacity = 256;

        public string DebugActiveNodeId { get; private set; }
        public IReadOnlyList<string> DebugActivePath => _debugActivePath;
        public IReadOnlyList<BtDebugNodeResult> DebugLastTick => _debugLastTick;
        public IReadOnlyList<BtDebugMetric> DebugLastMetrics => _debugMetrics;
        public event Action<MonsterBtRunner> DebugTicked;

        private readonly List<string> _debugActivePath = new();
        private readonly List<BtDebugNodeResult> _debugLastTick = new();
        private readonly List<BtDebugMetric> _debugMetrics = new();
        private readonly List<string> _execStack = new();

        private readonly Dictionary<string, BtNodeRecord> _nodeById = new(StringComparer.Ordinal);
        private BtRuntimeState _runtime;
        private RuntimeBlackboard _blackboard;
        private float _nextTickTime;

        private IMonsterCombatDriver _driver;

        public bool IsActive => enabled && isActiveAndEnabled && treeAsset != null && !string.IsNullOrEmpty(treeAsset.rootNodeId);

        public void SetTree(MonsterBehaviorTreeAsset asset)
        {
            treeAsset = asset;
            RebuildCache();
        }

        private void Awake()
        {
            RebuildCache();
        }

        private void OnEnable()
        {
            _nextTickTime = 0f;
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
            _blackboard = new RuntimeBlackboard(treeAsset != null ? treeAsset.blackboardSchema : null);

            if (treeAsset == null || treeAsset.nodes == null) return;
            foreach (var node in treeAsset.nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.id)) continue;
                _nodeById[node.id] = node;
            }
        }

        private void Update()
        {
            if (!IsActive) return;

            // Monster가 런타임에 ControllerMonster를 AddComponent 하는 구조이므로,
            // 최초 몇 프레임은 드라이버가 아직 없을 수 있다. 매 틱 느슨하게 획득한다.
            if (_driver == null)
                _driver = GetComponent<IMonsterCombatDriver>();

            if (_driver == null) return;

            float now = Time.time;
            if (tickRateHz > 0f)
            {
                if (now < _nextTickTime) return;
                _nextTickTime = now + (1f / tickRateHz);
            }

            _runtime.tickIndex++;
            _runtime.lastTickTime = now;

            if (enableDebugTrace)
            {
                _debugActivePath.Clear();
                _debugLastTick.Clear();
                _debugMetrics.Clear();
                _execStack.Clear();
                DebugActiveNodeId = null;
            }

            if (!_nodeById.ContainsKey(treeAsset.rootNodeId))
            {
                if (enableDebugLog) Debug.LogWarning($"[BT] Root node not found. root={treeAsset.rootNodeId}", this);
                return;
            }

            var ctx = new BtContext(this, _driver, _blackboard, _runtime, enableDebugLog);
            ExecuteNode(treeAsset.rootNodeId, ctx, depth: 0);

            if (enableDebugTrace)
            {
                DebugActiveNodeId = _debugActivePath.Count > 0 ? _debugActivePath[_debugActivePath.Count - 1] : null;
                DebugTicked?.Invoke(this);
            }
        }

        private BtStatus ExecuteNode(string nodeId, BtContext ctx, int depth)
        {
            if (depth > 64) return BtStatus.Failure; // 순환/과도한 깊이 방어
            if (!_nodeById.TryGetValue(nodeId, out var node) || node == null) return BtStatus.Failure;

            if (enableDebugTrace)
                _execStack.Add(nodeId);

            BtStatus status = node.kind switch
            {
                BtNodeKind.Composite => ExecuteComposite(node, ctx, depth),
                BtNodeKind.Decorator => ExecuteDecorator(node, ctx, depth),
                BtNodeKind.Condition => ExecuteCondition(node, ctx),
                BtNodeKind.Action => ExecuteAction(node, ctx),
                _ => BtStatus.Failure,
            };

            if (enableDebugTrace)
            {
                if (_debugLastTick.Count < debugTraceCapacity)
                    _debugLastTick.Add(new BtDebugNodeResult(nodeId, status, depth));

                // 가장 깊은 Running 경로를 최초 1회만 캡처한다.
                if (status == BtStatus.Running && _debugActivePath.Count == 0)
                    _debugActivePath.AddRange(_execStack);

                // pop
                if (_execStack.Count > 0)
                    _execStack.RemoveAt(_execStack.Count - 1);
            }

            return status;
        }

        
        private void AddMetric(string nodeId, string key, float value, string text = null)
        {
            if (!enableDebugTrace) return;
            if (_debugMetrics.Count >= debugMetricCapacity) return;
            _debugMetrics.Add(new BtDebugMetric(nodeId, key, value, text));
        }

        #region Composite
        private BtStatus ExecuteComposite(BtNodeRecord node, BtContext ctx, int depth)
        {
            if (node.children == null || node.children.Count == 0) return BtStatus.Failure;

            switch (node.typeId)
            {
                case BtTypeIds.Composite.Selector:
                    for (int i = 0; i < node.children.Count; i++)
                    {
                        var st = ExecuteNode(node.children[i], ctx, depth + 1);
                        if (st != BtStatus.Failure) return st;
                    }
                    return BtStatus.Failure;

                case BtTypeIds.Composite.Sequence:
                    for (int i = 0; i < node.children.Count; i++)
                    {
                        var st = ExecuteNode(node.children[i], ctx, depth + 1);
                        if (st != BtStatus.Success) return st;
                    }
                    return BtStatus.Success;

                case BtTypeIds.Composite.RandomWeighted:
                {
                    // MVP: 매 틱 랜덤 선택(상태 유지 X). 필요 시 NodeState에 선택 인덱스를 저장해 안정화 가능.
                    int pick = ctx.RandomPickWeighted(node.children, node.parameters);
                    if (pick < 0 || pick >= node.children.Count) return BtStatus.Failure;
                    return ExecuteNode(node.children[pick], ctx, depth + 1);
                }

                default:
                    return BtStatus.Failure;
            }
        }
        #endregion

        #region Decorator
        private BtStatus ExecuteDecorator(BtNodeRecord node, BtContext ctx, int depth)
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
                        return ExecuteNode(childId, ctx, depth + 1);

                    if (!ctx.IsCooldownReady(key))
                        return BtStatus.Failure;

                    var st = ExecuteNode(childId, ctx, depth + 1);
                    if (st != BtStatus.Failure)
                        ctx.ConsumeCooldown(key, seconds);
                    return st;
                }

                case BtTypeIds.Decorator.Timeout:
                {
                    float seconds = ctx.GetFloatParam(node, "sec", fallback: 0f);
                    if (seconds <= 0f) return ExecuteNode(childId, ctx, depth + 1);

                    if (ctx.IsTimeoutExceeded(node.id, seconds))
                        return BtStatus.Failure;

                    var st = ExecuteNode(childId, ctx, depth + 1);
                    if (st == BtStatus.Success || st == BtStatus.Failure)
                        ctx.ResetTimeout(node.id);
                    return st;
                }

                default:
                    return BtStatus.Failure;
            }
        }
        #endregion

        #region Condition
        private BtStatus ExecuteCondition(BtNodeRecord node, BtContext ctx)
        {
            bool ok;

            switch (node.typeId)
            {
                case BtTypeIds.Condition.HasAggroTarget:
                {
                    ok = ctx.HasAggroTarget();
                    AddMetric(node.id, "HasAggroTarget", ok ? 1f : 0f);
                    break;
                }

                case BtTypeIds.Condition.InAttackRange:
                {
                    ok = ctx.Driver.IsTargetInAttackRange();
                    AddMetric(node.id, "InAttackRange", ok ? 1f : 0f);
                    break;
                }

                case BtTypeIds.Condition.HpPercentBelow:
                {
                    float threshold = Mathf.Clamp01(ctx.GetFloatParam(node, "threshold", 0.25f));
                    float hp = Mathf.Clamp01(ctx.Driver.HpPercent);
                    ok = hp < threshold;
                    AddMetric(node.id, "HpPercent", hp);
                    AddMetric(node.id, "Threshold", threshold);
                    AddMetric(node.id, "Result", ok ? 1f : 0f);
                    break;
                }

                case BtTypeIds.Condition.TargetWithinDistance:
                {
                    float max = Mathf.Max(0f, ctx.GetFloatParam(node, "max", 12f));
                    float dist = ctx.GetTargetDistance(out bool hasTarget);
                    ok = hasTarget && dist <= max;
                    AddMetric(node.id, "Distance", hasTarget ? dist : -1f, hasTarget ? null : "No target");
                    AddMetric(node.id, "Max", max);
                    AddMetric(node.id, "Result", ok ? 1f : 0f);
                    break;
                }

                default:
                    ok = false;
                    AddMetric(node.id, "UnknownCondition", 0f, node.typeId);
                    break;
            }

            return ok ? BtStatus.Success : BtStatus.Failure;
        }
        #endregion

        #region Action
        private BtStatus ExecuteAction(BtNodeRecord node, BtContext ctx)
        {
            switch (node.typeId)
            {
                case BtTypeIds.Action.Wait:
                {
                    float sec = ctx.GetFloatParam(node, "sec", 0.2f);
                    return ctx.WaitForSeconds(node.id, sec);
                }

                case BtTypeIds.Action.WaitOneTick:
                    return BtStatus.Running;

                case BtTypeIds.Action.Stop:
                    ctx.Driver.RequestWait();
                    return BtStatus.Success;

                case BtTypeIds.Action.FaceToTarget:
                    ctx.Driver.RequestFaceToTarget();
                    return BtStatus.Success;

                case BtTypeIds.Action.MoveToTarget:
                    return ctx.MoveToTarget();

                case BtTypeIds.Action.AttackBasic:
                    ctx.Driver.RequestAttackOnce();
                    return BtStatus.Success;

                default:
                    return BtStatus.Failure;
            }
        }
        #endregion

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

            public BtContext(MonoBehaviour owner, IMonsterCombatDriver driver, RuntimeBlackboard blackboard, BtRuntimeState runtime, bool debugLog)
            {
                Owner = owner;
                Driver = driver;
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

            public BtStatus MoveToTarget()
            {
                if (!Driver.TryGetTarget(out var target) || target == null) return BtStatus.Failure;
                Vector3 raw = (target.position - Owner.transform.position);
                Vector2 dir = new Vector2(raw.x, raw.y);
                if (dir.sqrMagnitude <= 0.000001f) return BtStatus.Success;
                Driver.RequestMove(dir);
                return BtStatus.Running;
            }

            public BtStatus WaitForSeconds(string nodeId, float sec)
            {
                if (sec <= 0f) return BtStatus.Success;

                if (!Runtime.timeouts.TryGetValue(nodeId, out float start))
                {
                    Runtime.timeouts[nodeId] = Time.time;
                    return BtStatus.Running;
                }

                if (Time.time - start >= sec)
                {
                    Runtime.timeouts.Remove(nodeId);
                    return BtStatus.Success;
                }

                return BtStatus.Running;
            }

            public bool IsTimeoutExceeded(string nodeId, float sec)
            {
                if (sec <= 0f) return false;
                if (!Runtime.timeouts.TryGetValue(nodeId, out float start))
                {
                    Runtime.timeouts[nodeId] = Time.time;
                    return false;
                }
                return Time.time - start >= sec;
            }

            public void ResetTimeout(string nodeId)
            {
                Runtime.timeouts.Remove(nodeId);
            }

            public bool IsCooldownReady(string key)
            {
                return !Runtime.cooldowns.TryGetValue(key, out float readyAt) || Time.time >= readyAt;
            }

            public void ConsumeCooldown(string key, float sec)
            {
                if (sec <= 0f) return;
                Runtime.cooldowns[key] = Time.time + sec;
            }

            public float GetFloatParam(BtNodeRecord node, string key, float fallback)
            {
                return BtParamValue.TryGetFloat(node.parameters, key, out float v) ? v : fallback;
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
        }
    }
}
