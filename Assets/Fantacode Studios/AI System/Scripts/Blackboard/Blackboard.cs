using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    public class Blackboard
    {
        private readonly Dictionary<string, object> data = new Dictionary<string, object>();

        public void Set<T>(string key, T value)
        {
            data[key] = value;
        }

        public bool TryGet<T>(string key, out T value)
        {
            if(string.IsNullOrEmpty(key))
            {
                Debug.LogWarning("Blackboard key is empty");
                value = default;
                return false;
            }

            if (data.TryGetValue(key, out object obj) && obj is T)
            {
                value = (T)obj;
                return true;
            }
            value = default;
            return false;
        }

        public T Get<T>(string key)
        {
            return (T)data[key];
        }

        public bool Has(string key)
        {
            return data.ContainsKey(key);
        }

        public bool Remove(string key)
        {
            return data.Remove(key);
        }

        public bool TryGetDisplayValue(string key, out string displayValue)
        {
            if (data.TryGetValue(key, out object obj) && obj != null)
            {
                displayValue = obj is float f ? f.ToString("F2") : obj.ToString();
                return true;
            }
            displayValue = null;
            return false;
        }
    }

}
