using FS_Core;
using UnityEngine;

namespace FS_Core
{
    public class WaitState : State<AIController>
    {
        float duration;
        bool waitForever;
        float timer;

        public WaitState(float duration, bool waitForever)
        {
            this.duration = duration;
            this.waitForever = waitForever;
        }

        public override void OnEnter(AIController owner)
        {
            timer = 0f;
        }

        public override void Execute()
        {
            if (waitForever) return;

            timer += Time.deltaTime;
            if (timer >= duration)
                IsCompleted = true;
        }

        public override void Exit()
        {
        }
    }
}
