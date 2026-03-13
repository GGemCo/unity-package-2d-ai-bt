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
        private readonly Label _debugBadge;
        private readonly Label _breakpointBadge;

        public BtNodeView(BtNodeRecord record)
        {
            NodeId = record.id;
            Kind = record.kind;
            TypeId = record.typeId;

            ApplyRecordToView(record);

            // Input: 노드 정책에 따라 Single/Multi 허용
            var inputCapacity = BtNodeParentPolicy.SupportsMultipleParents(record) ? Port.Capacity.Multi : Port.Capacity.Single;
            InPort = InstantiatePort(Orientation.Horizontal, Direction.Input, inputCapacity, typeof(bool));
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

            _breakpointBadge = new Label("●")
            {
                style =
                {
                    unityTextAlign = TextAnchor.MiddleCenter,
                    color = new Color(1f, 0.35f, 0.35f, 1f),
                    marginLeft = 6,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    display = DisplayStyle.None,
                }
            };
            titleContainer.Add(_breakpointBadge);

            _debugBadge = new Label()
            {
                style =
                {
                    unityTextAlign = TextAnchor.MiddleRight,
                    fontSize = 10,
                    marginLeft = 6,
                    opacity = 0.85f,
                    display = DisplayStyle.None,
                }
            };
            titleContainer.Add(_debugBadge);

            RefreshExpandedState();
            RefreshPorts();
        }

        /// <summary>
        /// 에셋 데이터(<see cref="BtNodeRecord"/>) 변경 후, 현재 View의 표시만 갱신한다.
        /// - 노드 이동/연결과 무관한 속성(예: Title) 변경 시 전체 리빌드(<c>PopulateFromAsset</c>)를 피하기 위해 사용한다.
        /// </summary>
        public void RefreshFromRecord(BtNodeRecord record)
        {
            if (record == null || record.id != NodeId) return;
            ApplyRecordToView(record);
        }

        private void ApplyRecordToView(BtNodeRecord record)
        {
            title = string.IsNullOrEmpty(record.title) ? record.typeId : record.title;
        }

        public void MarkAsRoot(bool isRoot)
        {
            _rootBadge.style.display = isRoot ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetBreakpointState(bool hasBreakpoint, string tooltipText = null)
        {
            _breakpointBadge.style.display = hasBreakpoint ? DisplayStyle.Flex : DisplayStyle.None;
            if (!string.IsNullOrEmpty(tooltipText))
            {
                _breakpointBadge.tooltip = tooltipText;
            }
            else
            {
                _breakpointBadge.tooltip = string.Empty;
            }
        }

        public void SetDebugInfo(string badgeText, string tooltipText)
        {
            tooltip = tooltipText ?? string.Empty;

            bool hasBadge = !string.IsNullOrEmpty(badgeText);
            _debugBadge.text = badgeText ?? string.Empty;
            _debugBadge.style.display = hasBadge ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
#endif
