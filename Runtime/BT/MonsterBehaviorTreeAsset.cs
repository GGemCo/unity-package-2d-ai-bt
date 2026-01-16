using System;
using System.Collections.Generic;
using UnityEngine;

namespace GGemCo2DAiBt
{
    /// <summary>
    /// 몬스터 전투 BT 정의 에셋.
    /// - GraphView 등 UI에 의존하지 않는 직렬화 데이터만 포함한다.
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
