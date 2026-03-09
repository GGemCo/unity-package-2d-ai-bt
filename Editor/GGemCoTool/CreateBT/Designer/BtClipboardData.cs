#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using GGemCo2DAiBt;
using UnityEngine;

namespace GGemCo2DAiBtEditor
{
    /// <summary>
    /// BT 디자이너의 노드 복사/붙여넣기용 클립보드 데이터.
    /// GraphView UI 객체를 직접 저장하지 않고, 순수 직렬화 데이터만 보관한다.
    /// </summary>
    [Serializable]
    public sealed class BtClipboardData
    {
        public string schemaVersion = "1.0.0";
        public string sourceTreeGuid;
        public List<BtClipboardNodeData> nodes = new();
    }

    /// <summary>
    /// 복사된 단일 노드 정보.
    /// children에는 '복사 집합 내부'의 자식 노드 ID만 기록된다.
    /// </summary>
    [Serializable]
    public sealed class BtClipboardNodeData
    {
        public string oldId;
        public BtNodeKind kind;
        public string typeId;
        public string title;
        public BtAbortMode abortMode;
        public string comment;
        public int colorIndex;
        public Vector2 graphPosition;
        public Vector2 graphSize;
        public List<string> children = new();
        public List<BtParamValue> parameters = new();
    }
}
#endif
