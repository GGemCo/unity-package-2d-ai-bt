using System;
using System.Collections.Generic;
using UnityEngine;

namespace GGemCo2DAiBt
{
    /// <summary>
    /// 런타임 블랙보드. Schema 기반으로 초기화되며,
    /// 키 조회를 위해 내부 인덱스 캐시를 구성한다.
    /// </summary>
    public sealed class RuntimeBlackboard
    {
        private readonly BlackboardSchema _schema;
        private readonly List<BlackboardEntry> _entries;
        private readonly Dictionary<string, int> _indexByName;

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
    }
}
