using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{

    public enum BTExecutionStatus
    {
        SUCCESS,
        FAILURE,
        RUNNING
    }

    public interface IBTNode
    {
        BTExecutionStatus Tick();
        public bool IsRunning { get; set;}
    }
}
