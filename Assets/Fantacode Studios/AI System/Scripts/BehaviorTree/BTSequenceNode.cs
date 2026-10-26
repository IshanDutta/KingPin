using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    public class BTSequenceNode : IBTNode
    {
        private readonly IBTNode[] childNodes;
        private int currentIndex = 0;

        public BTSequenceNode(params IBTNode[] childNodes)
        {
            this.childNodes = childNodes;
            currentIndex = 0;
        }

        public BTExecutionStatus Tick()
        {
            IsRunning = true;
            while (currentIndex < childNodes.Length)
            {
                var status = childNodes[currentIndex].Tick();
                
                switch (status)
                {
                    case BTExecutionStatus.SUCCESS:
                        currentIndex++;
                        break;
                    case BTExecutionStatus.FAILURE:
                        currentIndex = 0;
                        return BTExecutionStatus.FAILURE;
                    case BTExecutionStatus.RUNNING:
                        return BTExecutionStatus.RUNNING;
                }
            }
            currentIndex = 0;
            return BTExecutionStatus.SUCCESS;
        }
        public bool IsRunning { get; set; }

    }
}
