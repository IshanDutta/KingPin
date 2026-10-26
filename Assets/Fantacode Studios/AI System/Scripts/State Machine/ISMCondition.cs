using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    public interface ISMCondition
    {
        bool Evaluate(Blackboard blackboard);
    }
}
