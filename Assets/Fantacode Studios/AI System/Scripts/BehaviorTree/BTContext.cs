using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    public class BTContext
    {
        public Blackboard Blackboard { get; }
        public object Owner { get; }

        public BTContext(object owner, Blackboard blackboard)
        {
            Owner = owner;
            Blackboard = blackboard;
        }
    }
}
