#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using GGemCo2DAiBt;
using GGemCo2DCoreEditor;
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
            if (defs != null)
            {
                foreach (var def in defs)
                {
                    if (TryFindParam(node.parameters, def.Key, out _))
                        continue;

                    node.parameters.Add(CreateDefaultParam(def));
                }
            }

            // RandomWeighted는 자식 수에 따라 weight_i를 맞춰준다(없으면 1).
            // 표시/저장되는 슬롯 수와 런타임이 읽는 슬롯 수가 일치하도록, 남는 weight는 정리한다.
            if (node.typeId == BtTypeIds.Composite.RandomWeighted)
            {
                int childCount = node.children?.Count ?? 0;

                for (int i = 0; i < childCount; i++)
                {
                    string key = $"weight_{i}";
                    if (TryFindParam(node.parameters, key, out _))
                        continue;

                    node.parameters.Add(new BtParamValue
                    {
                        key = key,
                        valueType = BtValueType.Float,
                        floatValue = 1f
                    });
                }

                for (int i = node.parameters.Count - 1; i >= 0; i--)
                {
                    string key = node.parameters[i].key;
                    if (!TryParseWeightIndex(key, out int weightIndex))
                        continue;

                    if (weightIndex >= childCount)
                        node.parameters.RemoveAt(i);
                }
            }
        }

        public static VisualElement CreateParamEditor(EditorWindow owner, MonsterBehaviorTreeAsset asset, BtNodeRecord node, Action onChanged)
        {
            var root = new VisualElement();
            var defs = GetDisplayParamDefs(node);

            if (defs.Count == 0)
            {
                root.Add(new Label("No defined parameters."));
                return root;
            }

            foreach (var def in defs)
            {
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 4 } };

                row.Add(new Label(GetDisplayLabel(node, asset, def))
                {
                    style =
                    {
                        width = 130,
                        unityTextAlign = TextAnchor.MiddleLeft,
                        unityFontStyleAndWeight = def.Required ? FontStyle.Bold : FontStyle.Normal
                    }
                });

                var field = CreateValueField(owner, asset, node, def, onChanged);
                field.style.flexGrow = 1;
                row.Add(field);

                root.Add(row);
            }

            return root;
        }


        private static List<BtParamDef> GetDisplayParamDefs(BtNodeRecord node)
        {
            var result = new List<BtParamDef>();
            var defs = BtNodeTypeCatalog.GetParamDefs(node.typeId);
            if (defs != null)
                result.AddRange(defs);

            if (node.typeId == BtTypeIds.Composite.RandomWeighted)
            {
                int childCount = node.children?.Count ?? 0;
                for (int i = 0; i < childCount; i++)
                {
                    result.Add(new BtParamDef($"weight_{i}", BtValueType.Float, required: false, defaultValue: 1f, min: 0f));
                }
            }

            return result;
        }

        private static string GetDisplayLabel(BtNodeRecord node, MonsterBehaviorTreeAsset asset, BtParamDef def)
        {
            if (!TryParseWeightIndex(def.Key, out int weightIndex))
                return def.Key;

            string childTitle = null;
            if (node.children != null && weightIndex >= 0 && weightIndex < node.children.Count)
            {
                var child = asset != null ? asset.FindNode(node.children[weightIndex]) : null;
                childTitle = child != null && !string.IsNullOrWhiteSpace(child.title)
                    ? child.title
                    : node.children[weightIndex];
            }

            return string.IsNullOrWhiteSpace(childTitle)
                ? def.Key
                : $"{def.Key} ({childTitle})";
        }

        private static bool TryParseWeightIndex(string key, out int index)
        {
            index = -1;
            if (string.IsNullOrEmpty(key) || !key.StartsWith("weight_", StringComparison.Ordinal))
                return false;

            return int.TryParse(key.Substring("weight_".Length), out index);
        }

        private static VisualElement CreateValueField(EditorWindow owner, MonsterBehaviorTreeAsset asset, BtNodeRecord node, BtParamDef def, Action onChanged)
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
                        BtUndoUtility.RecordDelta(asset, "Edit BT Param");
                        SetFloat(node.parameters, def.Key, evt.newValue);
                        BtUndoUtility.SetDirty(asset);
                        onChanged?.Invoke();
                    });
                    return field;
                }

                case BtValueType.Int:
                {
                    var v = GetInt(node.parameters, def.Key, Convert.ToInt32(def.DefaultValue));

                    // skillUid는 테이블 기반으로 검색 가능한 드롭다운으로 입력한다.
                    // - Condition.CanUseSkill
                    // - Condition.SkillUseCountCompare
                    // - Action.UseSkill
                    if (IsSkillUidParam(node.typeId, def.Key))
                        return CreateSkillUidDropdownField(owner, asset, node, def, v, onChanged);

                    if (IsAffectUidParam(node.typeId, def.Key))
                    {
                        return CreateAffectUidDropdownField(owner, asset, node, def, v, onChanged);
                    }

                    var field = new IntegerField { value = v };
                    field.RegisterValueChangedCallback(evt =>
                    {
                        BtUndoUtility.RecordDelta(asset, "Edit BT Param");
                        SetInt(node.parameters, def.Key, evt.newValue);
                        BtUndoUtility.SetDirty(asset);
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
                        BtUndoUtility.RecordDelta(asset, "Edit BT Param");
                        SetBool(node.parameters, def.Key, evt.newValue);
                        BtUndoUtility.SetDirty(asset);
                        onChanged?.Invoke();
                    });
                    return field;
                }

                case BtValueType.EnumString:
                {
                    var v = GetEnumString(node.parameters, def.Key, def.DefaultValue?.ToString() ?? "");

                    if (TryGetEnumOptions(node.typeId, def.Key, out var options) && options.Length > 0)
                    {
                        var field = new PopupField<string>(options.ToList(), Mathf.Max(0, Array.IndexOf(options, v)));
                        field.RegisterValueChangedCallback(evt =>
                        {
                            BtUndoUtility.RecordDelta(asset, "Edit BT Param");
                            SetEnumString(node.parameters, def.Key, evt.newValue);
                            BtUndoUtility.SetDirty(asset);
                            onChanged?.Invoke();
                        });
                        return field;
                    }

                    var textField = new TextField { value = v, isDelayed = true };
                    textField.RegisterValueChangedCallback(evt =>
                    {
                        BtUndoUtility.RecordDelta(asset, "Edit BT Param");
                        SetEnumString(node.parameters, def.Key, evt.newValue);
                        BtUndoUtility.SetDirty(asset);
                        onChanged?.Invoke();
                    });
                    return textField;
                }

                case BtValueType.String:
                default:
                {
                    var v = GetString(node.parameters, def.Key, def.DefaultValue?.ToString() ?? "");
                    var field = new TextField { value = v, isDelayed = true };
                    field.RegisterValueChangedCallback(evt =>
                    {
                        BtUndoUtility.RecordDelta(asset, "Edit BT Param");
                        SetString(node.parameters, def.Key, evt.newValue);
                        BtUndoUtility.SetDirty(asset);
                        onChanged?.Invoke();
                    });
                    return field;
                }
            }
        }

        #region 스킬 선택 박스
        /// <summary>
        /// 지정한 노드 파라미터가 몬스터 스킬 테이블 선택 UI를 사용해야 하는지 확인합니다.
        /// </summary>
        private static bool IsSkillUidParam(string nodeTypeId, string paramKey)
        {
            if (!string.Equals(paramKey, "skillUid", StringComparison.Ordinal))
                return false;

            return string.Equals(nodeTypeId, "Condition.CanUseSkill", StringComparison.Ordinal)
                   || string.Equals(nodeTypeId, "Condition.IsSkillInCastRange", StringComparison.Ordinal)
                   || string.Equals(nodeTypeId, "Condition.SkillUseCountCompare", StringComparison.Ordinal)
                   || string.Equals(nodeTypeId, "Action.MoveToSkillRange", StringComparison.Ordinal)
                   || string.Equals(nodeTypeId, "Action.UseSkill", StringComparison.Ordinal)
                   || string.Equals(nodeTypeId, "Action.UseSkillAndWait", StringComparison.Ordinal)
                   || string.Equals(nodeTypeId, "Condition.LastSkillResult", StringComparison.Ordinal)
                   || string.Equals(nodeTypeId, "Condition.LastSkillCombatOutcome", StringComparison.Ordinal)
                   || string.Equals(nodeTypeId, "Action.ResetSkillUseCount", StringComparison.Ordinal);
        }

        private static VisualElement CreateSkillUidDropdownField(
            EditorWindow owner,
            MonsterBehaviorTreeAsset asset,
            BtNodeRecord node,
            BtParamDef def,
            int currentUid,
            Action onChanged)
        {
            // owner는 MonsterBtDesignerWindow 인스턴스를 전달하는 것을 권장한다.
            // (fallback: focusedWindow)
            owner ??= EditorWindow.focusedWindow;

            var options = BtSkillDropdownProvider.GetOptions();
            int selectedIndex = BtSkillDropdownProvider.FindIndexByUid(options, currentUid);

            var button = new Button();
            button.style.height = 20;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.text = BtSkillDropdownProvider.FormatSelected(options, selectedIndex, currentUid);

            button.clicked += () =>
            {
                if (owner == null)
                {
                    Debug.LogWarning("[BT] SearchableDropdown requires an owner EditorWindow.");
                    return;
                }

                // 매번 최신 테이블을 반영할 수 있도록 클릭 시에도 옵션을 재조회한다.
                var latestOptions = BtSkillDropdownProvider.GetOptions(forceReload: false);
                int latestSelectedIndex = BtSkillDropdownProvider.FindIndexByUid(latestOptions, GetInt(node.parameters, def.Key, currentUid));

                Rect rect = SearchableDropdownUtility.GetScreenRect(owner, button);
                SearchableDropdownUtility.ShowUiToolkit(
                    owner: owner,
                    activatorRectScreen: rect,
                    options: latestOptions,
                    selectedIndex: latestSelectedIndex,
                    onSelected: (idx, opt) =>
                    {
                        BtUndoUtility.RecordDelta(asset, "Edit BT Param");
                        SetInt(node.parameters, def.Key, opt.Data);
                        BtUndoUtility.SetDirty(asset);
                        button.text = BtSkillDropdownProvider.FormatSelected(latestOptions, idx, opt.Data);
                        onChanged?.Invoke();
                    },
                    defaultSearchMode: SearchableDropdownUtility.SearchMode.Both);
            };

            return button;
        }
        #endregion

        private static bool IsAffectUidParam(string nodeTypeId, string paramKey)
        {
            if (!string.Equals(paramKey, "affectUid", StringComparison.Ordinal))
                return false;

            return string.Equals(nodeTypeId, "Condition.HasAffect", StringComparison.Ordinal);
        }

        private static VisualElement CreateAffectUidDropdownField(
            EditorWindow owner,
            MonsterBehaviorTreeAsset asset,
            BtNodeRecord node,
            BtParamDef def,
            int currentUid,
            Action onChanged)
        {
            // owner는 MonsterBtDesignerWindow 인스턴스를 전달하는 것을 권장한다.
            // (fallback: focusedWindow)
            owner ??= EditorWindow.focusedWindow;

            var options = BtAffectDropdownProvider.GetOptions();
            int selectedIndex = BtAffectDropdownProvider.FindIndexByUid(options, currentUid);

            var button = new Button();
            button.style.height = 20;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.text = BtAffectDropdownProvider.FormatSelected(options, selectedIndex, currentUid);

            button.clicked += () =>
            {
                if (owner == null)
                {
                    Debug.LogWarning("[BT] SearchableDropdown requires an owner EditorWindow.");
                    return;
                }

                // 매번 최신 테이블을 반영할 수 있도록 클릭 시에도 옵션을 재조회한다.
                var latestOptions = BtAffectDropdownProvider.GetOptions(forceReload: false);
                int latestSelectedIndex = BtAffectDropdownProvider.FindIndexByUid(latestOptions, GetInt(node.parameters, def.Key, currentUid));

                Rect rect = SearchableDropdownUtility.GetScreenRect(owner, button);
                SearchableDropdownUtility.ShowUiToolkit(
                    owner: owner,
                    activatorRectScreen: rect,
                    options: latestOptions,
                    selectedIndex: latestSelectedIndex,
                    onSelected: (idx, opt) =>
                    {
                        BtUndoUtility.RecordDelta(asset, "Edit BT Param");
                        SetInt(node.parameters, def.Key, opt.Data);
                        BtUndoUtility.SetDirty(asset);
                        button.text = BtAffectDropdownProvider.FormatSelected(latestOptions, idx, opt.Data);
                        onChanged?.Invoke();
                    },
                    defaultSearchMode: SearchableDropdownUtility.SearchMode.Both);
            };

            return button;
        }
        #region 헬퍼
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
                case BtValueType.EnumString:
                    p.enumValue = def.DefaultValue?.ToString() ?? string.Empty;
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

        private static string GetEnumString(List<BtParamValue> list, string key, string fallback)
        {
            return BtParamValue.TryGetEnumString(list, key, out string v) ? v : fallback;
        }

        private static void SetEnumString(List<BtParamValue> list, string key, string value)
        {
            if (TryFindParam(list, key, out int idx))
            {
                var p = list[idx];
                p.valueType = BtValueType.EnumString;
                p.enumValue = value;
                list[idx] = p;
                return;
            }
            list.Add(new BtParamValue { key = key, valueType = BtValueType.EnumString, enumValue = value });
        }
        private static bool TryGetEnumOptions(string nodeTypeId, string key, out string[] options)
        {
            options = null;

            if (string.Equals(nodeTypeId, "Condition.SkillUseCountCompare", StringComparison.Ordinal) &&
                string.Equals(key, "op", StringComparison.Ordinal))
            {
                options = new[] { ">", ">=", "==", "!=", "<", "<=" };
                return true;
            }

            if (string.Equals(nodeTypeId, "Action.UseSkill", StringComparison.Ordinal) &&
                string.Equals(key, "busyReturn", StringComparison.Ordinal))
            {
                options = new[] { "Running", "Success" };
                return true;
            }

            if (string.Equals(nodeTypeId, "Action.ResetSkillUseCount", StringComparison.Ordinal) &&
                string.Equals(key, "mode", StringComparison.Ordinal))
            {
                options = new[] { "AllReset", "ResetOne", "SetOne" };
                return true;
            }

            if (string.Equals(nodeTypeId, BtTypeIds.Action.BeginEvade, StringComparison.Ordinal) &&
                string.Equals(key, "trigger", StringComparison.Ordinal))
            {
                options = new[] { "Manual", "SoftLimit", "HardLimit" };
                return true;
            }

            if (string.Equals(nodeTypeId, "Condition.LastSkillResult", StringComparison.Ordinal) &&
                string.Equals(key, "result", StringComparison.Ordinal))
            {
                options = new[] { "Succeeded", "Canceled", "Failed" };
                return true;
            }

            if (string.Equals(nodeTypeId, "Condition.LastSkillCombatOutcome", StringComparison.Ordinal) &&
                string.Equals(key, "outcome", StringComparison.Ordinal))
            {
                options = new[] { "Hit", "Guarded", "JustGuarded", "Missed", "Immune", "Evaded", "GuardBroken" };
                return true;
            }

            return false;
        }
        #endregion
    }
}
#endif
