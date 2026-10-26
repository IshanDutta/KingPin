using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{

    public class BlackboardInitValueAssigner : MonoBehaviour
    {
        [SerializeField] List<BlackboardInitValue> initialBlackboardValues = new();

        AIController ai;
        private void Awake()
        {
            ai = GetComponent<AIController>();
            foreach (var init in initialBlackboardValues)
                init.Apply(ai.Blackboard);
        }
    }
}
