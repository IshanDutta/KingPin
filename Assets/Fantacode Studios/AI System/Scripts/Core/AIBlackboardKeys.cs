namespace FS_Core
{
    public static class AIBlackboardKeys
    {
        public const string Chase = "Chase";
        public const string ChaseTarget = "ChaseTarget";
        public const string TargetPosition = "TargetPosition";
        public const string Target = "Target";
        public const string FollowTarget = "FollowTarget";
        public const string IsTargetSet = "IsTargetSet";
        public const string HasVisualOnTarget = "HasVisualOnTarget";
        
        // Set by a state that checks out a stimulus via its own approach point (e.g. a flank that
        // overshoots the last seen position to sweep the hiding pocket beyond it).
        public const string StimulusCheckPosition = "StimulusCheckPosition";
        // Set by Combat Core when the AI takes a hit from the player: the attacker's position at
        // the moment of the hit. Consumed (removed) by the Stealth System to refresh the combat
        // situation — an attack proves the player's presence even without line of sight.
        public const string HitByPlayerPosition = "HitByPlayerPosition";
        public const string IsPaused = "IsPaused";
        public const string IsTargetUnreachableByGround = "IsTargetUnreachableByGround";
        public const string IsRagdolling = "IsRagdolling";
        public const string CanInterruptBehavior = "CanInterruptBehavior";
        public const string IsDead = "IsDead";
        public const string Health = "Health";
    }
}
