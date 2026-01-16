#if UNITY_EDITOR
using GGemCo2DAiBt;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace GGemCo2DAiBtEditor
{
    /// <summary>
    /// GraphView 상에서 표시되는 BT 노드 View.
    /// 저장은 <see cref="BtNodeRecord"/>가 담당하며, 본 클래스는 UI 표현 및 연결 포트만 제공한다.
    /// </summary>
    public sealed class BtNodeView : Node
    {
        public string NodeId { get; }
        public BtNodeKind Kind { get; }
        public string TypeId { get; }

        public Port InPort { get; }
        public Port OutPort { get; }

        private readonly Label _rootBadge;

        public BtNodeView(BtNodeRecord record)
        {
            NodeId = record.id;
            Kind = record.kind;
            TypeId = record.typeId;

            title = string.IsNullOrEmpty(record.title) ? record.typeId : record.title;

            // Input: Single (항상 부모 1개만 허용)
            InPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(bool));
            InPort.portName = "";
            inputContainer.Add(InPort);

            // Output: Kind에 따라 생성/규칙 부여
            if (Kind == BtNodeKind.Composite)
            {
                OutPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
                OutPort.portName = "";
                outputContainer.Add(OutPort);
            }
            else if (Kind == BtNodeKind.Decorator)
            {
                OutPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool));
                OutPort.portName = "";
                outputContainer.Add(OutPort);
            }
            else
            {
                // Condition/Action은 자식이 없으므로 Output 포트 없음
                OutPort = null;
            }

            _rootBadge = new Label("ROOT")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginLeft = 6,
                    display = DisplayStyle.None,
                }
            };
            titleContainer.Add(_rootBadge);

            RefreshExpandedState();
            RefreshPorts();
        }

        public void MarkAsRoot(bool isRoot)
        {
            _rootBadge.style.display = isRoot ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
#endif
