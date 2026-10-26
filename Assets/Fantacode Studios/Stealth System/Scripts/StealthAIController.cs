using System.Collections.Generic;
using FS_Core;
using FS_InteractionSystem;
using FS_Util;
using UnityEngine;

namespace FS_StealthSystem
{
    public class StealthAIController : MonoBehaviour
    {
        [Header("Detection Settings")]
        [Tooltip("Detection rate multiplier applied while the AI is in an alerted investigation, so it spots the player much faster.")]
        [SerializeField] float alertedInvestigationMultiplier = 4f;

        [Tooltip("While in combat with an acquired target, the AI tracks it by raw line of sight within this radius, ignoring the vision volumes (which only model stealth-phase detection). Omnidirectional and without a height limit, so the player can't break tracking by circling behind the AI or climbing — only occlusion or concealment. Also acts as the combat leash: beyond this distance the target is lost. Set to 0 to disable.")]
        [SerializeField] float combatSenseRadius = 25f;

        [Tooltip("When the AI is hit by the player from within this range, the attack itself reveals the attacker's position (like a touch) and refreshes the combat situation's last known position. Hits from beyond this range (e.g. distant gunfire) reveal nothing.")]
        [SerializeField] float hitSenseRange = 4f;

        [Header("Dialogue")]
        [Tooltip("Barks played during a suspicious (low-priority) investigation.")]
        [SerializeField] DialogueBarkSet suspiciousInvestigationBarkSet;
        [Tooltip("Barks played during an alert (high-priority) investigation.")]
        [SerializeField] DialogueBarkSet alertInvestigationBarkSet;
        [Tooltip("Barks played while warning a player trespassing in a restricted area.")]
        [SerializeField] DialogueBarkSet trespassingBarkSet;
        [Tooltip("Barks played during combat.")]
        [SerializeField] DialogueBarkSet combatBarkSet;
        [Tooltip("Bark played when the AI sees a non-suspicious player throw something, instead of investigating the sound.")]
        [SerializeField] DialogueBarkSet sawThrowBarkSet;

        public Stimulus ActiveStimulus { get; private set; }

        // Stimulus for which the awareness should be tracked (only for player right now)
        public Stimulus PerceptionCandidate { get; private set; }

        // True while the AI can actually see the player (LOS and not concealed in a hiding area)
        public bool PlayerInSight { get; private set; }

        VisualStimulus playerStimulus;

        /// <summary>
        /// Target stimuli can be Player or other threats
        /// </summary>
        VisualStimulus targetStimulus;


        Situation currentSituation;
        public Situation CurrentSituation
        {
            get => currentSituation;
            set
            {
                currentSituation = value;
                aiController.Blackboard.Set(StealthBlackboardKeys.InSituation, value != null);
            }
        }

        public AIController AIController => aiController;

        AwarenessSystem awarenessSystem;
        AIController aiController;
        AIVision aiVision;
        BoneMapper boneMapper;


        bool playerSeenHiding;
        bool settingTargetInternally;


        SuspicionProfile suspicionProfile;

        private void Awake()
        {
            awarenessSystem = GetComponent<AwarenessSystem>();
            aiController = GetComponent<AIController>();
            boneMapper = GetComponent<BoneMapper>();
            aiVision = GetComponent<AIVision>();
        }

        private void Start()
        {
            StealthAIManager.Instance.RegisterAI(this);
            aiController.Damagable.OnDead += OnDead;
            aiController.TargetHandler.OnTargetChanged += OnTargetChanged;
        }

        private void OnDisable()
        {
            if (StealthAIManager.Instance != null)
                StealthAIManager.Instance.UnregisterAI(this);
            aiController.TargetHandler.OnTargetChanged -= OnTargetChanged;
        }

        void OnDead()
        {
            StealthAIManager.Instance.UnregisterAI(this);

            if (aiController.AudioSource != null && aiController.AudioSource.isPlaying)
                aiController.AudioSource.Stop();

            aiVision.enabled = false;
            GetComponent<AIHearing>().enabled = false;
            if (TryGetComponent<AITouch>(out var aiTouch)) aiTouch.enabled = false;

            awarenessSystem.SetAwareness(0f);
            ReleaseSearchPosition();

            if (StealthSettings.i.DeadBodyPrefab != null) 
            {
                var hipsBone = boneMapper.GetBone(BoneType.Hips);
                Instantiate(StealthSettings.i.DeadBodyPrefab, hipsBone);
            }

            if (StealthSettings.i.CarryHotspot != null)
            {
                var hipsBone = boneMapper.GetBone(BoneType.Hips);
                var carryHotspot = Instantiate(StealthSettings.i.CarryHotspot, hipsBone);
                carryHotspot.objectToCarry = GetComponent<Carryable>();
            }

            enabled = false;
        }

        private void Update()
        {
            CheckHitByPlayer();
            CheckStimulusReached();
        }

        // Consumes the hit report published by the combat system (via blackboard, keeping the systems decoupled). 
        // A close-range hit proves the player is right there.
        void CheckHitByPlayer()
        {
            if (!aiController.Blackboard.TryGet<Vector3>(AIBlackboardKeys.HitByPlayerPosition, out Vector3 attackerPos))
                return;
            aiController.Blackboard.Remove(AIBlackboardKeys.HitByPlayerPosition);

            if (Vector3.Distance(transform.position, attackerPos) <= hitSenseRange)
                StealthAIManager.Instance.ReportHitByPlayer(this, attackerPos);
        }

        void CheckStimulusReached()
        {
            if (CurrentSituation == null || CurrentSituation.StimulusReached) return;

            // Investigation: only investigators check
            // Combat: any participant reaching the last seen position counts
            if (CurrentSituation.IsInvestigationSituation() && !CurrentSituation.IsInvestigator(this))
                return;

            // A state that is actively checking out the stimulus publishes its own approach point
            // (a flank or chase overshooting the last seen pos to sweep the hiding pocket beyond it)
            Vector3 checkPos = CurrentSituation.Position;
            if (CurrentSituation.Type == SituationType.Combat)
            {
                if (aiController.Blackboard.TryGet<Vector3>(AIBlackboardKeys.StimulusCheckPosition, out Vector3 approachPoint))
                    checkPos = approachPoint;
                else
                    return;     // Combat Situation must have a StimulusCheckPosition
            }

            float dist = Vector3.Distance(transform.position, checkPos);
            if (dist <= aiController.NavAgent.stoppingDistance + 0.5f)
                StealthAIManager.Instance.OnStimulusReached(CurrentSituation, this);
        }

        public void HandleVision(VisionInfo visionInfo)
        {
            // Combat tracking: the vision volumes should only be used in stealth-phase detection. If a target that is already in
            // combat goes out of the vision volume (e.g. climbed above the volume height, or stepped behind the AI) is still 
            // tracked by raw LOS within combatSenseRadius
            if (IsPlayerTarget() && (visionInfo == null || visionInfo.Stimulus.Type != VisualStimulusType.Player)
                && TrackTargetInCombatRange())
                visionInfo = new VisionInfo { Zone = VisionZone.Primary, Stimulus = targetStimulus, IsHidden = false };

            PlayerInSight = visionInfo != null && visionInfo.Stimulus.Type == VisualStimulusType.Player && !visionInfo.IsHidden;
            playerStimulus = PlayerInSight? visionInfo.Stimulus: null;
            
            // Player target was previously detected but is now lost
            if (IsPlayerTarget() && (visionInfo == null || visionInfo.Stimulus.Type != VisualStimulusType.Player))
            {
                HandlePlayerLostFromVision();
                return;
            }

            // If nothing found in vision, then return
            if (visionInfo == null)
            {
                awarenessSystem.DecayAwareness();
                if (PerceptionCandidate != null && awarenessSystem.CurrentAwareness <= 0)
                    PerceptionCandidate = null;

                return;
            }

            var newStimulus = visionInfo.Stimulus.GetStimulus();

            // Don't replace the current stimulus if newly found stimulus has lower type priority or importance
            if (!StealthAIManager.Instance.CanPursueStimulus(this, newStimulus))
                return;

            switch (visionInfo.Stimulus.Type)
            {
                case VisualStimulusType.Player:
                    HandlePlayer(newStimulus, visionInfo);
                    break;

                case VisualStimulusType.Threat:
                    HandleThreat(newStimulus, visionInfo);
                    break;

                case VisualStimulusType.AlertObject:
                    HandleAlertObject(newStimulus, visionInfo);
                    break;

                case VisualStimulusType.SuspiciousObject:
                    HandleSuspiciousObject(newStimulus, visionInfo);
                    break;
            }
        }

        public void HandleAudio(Stimulus stimulus)
        {
            if (!StealthAIManager.Instance.CanPursueStimulus(this, stimulus))
                return;

            if (stimulus.priority == StimulusPriority.High || stimulus.priority == StimulusPriority.Critical)
            {
                // Go to Alerted Investigate via situation system
                StealthAIManager.Instance.CreateOrJoinSituation(SituationType.AlertInvestigation, stimulus.position, stimulus, this);
            }
            else
            {
                bool insideRestricted = RestrictedArea.ContainsPosition(stimulus.position);
                bool insideHostile = HostileArea.ContainsPosition(stimulus.position);
                bool insidePublicArea = !(insideRestricted || insideHostile);

                if (stimulus.soundType == GameSoundType.Footstep)
                {
                    // Footsteps are ignored when outside restricted areas, when AI is
                    // already aware, or in combat.
                    if (insidePublicArea || awarenessSystem.IsAware || aiController.Target != null)
                        return;

                    // Heard while the player is visible — nothing to investigate, but the
                    // noise makes the AI notice the player faster
                    if (PlayerInSight)
                    {
                        awarenessSystem.IncreaseAwarenessFromSound(playerStimulus);
                        //return;
                    }
                }

                // Source attribution: AI saw who caused the sound and they're not suspicious
                if (stimulus.source != null && PerceptionCandidate != null && PerceptionCandidate.source == stimulus.source)
                {
                    var profile = stimulus.source.GetComponent<SuspicionProfile>();
                    if (profile != null && !profile.IsSuspicious)
                    {
                        // AI saw a non-threat throw something — bark at them, don't investigate
                        if (sawThrowBarkSet != null && CurrentSituation == null)
                        {
                            var audio = sawThrowBarkSet.Pick();
                            if (audio != null)
                                aiController.Speak(audio);
                        }
                        return;
                    }
                }

                // Normal investigation via situation system
                StealthAIManager.Instance.CreateOrJoinSituation(SituationType.SuspiciousInvestigation, stimulus.position, stimulus, this);
            }
        }

        void HandlePlayer(Stimulus stimulus, VisionInfo visionInfo)
        {
            var profile = GetSuspicionProfile(visionInfo.Stimulus);

            bool notDetected = aiController.Target == null && !IdentifiedPlayerAsThreat;
            bool hasNoSuspiciousProfile = profile != null && !profile.IsSuspicious;

            // No suspicion, decay and ignore
            // Skip this gate if already in combat (has a target) so awareness doesn't decay mid-fight
            if (notDetected && hasNoSuspiciousProfile)
            {
                awarenessSystem.DecayAwareness();
                PerceptionCandidate = stimulus;     // For audio attribution. For example, if a non supsicious player throws a brick in front of the AI
                return;
            }

            // Player is inside a hiding area and fully concealed — AI has LOS but can't actually see them.
            if (visionInfo.IsHidden)
            {
                if (aiController.Target != null)
                {
                    // Had eyes on the player the frame they became concealed → witnessed the hide
                    bool hadVisual = GetHasVisualOnTarget();
                    if (hadVisual)
                        playerSeenHiding = true;

                    // A witness keeps live tracking of the player they saw hide; an AI that only
                    // regained raw LOS to the hiding spot can't see a concealed target
                    SetHasVisualOnTarget(playerSeenHiding);
                }

                if (notDetected)
                {
                    // Not detected yet — decay awareness
                    awarenessSystem.DecayAwareness();
                    if (PerceptionCandidate != null && awarenessSystem.CurrentAwareness <= 0)
                        PerceptionCandidate = null;
                }
                return;
            }

            // Player is visible — reset lost flag
            SetHasVisualOnTarget(true);

            // Track whether AI sees the player walk into a hiding area or concealment spot (not yet concealed)
            playerSeenHiding = targetStimulus != null &&
                (HidingArea.IsInsideAnyArea(targetStimulus) || targetStimulus.IsInsideEnterExitSpot);

            // Player detection goes through gradual awareness. AI on high alert detects the player much faster
            bool alerted = aiController.Blackboard.TryGet(StealthBlackboardKeys.IsAlertedInvestigation, out bool isAlerted) && isAlerted;
            awarenessSystem.UpdateAwarenessFromVision(visionInfo, alerted ? alertedInvestigationMultiplier : 1f);
            PerceptionCandidate = stimulus;

            if (awarenessSystem.IsAware)
                ReactToDetectedPlayer(stimulus, visionInfo.Stimulus, profile);
        }

        /// <summary>
        /// Reaction once the AI is fully aware of the player — warn first in restricted
        /// areas, otherwise engage. Shared by vision detection and touch.
        /// </summary>
        void ReactToDetectedPlayer(Stimulus stimulus, VisualStimulus visualStimulus, SuspicionProfile profile)
        {
            PerceptionCandidate = null;
            ActiveStimulus = stimulus;

            if (aiController.Target == null && profile != null && profile.ShouldWarn)
            {
                // Restricted area — warn before combat (skip if already in trespassing or higher)
                if (CurrentSituation == null || CurrentSituation.Type < SituationType.TrespassingRestrictedArea)
                {
                    StealthAIManager.Instance.CreateOrJoinSituation(SituationType.TrespassingRestrictedArea,
                        visualStimulus.transform.position, stimulus, this);
                }
            }
            else
            {
                // Instant threat or default suspicious — go to combat
                if (aiController.Target == null)
                {
                    bool accepted = TrySetTarget(visualStimulus.transform);
                    if (!accepted) return;
                }

                StealthAIManager.Instance.ReportPlayerSpotted(this, stimulus);
                IdentifiedPlayerAsThreat = true;
            }
        }

        /// <summary>
        /// Called by AITouch when the player's collider comes in contact with the AI's body.
        /// Touch means instant awareness — the reaction depends on the suspicion profile,
        /// same as a completed visual detection (ignore / warn / combat).
        /// </summary>
        public void HandleTouch(VisualStimulus visualStimulus)
        {
            // Ignore contact while dead or in a synced animation (e.g. being stealth-finished)
            if (aiController.Damagable.IsDead || aiController.ActionExecutor.IsInSyncedAnimation) return;

            var profile = GetSuspicionProfile(visualStimulus);

            bool notDetected = aiController.Target == null && !IdentifiedPlayerAsThreat;
            bool hasNotSuspiciousProfile = profile != null && !profile.IsSuspicious;

            // Public area — touching a non-suspicious player has no effect
            if (notDetected && hasNotSuspiciousProfile) return;

            var stimulus = new Stimulus
            {
                modality = StimulusModality.Touch,
                priority = StimulusPriority.Critical,
                importance = 1f,
                position = visualStimulus.transform.position,
                source = visualStimulus.gameObject,
            };

            if (!StealthAIManager.Instance.CanPursueStimulus(this, stimulus))
                return;

            awarenessSystem.SetAwareness(1f);
            ReactToDetectedPlayer(stimulus, visualStimulus, profile);
        }

        void HandleThreat(Stimulus stimulus, VisionInfo visionInfo)
        {
            var target = visionInfo.Stimulus.transform;

            // Skip self
            if (target == transform) return;

            // Player target takes priority
            if (IsPlayerTarget()) return;

            if (aiController.Target == null)
            {
                TrySetTarget(target);
            }
        }

        void HandleAlertObject(Stimulus stimulus, VisionInfo visionInfo)
        {
            if (StealthAIManager.Instance.IsStimulusResolved(stimulus))
                return;

            if (visionInfo.Zone == VisionZone.Primary)
            {
                // Go to Alerted Investigate via situation system
                StealthAIManager.Instance.CreateOrJoinSituation(SituationType.AlertInvestigation, stimulus.position, 
                    stimulus, this);
                StealthAIManager.Instance.ResolveStimulus(stimulus);
            }
        }

        void HandleSuspiciousObject(Stimulus stimulus, VisionInfo visionInfo)
        {
            if (StealthAIManager.Instance.IsStimulusResolved(stimulus))
                return;

            if (visionInfo.Zone == VisionZone.Primary)
            {
                // Go to Normal Investigate via situation system
                StealthAIManager.Instance.CreateOrJoinSituation(SituationType.SuspiciousInvestigation, 
                    visionInfo.Stimulus.transform.position, stimulus, this);
                StealthAIManager.Instance.ResolveStimulus(stimulus);
            }
        }

        void HandlePlayerLostFromVision()
        {
            // AI saw the player enter the hiding area — keep the target
            if (playerSeenHiding)
                return;

            // Record last known position on the first frame of losing LOS
            bool hasVisual = GetHasVisualOnTarget();
            if (hasVisual)
                AIManager.Instance.LastKnownTargetPos = aiController.Target.position;

            // Signal LOS lost to StealthAIManager
            SetHasVisualOnTarget(false);
            StealthAIManager.Instance.ReportPlayerLost(this);
        }

        // Only tracks, never detects: targetStimulus is non-null only while in combat
        // (set in OnCombatSituation, cleared on downgrade), so stealth detection is unaffected
        bool TrackTargetInCombatRange()
        {
            if (combatSenseRadius <= 0f || targetStimulus == null) return false;
            if (targetStimulus.IsConcealed || HidingArea.IsHidden(targetStimulus)) return false;
            if (Vector3.Distance(transform.position, targetStimulus.transform.position) > combatSenseRadius) return false;

            return AIUtil.HasLineOfSight(targetStimulus.transform, transform.position + Vector3.up * aiVision.EyeHeight, heightOffset: targetStimulus.RaycastHeightOffset);
        }

        SuspicionProfile GetSuspicionProfile(VisualStimulus visualStimulus)
        {
            if (suspicionProfile == null)
                suspicionProfile = visualStimulus.GetComponent<SuspicionProfile>();

            return suspicionProfile;
        }

        bool TrySetTarget(Transform target)
        {
            settingTargetInternally = true;
            bool accepted = aiController.TargetHandler.TrySetTarget(target);
            settingTargetInternally = false;
            
            return accepted;
        }

        void ClearTarget()
        {
            playerSeenHiding = false;
            TrySetTarget(null);
        }

        void OnTargetChanged(Transform newTarget)
        {
            // Always sync targetStimulus with the current target
            targetStimulus = newTarget != null
                ? newTarget.GetComponentInChildren<VisualStimulus>()
                : null;

            if (newTarget == null)
                playerSeenHiding = false;

            if (settingTargetInternally || newTarget == null) return;

            // Target was set by an external system (e.g. FighterCore on damage).
            // If we don't already have a combat situation, escalate to combat.
            bool alreadyInCombat = CurrentSituation != null && CurrentSituation.Type == SituationType.Combat;
            if (alreadyInCombat) return;

            var stimulus = new Stimulus
            {
                modality = StimulusModality.Damage,
                priority = StimulusPriority.Critical,
                importance = 1f,
                position = newTarget.position,
                source = newTarget.gameObject,
            };

            awarenessSystem.SetAwareness(1f);
            IdentifiedPlayerAsThreat = true;
            StealthAIManager.Instance.ReportPlayerSpotted(this, stimulus);
        }

        bool IsPlayerTarget()
        {
            return targetStimulus != null && targetStimulus.Type == VisualStimulusType.Player;
        }


        // ── Situation Lifecycle Methods ──────────────────────────────────────────


        // Used to notify an AI about an investigation situation
        public void OnInvestigateSituation(Stimulus stimulus, Situation situation, bool isInvestigator, bool restart = false)
        {
            if (aiController.Target != null)
                return; // already in combat

            CurrentSituation = situation;
            ActiveStimulus = stimulus;

            // Vision owns awareness — investigations only ensure decay is running
            // (a combat downgrade arrives here with PauseDecay still set)
            awarenessSystem.PauseDecay = false;

            aiController.Blackboard.Set(StealthBlackboardKeys.MoveToInvestigatePos, isInvestigator);
            SetInvestigating(stimulus.position, restart);
        }

        // Used to notify an AI about a combat situation
        public void OnCombatSituation(Stimulus stimulus, Vector3 lastKnownPosition,
            Situation situation)
        {
            if (aiController.Target != null)
                return; // already in combat

            CurrentSituation = situation;
            ActiveStimulus = stimulus;
            awarenessSystem.PauseDecay = true;

            awarenessSystem.SetAwareness(1f);
            IdentifiedPlayerAsThreat = true;

            // Set the target so the AI enters combat states
            if (stimulus.source != null)
            {
                TrySetTarget(stimulus.source.transform);
            }
        }

        public void OnTrespassingSituation(Situation situation, bool follow)
        {
            if (aiController.Target != null) return;

            CurrentSituation = situation;
            ActiveStimulus = situation.OriginStimulus;
            awarenessSystem.PauseDecay = true;

            SetTrespassingKeys(ActiveStimulus.source.transform, follow);
        }

        /// <summary>
        /// Called by StealthAIManager when combat downgrades to investigation.
        /// Clears combat state so the AI can transition to investigation behavior.
        /// </summary>
        public void OnCombatDowngrade()
        {
            ClearTarget();
            awarenessSystem.SetAwareness(0f);
        }

        public void OnSituationEnded(Situation situation)
        {
            if (CurrentSituation != situation) return;

            CurrentSituation = null;
            awarenessSystem.PauseDecay = false;

            ClearSituationKeys();

            if (ActiveStimulus != null && ActiveStimulus.modality == StimulusModality.Visual)
                StealthAIManager.Instance.ResolveStimulus(ActiveStimulus);

            ActiveStimulus = null;
            IdentifiedPlayerAsThreat = false;
        }

        // ── Blackboard helpers ──────────────────────────────────────────

        void SetInvestigating(Vector3 position, bool restart)
        {
            bool isAlertedInvestigation = CurrentSituation != null && CurrentSituation.Type >= SituationType.AlertInvestigation;

            if (restart)
                aiController.Blackboard.Set(StealthBlackboardKeys.RestartInvestigation, true);

            aiController.Blackboard.Set(StealthBlackboardKeys.Investigate, true);
            aiController.Blackboard.Set(StealthBlackboardKeys.InvestigatePosition, position);
            aiController.Blackboard.Set(StealthBlackboardKeys.IsAlertedInvestigation, isAlertedInvestigation);
        }

        public void ClearSituationKeys()
        {
            aiController.Blackboard.Set(StealthBlackboardKeys.Investigate, false);
            aiController.Blackboard.Set(StealthBlackboardKeys.IsAlertedInvestigation, false);
            aiController.Blackboard.Set(StealthBlackboardKeys.RestartInvestigation, false);
            aiController.Blackboard.Set(StealthBlackboardKeys.MoveToInvestigatePos, false);
            aiController.Blackboard.Remove(StealthBlackboardKeys.WarnTarget);
            aiController.Blackboard.Set(StealthBlackboardKeys.FollowWhileWarning, false);

            ReleaseSearchPosition();
        }

        void ReleaseSearchPosition()
        {
            if (aiController.Blackboard.TryGet<Vector3>(StealthBlackboardKeys.SearchPosition, out var pos))
            {
                SearchPositionManager.Release(pos);
                aiController.Blackboard.Remove(StealthBlackboardKeys.SearchPosition);
            }
        }

        // Followers (the situation's investigators) approach and follow the trespasser;
        // the rest face them from where they stand
        void SetTrespassingKeys(Transform target, bool follow)
        {
            aiController.Blackboard.Set(StealthBlackboardKeys.WarnTarget, target);
            aiController.Blackboard.Set(StealthBlackboardKeys.FollowWhileWarning, follow);
        }

        void SetHasVisualOnTarget(bool hasVisual)
        {
            aiController.Blackboard.Set(AIBlackboardKeys.HasVisualOnTarget, hasVisual);
        }

        bool GetHasVisualOnTarget()
        {
            aiController.Blackboard.TryGet<bool>(AIBlackboardKeys.HasVisualOnTarget, out bool hasVisual);
            return hasVisual;
        }

        bool identifiedPlayerAsThreat;
        public bool IdentifiedPlayerAsThreat
        {
            get => identifiedPlayerAsThreat;
            private set
            {
                identifiedPlayerAsThreat = value;
                aiController.Blackboard.Set(StealthBlackboardKeys.IdentifiedPlayerAsThreat, value);
            }
        }

        public DialogueBarkSet GetBarkSetForSituation(Situation situation)
        {
            return situation.Type switch
            {
                SituationType.SuspiciousInvestigation => suspiciousInvestigationBarkSet,
                SituationType.AlertInvestigation => alertInvestigationBarkSet,
                SituationType.TrespassingRestrictedArea => trespassingBarkSet,
                SituationType.Combat => combatBarkSet,
                _ => null
            };
        }

        public Vector3 EyePosition => transform.position + Vector3.up * aiVision.EyeHeight;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (ActiveStimulus != null)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(ActiveStimulus.position + Vector3.up * 0.25f, 1.0f);

                UnityEditor.Handles.Label(ActiveStimulus.position + Vector3.up * 1.5f,
                    $"ActiveStimulus: {ActiveStimulus.priority}");
            }

        }
#endif
    }
}
