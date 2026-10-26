using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core 
{
    public class BTPrintAction : IBTNode
    {
        bool printed = false;

        readonly string message;
        public BTPrintAction(string message) 
        { 
            this.message = message;
        }

        public BTExecutionStatus Tick()
        {
            if (!printed)
            {
                Debug.Log(message);
                printed = true;
            }

            return BTExecutionStatus.SUCCESS;
        }

        public bool IsRunning { get; set; }

    }
}
