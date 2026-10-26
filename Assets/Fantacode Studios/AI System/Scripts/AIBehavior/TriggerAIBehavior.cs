using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    public class TriggerAIBehavior : MonoBehaviour
    {
        const string TriggerKey = "TriggerAuthoredBehavior";

        public enum TriggerMode { OnPlayerEnter, OnPlayerExit, OnStart }

        [SerializeField] TriggerMode triggerMode = TriggerMode.OnPlayerEnter;
        [SerializeField] List<AIController> ais = new List<AIController>();

        void Start()
        {
            if (triggerMode == TriggerMode.OnStart)
                Trigger();
        }

        void OnTriggerEnter(Collider other)
        {
            if (triggerMode == TriggerMode.OnPlayerEnter && other.CompareTag("Player"))
                Trigger();
        }

        void OnTriggerExit(Collider other)
        {
            if (triggerMode == TriggerMode.OnPlayerExit && other.CompareTag("Player"))
                Trigger();
        }

        void Trigger()
        {
            foreach (var ai in ais)
            {
                if (ai == null) continue;
                ai.Blackboard.Set(TriggerKey, true);
            }

            Destroy(gameObject);
        }
    }
}
