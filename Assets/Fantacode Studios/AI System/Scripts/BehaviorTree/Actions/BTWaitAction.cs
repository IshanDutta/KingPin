using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FS_Core
{
    public class BTWaitAction : IBTNode
    {
        private float waitTime;
        private float timer;

        public BTWaitAction(float waitTime)
        {

            this.waitTime = waitTime;
            this.timer = 0f;
        }

        public BTExecutionStatus Tick()
        {
            
            timer += Time.deltaTime;
            if (timer >= waitTime)
            {
                timer = 0f;
                return BTExecutionStatus.SUCCESS;
            }

            return BTExecutionStatus.RUNNING;
        }
        public bool IsRunning { get; set; }
    }
}
