# AI_BT ReferenceSnippets

작성일: 2026-02-18

목적:
- AI BT 패키지에서 Runner/Blackboard/데이터 주도 트리 구조를 자동 복제

우선순위:
1) `Docs/ReferenceSnippets.md`
2) `Docs/STYLE_CONTRACT.md`
3) `Docs/GOLDEN_REFERENCES.md`
4) `Docs/GGemCoPatterns/*`
5) AI_BT `CONVENTIONS/ARCHITECTURE/PLAYBOOK`

---

## MonsterBtRunner(런타임 Tick/실행기)

- 경로: `BT/MonsterBtRunner.cs`
- 포인트:
  - Tick 루프의 성능/할당 주의
  - Suspend/Resume 훅(테스트 모드 격리) 확장 포인트

```csharp
    /// - Core 패키지는 본 BT 패키지를 참조하지 않는다(의존성 단방향).
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MonsterBtRunner : MonoBehaviour, IMonsterBrainTickable
    {
        [Header("Behavior Tree")]
        [SerializeField] private MonsterBehaviorTreeAsset treeAsset;

        [Header("Tick")]
        [SerializeField, Tooltip("0 이면 Update 프레임마다 평가한다. 0보다 크면 해당 Hz로 평가한다.")]
        private float tickRateHz = 0f;

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
        private IMonsterSkillDriver _skillDriver;
        private IMonsterBrainSuspendProvider _suspendProvider;

        private bool _isExecuting;
        private bool _hasPendingTreeChange;
        private MonsterBehaviorTreeAsset _pendingTreeAsset;
        private BtTreeSwitchMode _pendingSwitchMode = BtTreeSwitchMode.ResetAll;

        public int Priority => 100;

        public bool IsActive => enabled && isActiveAndEnabled && treeAsset != null &&
                                !string.IsNullOrEmpty(treeAsset.rootNodeId);

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
            if (asset == treeAsset && !_hasPendingTreeChange)
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

        private void ApplyTreeChange(MonsterBehaviorTreeAsset newAsset, BtTreeSwitchMode mode)
        {
            var prev = treeAsset;
            var prevBlackboard = _blackboard;

            treeAsset = newAsset;

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

            if (_driver == null) return;

            // 그로기/기절/컷씬 등으로 Brain 평가를 중지해야 하는 경우, 이번 틱은 스킵한다.
            if (_suspendProvider != null && _suspendProvider.ShouldSuspendBrain) return;

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

            var ctx = new BtContext(this, _driver, _skillDriver, _blackboard, _runtime, enableDebugLog);
            try
            {
                _isExecuting = true;
                ExecuteNode(treeAsset.rootNodeId, ctx, depth: 0);
            }
            finally
            {
                _isExecuting = false;
            }

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
```
## RuntimeBlackboard(런타임 상태 저장소)

- 경로: `BT/RuntimeBlackboard.cs`
- 포인트:
  - 키 중앙화(Schema) + 타입 안정성
  - Tick 중 할당 최소화

```csharp
    /// 런타임 블랙보드. Schema 기반으로 초기화되며,
    /// 키 조회를 위해 내부 인덱스 캐시를 구성한다.
    /// </summary>
    public sealed class RuntimeBlackboard
    {
        private readonly BlackboardSchema _schema;
        private readonly List<BlackboardEntry> _entries;
        private readonly Dictionary<string, int> _indexByName;

        // 동적 런타임 캐시(스키마에 정의되지 않는 값)
        private readonly Dictionary<int, int> _skillUseCounts = new Dictionary<int, int>();

        private struct BlackboardEntry
        {
            public BtValueType Type;
            public bool Bool;
            public int Int;
            public float Float;
            public string String;
            public Vector2 Vector2;
            public Vector3 Vector3;
        }

        public RuntimeBlackboard(BlackboardSchema schema)
        {
            _schema = schema;
            _entries = new List<BlackboardEntry>(schema?.keys?.Count ?? 0);
            _indexByName = new Dictionary<string, int>(StringComparer.Ordinal);

            if (schema?.keys == null) return;

            for (int i = 0; i < schema.keys.Count; i++)
            {
                var def = schema.keys[i];
                if (def == null || string.IsNullOrEmpty(def.name))
                    continue;

                _indexByName[def.name] = _entries.Count;
                _entries.Add(new BlackboardEntry
                {
                    Type = def.type,
                    Bool = def.defaultBool,
                    Int = def.defaultInt,
                    Float = def.defaultFloat,
                    String = def.defaultString,
                    Vector2 = def.defaultVector2,
                    Vector3 = def.defaultVector3,
                });
            }
        }

        public bool HasKey(string name) => !string.IsNullOrEmpty(name) && _indexByName.ContainsKey(name);

        public BtValueType GetKeyType(string name)
        {
            if (!_indexByName.TryGetValue(name, out int idx)) return BtValueType.String;
            return _entries[idx].Type;
        }

        public bool TryGetFloat(string name, out float value)
        {
            value = 0f;
            if (!_indexByName.TryGetValue(name, out int idx)) return false;
            if (_entries[idx].Type != BtValueType.Float) return false;
            value = _entries[idx].Float;
            return true;
        }

        public bool TryGetInt(string name, out int value)
        {
            value = 0;
            if (!_indexByName.TryGetValue(name, out int idx)) return false;
            if (_entries[idx].Type != BtValueType.Int) return false;
            value = _entries[idx].Int;
            return true;
        }

        public bool TryGetBool(string name, out bool value)
        {
            value = false;
            if (!_indexByName.TryGetValue(name, out int idx)) return false;
            if (_entries[idx].Type != BtValueType.Bool) return false;
            value = _entries[idx].Bool;
            return true;
        }

        public bool TrySetFloat(string name, float value)
        {
            if (!_indexByName.TryGetValue(name, out int idx)) return false;
            if (_entries[idx].Type != BtValueType.Float) return false;
            var e = _entries[idx];
            e.Float = value;
            _entries[idx] = e;
            return true;
        }

        public bool TrySetInt(string name, int value)
        {
            if (!_indexByName.TryGetValue(name, out int idx)) return false;
            if (_entries[idx].Type != BtValueType.Int) return false;
            var e = _entries[idx];
            e.Int = value;
            _entries[idx] = e;
            return true;
        }

        public bool TrySetBool(string name, bool value)
        {
            if (!_indexByName.TryGetValue(name, out int idx)) return false;
            if (_entries[idx].Type != BtValueType.Bool) return false;
            var e = _entries[idx];
            e.Bool = value;
            _entries[idx] = e;
            return true;
        }

        /// <summary>
        /// 특정 스킬 UID의 사용 횟수를 조회한다. (없으면 0)
        /// </summary>
        public int GetSkillUseCount(int skillUid)
        {
            if (skillUid <= 0) return 0;
            return _skillUseCounts.TryGetValue(skillUid, out int v) ? v : 0;
        }

        /// <summary>
        /// 특정 스킬 UID의 사용 횟수를 증가시킨다.
        /// </summary>
        public void IncrementSkillUseCount(int skillUid, int delta = 1)
        {
            if (skillUid <= 0) return;
            if (delta == 0) return;

            _skillUseCounts.TryGetValue(skillUid, out int cur);
            _skillUseCounts[skillUid] = cur + delta;
        }

        /// <summary>
        /// 특정 스킬 UID의 사용 횟수를 0으로 리셋한다.
        /// </summary>
        public void ResetSkillUseCount(int skillUid)
        {
            if (skillUid <= 0) return;
            _skillUseCounts.Remove(skillUid);
        }

        /// <summary>
        /// 모든 스킬 사용 횟수 캐시를 초기화한다.
        /// </summary>
        public void ClearAllSkillUseCounts()
        {
            _skillUseCounts.Clear();
        }

        /// <summary>
        /// 다른 블랙보드에서 공통 키의 값을 복사한다.
        /// </summary>
        /// <remarks>
        /// - 키 이름이 동일하고, 타입이 동일한 항목만 복사한다.
        /// - 스키마에 정의되지 않은 동적 값은 복사 대상이 아니다.
        /// </remarks>
        public void CopyCommonValuesFrom(RuntimeBlackboard source, bool includeSkillUseCounts)
        {
            if (source == null) return;

            // 스키마 기반 키 복사
            foreach (var kv in _indexByName)
            {
                var key = kv.Key;
                int dstIndex = kv.Value;

                if (!source._indexByName.TryGetValue(key, out int srcIndex))
                    continue;

                var srcEntry = source._entries[srcIndex];
                var dstEntry = _entries[dstIndex];

                if (srcEntry.Type != dstEntry.Type)
                    continue;

                _entries[dstIndex] = srcEntry;
            }

            if (includeSkillUseCounts)
            {
                _skillUseCounts.Clear();
                foreach (var kv in source._skillUseCounts)
                    _skillUseCounts[kv.Key] = kv.Value;
            }
        }
    }
}
```
## BlackboardSchema(키 정의 중앙화)

- 경로: `BT/BlackboardSchema.cs`
- 포인트:
  - 키는 상수/enum으로 중앙화
  - 누락/기본값 정책 참고

```csharp
{
    /// <summary>
    /// 블랙보드 키 정의(스키마).
    /// </summary>
    [Serializable]
    public sealed class BlackboardKeyDef
    {
        [SerializeField] public string name;
        [SerializeField] public BtValueType type;
        [SerializeField] public string description;
        [SerializeField] public bool isReadOnly;

        [SerializeField] public bool defaultBool;
        [SerializeField] public int defaultInt;
        [SerializeField] public float defaultFloat;
        [SerializeField] public string defaultString;
        [SerializeField] public Vector2 defaultVector2;
        [SerializeField] public Vector3 defaultVector3;
    }

    /// <summary>
    /// 블랙보드 스키마.
    /// </summary>
    [Serializable]
    public sealed class BlackboardSchema
    {
        [SerializeField] public List<BlackboardKeyDef> keys = new();
    }
}
```
## MonsterBehaviorTreeAsset(트리 데이터/에셋)

- 경로: `BT/MonsterBehaviorTreeAsset.cs`
- 포인트:
  - 트리 구성 데이터의 표준 형태
  - 노드 레코드/파라미터 구조 참고

```csharp
    /// - 런타임에서는 BtRunner가 이 에셋을 읽어 평가한다.
    /// </summary>
    [CreateAssetMenu(menuName = "GGemCo/AI/Monster Behavior Tree", fileName = "MonsterBehaviorTree")]
    public sealed class MonsterBehaviorTreeAsset : ScriptableObject
    {
        [SerializeField] public string schemaVersion = "1.0.0";
        [SerializeField] public string treeGuid = Guid.NewGuid().ToString("N");

        [SerializeField] public string rootNodeId;
        [SerializeField] public List<BtNodeRecord> nodes = new();
        [SerializeField] public BlackboardSchema blackboardSchema = new();

        /// <summary>
        /// 빠른 조회를 위한 노드 인덱스 캐시.
        /// 에셋 수정 시 다시 빌드해야 하므로 런타임에서는 Runner에서 로컬 캐시를 구성한다.
        /// </summary>
        public IReadOnlyList<BtNodeRecord> Nodes => nodes;

        /// <summary>
        /// 노드 ID로 노드 레코드를 찾는다.
        /// </summary>
        /// <param name="nodeId">검색할 노드 ID</param>
        /// <returns>찾은 노드. 없으면 null.</returns>
        public BtNodeRecord FindNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId) || nodes == null) return null;
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (n != null && n.id == nodeId) return n;
            }
            return null;
        }
    }
}
```
## BtNodeRecord(노드 데이터 레코드)

- 경로: `BT/BtNodeRecord.cs`
- 포인트:
  - 노드 타입/연결/파라미터 데이터 구조 참고
  - 런타임 노드 확장 시 이 구조와 호환 유지

```csharp
    /// GraphView 등 에디터 UI와 무관한 순수 데이터 모델.
    /// </summary>
    [Serializable]
    public sealed class BtNodeRecord
    {
        [SerializeField] public string id;
        [SerializeField] public BtNodeKind kind;
        [SerializeField] public string typeId;
        [SerializeField] public string title;
        [SerializeField] public BtAbortMode abortMode;

        [SerializeField] public List<string> children = new();
        [SerializeField] public List<BtParamValue> parameters = new();

        // --- Editor only metadata (runtime에서는 무시해도 안전) ---
        [SerializeField] public Vector2 graphPosition;
        [SerializeField] public Vector2 graphSize;
        [SerializeField] public string comment;
        [SerializeField] public int colorIndex;

        public override string ToString()
        {
            return string.IsNullOrEmpty(title)
                ? $"{typeId} ({id})"
                : $"{title} ({typeId}, {id})";
        }
    }
}
```
