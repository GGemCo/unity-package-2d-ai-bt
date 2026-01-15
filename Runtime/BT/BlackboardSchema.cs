using System;
using System.Collections.Generic;
using UnityEngine;

namespace GGemCo2DAiBt
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
