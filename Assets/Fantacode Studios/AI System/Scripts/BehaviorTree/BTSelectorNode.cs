using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    public class BTSelectorNode : IBTNode
    {
        private readonly IBTNode[] childNodes;
        public BTSelectorNode(params IBTNode[] childNodes)
        {
            this.childNodes = childNodes;
        }

        public BTExecutionStatus Tick()
        {
            IsRunning = true;
            foreach (var node in childNodes) 
            {
                var status = node.Tick();

                if (status != BTExecutionStatus.FAILURE)
                {
                    node.IsRunning = true;
                    foreach (var n in childNodes)
                    {
                        if (n != node)
                            n.IsRunning = false;
                    }
                    return status;
                }
            }

            // We weren't able to find a successful child node to execute
            return BTExecutionStatus.FAILURE;
        }

        public bool IsRunning { get; set; }
    }

}
