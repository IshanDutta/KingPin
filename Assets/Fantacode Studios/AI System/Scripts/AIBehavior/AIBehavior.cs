using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    public class AIBehavior : MonoBehaviour
    {
        [SerializeField] BlackboardKey blackboardKey = new BlackboardKey(BlackboardValueType.AIBehavior);

        [Tooltip("If false, states like investigation or combat cannot interrupt this behavior — it always plays to completion.")]
        [SerializeField] bool interruptible = true;

        [SerializeReference, HideInInspector]
        List<AIBehaviorNode> nodes = new List<AIBehaviorNode>();

        public bool Interruptible => interruptible;
        public int NodeCount => nodes.Count;
        public AIBehaviorNode GetNode(int index) => nodes[index];
        public string BlackboardKeyName => blackboardKey.Name;
        public List<AIBehaviorNode> Nodes => nodes;

        void Awake()
        {
            var ai = GetComponent<AIController>();
            if (ai != null && !string.IsNullOrEmpty(blackboardKey.Name))
                ai.Blackboard.Set(blackboardKey.Name, this);
        }
    }
}
