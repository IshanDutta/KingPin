using FS_Core;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FS_Core
{
    public enum BlackboardValueType
    {
        Float,
        Bool,
        Int,
        String,
        Vector2,
        Vector3,
        Transform,
        Object,
        AIBehavior,
        AnimGraphClipInfo,
    }
    public class BlackBoardkeys : ScriptableObject
    {
        [SerializeField] List<BlackboardKey> keys = new List<BlackboardKey>();
        public List<BlackboardKey> Keys => keys;

#if UNITY_EDITOR
        private void OnValidate()
        {
            foreach (var p in keys)
                p?.EnsureID();
        }
#endif

        public BlackboardKey GetParameterByID(string id)
        {
            return keys.Find(p => p.Id == id);
        }
    }

    [Serializable]
    public class BlackboardKey
    {
        [SerializeField, HideInInspector] string id;   
        [SerializeField] string name;
        [SerializeField] BlackboardValueType type;
        [SerializeField] string description;

        public BlackboardKey(BlackboardValueType type)
        {
            this.type = type;
        }

        public string Id => id;
        public string Name { get { return name; } set { name = value; } }
        public string Description { get { return description; } set { description = value; } }
        public BlackboardValueType Type { get { return type; }set { type = value; } }

#if UNITY_EDITOR
        public void EnsureID()
        {
            if (string.IsNullOrEmpty(id))
                id = Guid.NewGuid().ToString();
        }


        public BlackboardKey() { EnsureID(); }
#endif
       
    }


}