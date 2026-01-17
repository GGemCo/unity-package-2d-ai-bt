using System;
using System.Collections.Generic;
using UnityEngine;

namespace GGemCo2DAiBt
{
    /// <summary>
    /// 노드 파라미터 값(Variant).
    /// </summary>
    [Serializable]
    public struct BtParamValue
    {
        [SerializeField] public string key;
        [SerializeField] public BtValueType valueType;

        [SerializeField] public bool boolValue;
        [SerializeField] public int intValue;
        [SerializeField] public float floatValue;
        [SerializeField] public string stringValue;
        [SerializeField] public string enumValue;
        [SerializeField] public Vector2 vector2Value;
        [SerializeField] public Vector3 vector3Value;
        [SerializeField] public string blackboardKeyName;



        public static bool TryGetBool(List<BtParamValue> list, string key, out bool value)
        {
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (p.key == key)
                    {
                        value = p.boolValue;
                        return true;
                    }
                }
            }
            value = default;
            return false;
        }

        public static bool TryGetInt(List<BtParamValue> list, string key, out int value)
        {
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (p.key == key)
                    {
                        value = p.intValue;
                        return true;
                    }
                }
            }
            value = default;
            return false;
        }

        public static bool TryGetFloat(List<BtParamValue> list, string key, out float value)
        {
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (p.key == key)
                    {
                        value = p.floatValue;
                        return true;
                    }
                }
            }
            value = default;
            return false;
        }

        public static bool TryGetString(List<BtParamValue> list, string key, out string value)
        {
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (p.key == key)
                    {
                        value = p.stringValue;
                        return true;
                    }
                }
            }
            value = default;
            return false;
        }
        public static bool TryGetEnumString(List<BtParamValue> list, string key, out string value)
        {
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (p.key == key)
                    {
                        value = p.enumValue;
                        return true;
                    }
                }
            }
            value = default;
            return false;
        }

        public override string ToString()
        {
            return valueType switch
            {
                BtValueType.Bool => $"{key}={boolValue}",
                BtValueType.Int => $"{key}={intValue}",
                BtValueType.Float => $"{key}={floatValue}",
                BtValueType.String => $"{key}={stringValue}",
                BtValueType.EnumString => $"{key}={enumValue}",
                BtValueType.Vector2 => $"{key}={vector2Value}",
                BtValueType.Vector3 => $"{key}={vector3Value}",
                BtValueType.BlackboardKey => $"{key}=@{blackboardKeyName}",
                _ => $"{key}=?",
            };
        }
    }
}
