using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    public abstract class BTDecoratorNode : IBTNode
    {
        protected readonly IBTNode childNode;
        public BTDecoratorNode(IBTNode childNode)
        {
            this.childNode = childNode;
        }

        public abstract BTExecutionStatus Tick();
        public bool IsRunning { get; set; }
    }

}
