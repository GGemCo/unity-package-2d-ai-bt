#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using GGemCo2DAiBt;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GGemCo2DAiBtEditor
{
    /// <summary>
    /// 디자이너에서 노드 파라미터를 '정의 기반'으로 자동 생성/편집하기 위한 유틸리티.
    /// </summary>
    public static class BtEditorParamUtility
    {
        public static void EnsureParams(BtNodeRecord node, MonsterBehaviorTreeAsset asset)
        {
            if (node == null) return;
            node.parameters ??= new List<BtParamValue>();

            var defs = BtNodeTypeCatalog.GetParamDefs(node.typeId);
            if (defs == null) return;

            foreach (var def in defs)
            {
                if (TryFindParam(node.parameters, def.Key, out _))
                    continue;

                node.parameters.Add(CreateDefaultParam(def));
            }

            // RandomWeighted는 자식 수에 따라 weight_i를 맞춰준다(없으면 1)
            if (node.typeId == BtTypeIds.Composite.RandomWeighted && node.children != null)
            {
                for (int i = 0; i < node.children.Count; i++)
                {
                    string k = $"weight_{i}";
                    if (TryFindParam(node.parameters, k, out _)) continue;
                    node.parameters.Add(new BtParamValue { key = k, valueType = BtValueType.Float, floatValue = 1f });
                }
            }
        }

        public static VisualElement CreateParamEditor(MonsterBehaviorTreeAsset asset, BtNodeRecord node, Action onChanged)
        {
            var root = new VisualElement();

            var defs = BtNodeTypeCatalog.GetParamDefs(node.typeId);
            if (defs == null || defs.Count == 0)
            {
                root.Add(new Label("No defined parameters."));
                return root;
            }

            foreach (var def in defs)
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 4 } };

                row.Add(new Label(def.Key)
                {
                    style =
                    {
                        width = 130,
                        unityTextAlign = TextAnchor.MiddleLeft,
                        unityFontStyleAndWeight = def.Required ? FontStyle.Bold : FontStyle.Normal
                    }
                });

                var field = CreateValueField(asset, node, def, onChanged);
                field.style.flexGrow = 1;
                row.Add(field);

                root.Add(row);
            }

            return root;
        }

        private static VisualElement CreateValueField(MonsterBehaviorTreeAsset asset, BtNodeRecord node, BtParamDef def, Action onChanged)
        {
            EnsureParamExists(node, def);

            switch (def.ValueType)
            {
                case BtValueType.Float:
                {
                    var f = GetFloat(node.parameters, def.Key, Convert.ToSingle(def.DefaultValue));
                    var field = new FloatField { value = f };
                    field.RegisterValueChangedCallback(evt =>
                    {
                        Undo.RecordObject(asset, "Edit BT Param");
                        SetFloat(node.parameters, def.Key, evt.newValue);
                        EditorUtility.SetDirty(asset);
                        onChanged?.Invoke();
                    });
                    return field;
                }

                case BtValueType.Int:
                {
                    var v = GetInt(node.parameters, def.Key, Convert.ToInt32(def.DefaultValue));
                    var field = new IntegerField { value = v };
                    field.RegisterValueChangedCallback(evt =>
                    {
                        Undo.RecordObject(asset, "Edit BT Param");
                        SetInt(node.parameters, def.Key, evt.newValue);
                        EditorUtility.SetDirty(asset);
                        onChanged?.Invoke();
                    });
                    return field;
                }

                case BtValueType.Bool:
                {
                    var v = GetBool(node.parameters, def.Key, def.DefaultValue is bool b && b);
                    var field = new Toggle { value = v };
                    field.RegisterValueChangedCallback(evt =>
                    {
                        Undo.RecordObject(asset, "Edit BT Param");
                        SetBool(node.parameters, def.Key, evt.newValue);
                        EditorUtility.SetDirty(asset);
                        onChanged?.Invoke();
                    });
                    return field;
                }

                case BtValueType.String:
                default:
                {
                    var v = GetString(node.parameters, def.Key, def.DefaultValue?.ToString() ?? "");
                    var field = new TextField { value = v };
                    field.RegisterValueChangedCallback(evt =>
                    {
                        Undo.RecordObject(asset, "Edit BT Param");
                        SetString(node.parameters, def.Key, evt.newValue);
                        EditorUtility.SetDirty(asset);
                        onChanged?.Invoke();
                    });
                    return field;
                }
            }
        }

        private static void EnsureParamExists(BtNodeRecord node, BtParamDef def)
        {
            node.parameters ??= new List<BtParamValue>();
            if (TryFindParam(node.parameters, def.Key, out _)) return;
            node.parameters.Add(CreateDefaultParam(def));
        }

        private static BtParamValue CreateDefaultParam(BtParamDef def)
        {
            var p = new BtParamValue { key = def.Key, valueType = def.ValueType };

            switch (def.ValueType)
            {
                case BtValueType.Float:
                    p.floatValue = def.DefaultValue is float f ? f : 0f;
                    break;
                case BtValueType.Int:
                    p.intValue = def.DefaultValue is int i ? i : 0;
                    break;
                case BtValueType.Bool:
                    p.boolValue = def.DefaultValue is bool b && b;
                    break;
                case BtValueType.String:
                default:
                    p.stringValue = def.DefaultValue?.ToString() ?? string.Empty;
                    break;
            }

            return p;
        }

        private static bool TryFindParam(List<BtParamValue> list, string key, out int index)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].key == key) { index = i; return true; }
            }
            index = -1;
            return false;
        }

        private static float GetFloat(List<BtParamValue> list, string key, float fallback)
        {
            return BtParamValue.TryGetFloat(list, key, out float v) ? v : fallback;
        }

        private static void SetFloat(List<BtParamValue> list, string key, float value)
        {
            if (TryFindParam(list, key, out int idx))
            {
                var p = list[idx];
                p.valueType = BtValueType.Float;
                p.floatValue = value;
                list[idx] = p;
                return;
            }
            list.Add(new BtParamValue { key = key, valueType = BtValueType.Float, floatValue = value });
        }

        private static int GetInt(List<BtParamValue> list, string key, int fallback)
        {
            return BtParamValue.TryGetInt(list, key, out int v) ? v : fallback;
        }

        private static void SetInt(List<BtParamValue> list, string key, int value)
        {
            if (TryFindParam(list, key, out int idx))
            {
                var p = list[idx];
                p.valueType = BtValueType.Int;
                p.intValue = value;
                list[idx] = p;
                return;
            }
            list.Add(new BtParamValue { key = key, valueType = BtValueType.Int, intValue = value });
        }

        private static bool GetBool(List<BtParamValue> list, string key, bool fallback)
        {
            return BtParamValue.TryGetBool(list, key, out bool v) ? v : fallback;
        }

        private static void SetBool(List<BtParamValue> list, string key, bool value)
        {
            if (TryFindParam(list, key, out int idx))
            {
                var p = list[idx];
                p.valueType = BtValueType.Bool;
                p.boolValue = value;
                list[idx] = p;
                return;
            }
            list.Add(new BtParamValue { key = key, valueType = BtValueType.Bool, boolValue = value });
        }

        private static string GetString(List<BtParamValue> list, string key, string fallback)
        {
            return BtParamValue.TryGetString(list, key, out string v) ? v : fallback;
        }

        private static void SetString(List<BtParamValue> list, string key, string value)
        {
            if (TryFindParam(list, key, out int idx))
            {
                var p = list[idx];
                p.valueType = BtValueType.String;
                p.stringValue = value;
                list[idx] = p;
                return;
            }
            list.Add(new BtParamValue { key = key, valueType = BtValueType.String, stringValue = value });
        }
    }
}
#endif
