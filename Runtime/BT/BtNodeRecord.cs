using System;
using System.Collections.Generic;
using UnityEngine;

namespace GGemCo2DAiBt
{
    /// <summary>
    /// BT 노드 정의(직렬화 데이터).
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
