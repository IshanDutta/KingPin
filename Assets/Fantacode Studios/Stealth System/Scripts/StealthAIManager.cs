using System;
using System.Collections.Generic;
using System.Linq;
using FS_Core;
using FS_Util;
using UnityEngine;

namespace FS_StealthSystem
{
    /// <summary>
    /// ============================================================================================
    /// STEALTH SYSTEM — FULL DETECTION-TO-STATE-MACHINE FLOW
    /// ============================================================================================
    ///
    /// 1. SENSOR LAYER (per-AI)
    /// ────────────────────────
    ///   • AIVision (FixedUpdate) — Two procedural mesh volumes (primary + peripheral) with trigger
    ///     colliders collect VisualStimulus components. CheckVision() picks the highest-priority
    ///     stimulus across both zones, performs a line-of-sight raycast, and checks concealment
    ///     (HidingArea volumes and ConcealmentHotspot occupancy via VisualStimulus.IsConcealed).
    ///     Returns a VisionInfo (zone, stimulus, isHidden).
    ///
    ///   • AIHearing — StealthSoundHandler broadcasts GameSoundEvents via Physics.OverlapSphere.
    ///     Each AIHearing in range computes distance attenuation; if strength > minHearingThreshold,
    ///     it forwards a Stimulus to StealthAIController.HandleAudio().
    ///
    ///   • Damage — When an external system (e.g. FighterCore) calls TargetHandler.SetTarget(),
    ///     StealthAIController.OnTargetChanged() synthesises a Critical-priority Damage stimulus.
    ///
    ///   All detections are wrapped in a Stimulus { modality, priority, position, source }.
    ///
    /// 2. AWARENESS ESCALATION (StealthAIController, per-AI)
    /// ──────────────────────────────────────────────────────
    ///   HandleVision(VisionInfo) / HandleAudio(Stimulus):
    ///     a) CanPursueStimulus() — rejects stimuli that are lower priority than the AI's current
    ///        situation (Combat only accepts Critical; AlertInvestigation only accepts >= current).
    ///     b) For visual Player stimuli → HandlePlayer():
    ///        - Checks SuspicionProfile (RestrictedArea / HostileArea / WeaponSuspicionFactor).
    ///          If no profile, not suspicious, player unknown → decay awareness, return.
    ///        - If player hidden in a HidingArea or ConcealmentHotspot → track but don't increase awareness.
    ///        - Otherwise → AwarenessSystem.UpdateAwarenessFromVision(visionInfo, multiplier).
    ///          AwarenessSystem computes the rate from the vision zone, distance, motion and light:
    ///              Primary vision:    0.80 × distanceMultiplier × motionMultiplier × visibility × dt
    ///              Peripheral vision: 0.20 × distanceMultiplier × motionMultiplier × visibility × dt
    ///              Distance multiplier: ≤3m = 10×, 3-6m = lerp(2×,1×), >6m = 1×
    ///              Motion multiplier: observed stimulus speed ≤2.5m/s = 1×, ramps to 4× at 6m/s
    ///              Light visibility (LightVision, 1 when unused): 1 = full gain, 0 = full decay
    ///          The controller passes alertedInvestigationMultiplier when on high alert.
    ///        - When awareness reaches 1.0 (IsAware):
    ///              • SuspicionProfile.ShouldWarn → Chase blackboard keys + Trespassing situation
    ///              • Otherwise → set target, IdentifiedPlayerAsThreat = true, report Combat situation
    ///     c) For visual AlertObject/SuspiciousObject → create investigation situation.
    ///     d) For audio stimuli → create investigation situation at sound position
    ///        (footsteps heard while the player is visible bump awareness instead).
    ///     e) On losing LOS to a known player → record LastSeenTargetPos, set HasVisualOnTarget = false.
    ///     f) When no stimulus detected → decay awareness over time (decayRate default 0.15/s).
    ///
    /// 3. SITUATION SYSTEM (StealthAIManager, global)
    /// ───────────────────────────────────────────────
    ///   Situations are shared investigation/combat contexts that coordinate multiple AI agents.
    ///
    ///   Types (escalation hierarchy):
    ///     0 = NoSituation
    ///     1 = SuspiciousInvestigation
    ///     2 = TrespassingRestrictedArea
    ///     3 = AlertInvestigation
    ///     4 = Combat (no timeout, persists while any AI spots the player)
    ///   Durations, dialogue cooldowns and investigator caps are tuned per type in the
    ///   StealthAIManager inspector (Situation Configs).
    ///
    ///   CreateOrJoinSituation(type, reporter, stimulus, position):
    ///     a) If reporter already in a situation → escalate (if higher type) or redirect position.
    ///     b) Else merge with nearby situation within 20m (escalating or redirecting it by
    ///        the same rules as (a)), or create new.
    ///     c) Reporter becomes Leader.
    ///     d) AlertNearbyAIs: inner sphere (15m, unconditional) + outer sphere (40m, LOS required)
    ///        → all alerted AI join as participants. Then ReassignInvestigators picks the
    ///        participants closest to the stimulus (by navmesh path) as investigators, up to
    ///        the per-type cap — full re-pick during the creation frame (all simultaneous
    ///        hearers); afterwards incumbents keep the role unless a candidate is closer by
    ///        more than investigatorSwapDistance (or a slot is vacant).
    ///     e) NotifyAIOfSituation: clears the participant's situation keys, then dispatches
    ///        OnCombatSituation / OnInvestigateSituation(isInvestigator) / OnTrespassingSituation —
    ///        the controller callbacks own all Blackboard key writes
    ///
    ///   EscalateSituation: upgrades type, resets dialogue, re-assigns investigators, re-notifies.
    ///
    ///   UpdateSituations (per frame):
    ///     - Combat ends when spottingAI.Count == 0 → downgrade to AlertInvestigation at
    ///       GlobalLastKnownPlayerPos.
    ///     - Others end when duration exceeded → EndSituation → OnSituationEnded on all
    ///       participants → clear investigation Blackboard keys.
    ///
    ///   Dialogue: Leader speaks initial bark, then periodic ongoing barks from random participants,
    ///     with phase tags (initial/ongoing/arrived/ended) + modality tags (sound/visual/damage).
    ///
    /// 4. BLACKBOARD KEYS → STATE MACHINE
    /// ────────────────────────────────────
    ///   Key                     Type      Set When
    ///   ─────────────────────── ───────── ──────────────────────────────────────────────
    ///   Investigate             bool      Investigation situation started/restarted
    ///   InvestigatePosition     Vector3   Stimulus position to investigate
    ///   IsAlertedInvestigation  bool      AlertInvestigation (vs Suspicious)
    ///   MoveToInvestigatePos    bool      AI is assigned investigator (moves to position)
    ///   RestartInvestigation    bool      New stimulus during existing investigation
    ///   SearchPosition          Vector3   FindSearchPointState picks a hidden search point (absent = none found)
    ///   IdentifiedPlayerAsThreat bool      AI identified player as hostile (engage on sight)
    ///   HasVisualOnTarget       bool      AI currently has line of sight to target
    ///   (Combat end is handled by the situation system, not a blackboard key)
    ///
    ///   All situation keys are written by StealthAIController — the manager only decides
    ///   who gets notified and with what role. States write only the keys they produce
    ///   (e.g. SearchPosition).
    ///
    ///   The AI StateMachine reads these keys via BTBoolCondition / BTFloatCondition on
    ///   StateTransitionAssets to drive transitions between patrol, investigation, warning,
    ///   and combat states (AlertedIdleState, FindSearchPointState, ResolveStimulusState, etc.).
    ///
    /// 5. EXECUTION ORDER (per frame)
    /// ──────────────────────────────
    ///   AIVision.FixedUpdate → CheckVision → StealthAIController.HandleVision
    ///   StealthSoundHandler → OverlapSphere → AIHearing.Hear → StealthAIController.HandleAudio
    ///   StealthAIController → AwarenessSystem.UpdateAwarenessFromVision/DecayAwareness → CreateOrJoinSituation
    ///   StealthAIManager.Update → UpdateSituations (timeout/escalation) + UpdateSituationDialogue
    ///   AIController.Update → StateMachine.Tick → reads Blackboard keys → executes states
    /// ============================================================================================
    /// </summary>
    /// 
    [RequireComponent(typeof(AIManager))]
    public class StealthAIManager : MonoBehaviour
    {
        [Header("Alert Radii")]
        [Tooltip("Radius in meters around the stimulus in which AIs are alerted unconditionally.")]
        [SerializeField] float innerAlertRadius = 15f;
        [Tooltip("Radius in meters in which AIs are alerted only if they have line of sight to the reporter.")]
        [SerializeField] float outerAlertRadius = 40f;

        [Header("Investigator Assignment")]
        [Tooltip("A non-investigator takes over an investigation only when its path to the stimulus is this many meters shorter than the current investigator's — large enough to justify stopping them mid-walk")]
        [SerializeField] float investigatorSwapDistance = 5f;

        [Header("Proximity Propagation")]
        [Tooltip("Radius in meters around each participant within which the alert spreads to other AIs on each propagation pass.")]
        [SerializeField] float propagationRadius = 10f;
        [Tooltip("Seconds between periodic alert propagation passes.")]
        [SerializeField] float propagationInterval = 0.75f;
        float lastPropagationTime;

        [Header("Situation Configs")]
        [Tooltip("Duration, dialogue and investigator settings for suspicious investigations.")]
        [SerializeField] SituationConfig suspiciousInvestigationConfig = new(15f, 10f, false, 1);
        [Tooltip("Duration, dialogue and investigator settings for restricted-area trespassing situations.")]
        [SerializeField] SituationConfig trespassingConfig = new(16f, 6f, false, 2, leaderSpeaksOnly: true);
        [Tooltip("Duration, dialogue and investigator settings for alert investigations.")]
        [SerializeField] SituationConfig alertInvestigationConfig = new(60f, 20f, false, 3);

        [Tooltip("Minimum seconds after losing LOS before combat can downgrade to investigation. This prevent downgrading if LOS blocked for a single frame while the target is right next to the AI.")]
        [SerializeField] float combatDowngradeDelay = 3f;

        [Header("Debug")]
        [Tooltip("Show the active situations on screen while playing in the editor.")]
        [SerializeField] bool showSituationDebugInfo = false;

        // Combat has nothing to tune, so made it static
        static readonly SituationConfig combatConfig = new(0f, 0f, true);

        SituationConfig GetConfig(SituationType type) => type switch
        {
            SituationType.TrespassingRestrictedArea => trespassingConfig,
            SituationType.AlertInvestigation => alertInvestigationConfig,
            SituationType.Combat => combatConfig,
            _ => suspiciousInvestigationConfig,
        };

        public Stimulus GlobalStimulus { get; private set; }

        public Vector3 GlobalLastKnownPlayerPos
        {
            get => AIManager.Instance.LastKnownTargetPos;
            set => AIManager.Instance.LastKnownTargetPos = value;
        }

        /// <summary>
        /// The highest-priority situation type currently active across all situations.
        /// NoSituation when there are no active situations.
        /// </summary>
        public SituationType HighestSituationType { get; private set; } = SituationType.NoSituation;

        // All AI registered in the scene
        readonly HashSet<StealthAIController> allAI = new();
        // Hearing-layer collider → controller lookup (avoids GetComponentInParent per overlap hit)
        readonly Dictionary<Collider, StealthAIController> colliderToAI = new();

        // AI that currently see the player
        readonly HashSet<StealthAIController> spottingAI = new();

        readonly List<Situation> activeSituations = new();

        // Reusable collections for PropagateAlerts (avoid allocation)
        readonly List<StealthAIController> participantSnapshot = new();
        readonly HashSet<StealthAIController> propagationAlerted = new();

        // Reusable collections for ReassignInvestigators (avoid allocation)
        readonly List<StealthAIController> rankedParticipants = new();
        readonly Dictionary<StealthAIController, float> participantScores = new();
        readonly HashSet<StealthAIController> previousInvestigators = new();
        Collider[] sphereResults = new Collider[64];
        const float SituationMergeRadius = 20f;

        /// <summary>
        /// Fired when the highest-priority situation type changes (e.g., for music/UI).
        /// </summary>
        public event Action<SituationType> OnHighestSituationChanged;

        public static StealthAIManager Instance { get; private set; }
        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            UpdateSituations();
            PropagateAlerts();
            UpdateSituationDialogue();
        }

        public void RegisterAI(StealthAIController ai)
        {
            if (ai == null) return;
            allAI.Add(ai);

            var col = ai.GetComponent<Collider>();
            if (col != null)
                colliderToAI[col] = ai;
        }

        public void UnregisterAI(StealthAIController ai)
        {
            if (ai == null) return;

            allAI.Remove(ai);
            spottingAI.Remove(ai);

            var col = ai.GetComponent<Collider>();
            if (col != null)
                colliderToAI.Remove(col);

            // Remove from any active situations
            for (int i = activeSituations.Count - 1; i >= 0; i--)
            {
                var sit = activeSituations[i];
                if (!sit.HasParticipant(ai)) continue;

                sit.RemoveParticipant(ai);

                if (sit.Participants.Count == 0)
                {
                    EndSituation(sit);
                    continue;
                }
            }
        }

        // ── Situation lifecycle ──────────────────────────────────────────

        public Situation CreateOrJoinSituation(SituationType type, Vector3 position,
            Stimulus stimulus, StealthAIController reporter)
        {
            // Stimulus positions can be off-mesh so map to the navmesh
            position = NavMeshUtil.GetNearestReachablePosition(reporter.transform.position, position);

            // If the reporter is already in a situation, escalate or redirect it
            var reporterSituation = reporter.CurrentSituation;
            if (reporterSituation != null && reporterSituation.IsActive)
            {
                if (type > reporterSituation.Type)
                    EscalateSituation(reporterSituation, type, position, stimulus, reporter);
                else if (CanRedirectSituation(reporterSituation, type, stimulus))
                    ChangeSituationStimulus(reporterSituation, position, stimulus, reporter);
                else
                    return reporterSituation; // Lower-priority stimulus while still investigating — ignore

                AlertNearbyAIs(position, reporter, reporterSituation);
                ReassignInvestigators(reporterSituation);
                return reporterSituation;
            }

            // Try to merge with an existing nearby situation
            Situation existing = FindMergeableSituation(type, position);

            if (existing != null)
            {
                if (type > existing.Type)
                    EscalateSituation(existing, type, position, stimulus, reporter);
                else if (CanRedirectSituation(existing, type, stimulus))
                    ChangeSituationStimulus(existing, position, stimulus, reporter);

                existing.AddParticipant(reporter);
                reporter.CurrentSituation = existing;

                NotifyAIOfSituation(reporter, existing);
                AlertNearbyAIs(position, reporter, existing);
                ReassignInvestigators(existing);
                return existing;
            }

            // Create new situation
            var situation = new Situation
            {
                Type = type,
                Config = GetConfig(type),
                Position = position,
                OriginStimulus = stimulus,
                SuspicionProfile = stimulus?.source != null? stimulus.source.GetComponent<SuspicionProfile>() : null,
                Leader = reporter,
                CreatedTime = Time.time,
                CreatedFrame = Time.frameCount,
                LastUpdatedTime = Time.time,
                IsActive = true,
            };
            situation.AddParticipant(reporter);
            reporter.CurrentSituation = situation;
            activeSituations.Add(situation);

            NotifyAIOfSituation(reporter, situation);
            AlertNearbyAIs(position, reporter, situation);
            ReassignInvestigators(situation);
            TrySpeak(situation, reporter, BarkPhase.Initial);
            UpdateHighestSituation();
            return situation;
        }

        /// <summary>
        /// Whether a new stimulus should become the situation's lead (keeping the situation's
        /// type — e.g. AlertInvestigation stays alert). A same-type stimulus with equal-or-higher
        /// priority always redirects; once the current lead has been checked out, any new
        /// same-type or investigation stimulus redirects.
        /// </summary>
        bool CanRedirectSituation(Situation situation, SituationType type, Stimulus newStimulus)
        {
            // Already the current lead — one sound event produces a single Stimulus instance
            // shared by all hearers, so every extra hearer reports the same stimulus
            if (newStimulus == situation.OriginStimulus)
                return false;

            // If the new stimulus is visual and it's source is same as the current stimulus, then ignore it. 
            // In case of audio stimuli, every new stimulus (like a footstep) should be considered as a new lead
            // Combat is exempt: its spotted-reports intentionally redirect every report to keep the situation fresh.
            if (situation.Type != SituationType.Combat
                && newStimulus.modality == StimulusModality.Visual
                && newStimulus.source != null && situation.OriginStimulus != null
                && newStimulus.source == situation.OriginStimulus.source
                && newStimulus.modality == situation.OriginStimulus.modality)
                return false;

            // If type priority is same or above, then redirect
            if (type == situation.Type && newStimulus.TypePriority >= situation.OriginStimulus.TypePriority)
                return true;

            // If the old stimulus was already reached, an investigation situation should
            // redirect even if the new situation type is lower.
            return situation.StimulusReached
                && (type == situation.Type || situation.IsInvestigationSituation());
        }

        Situation FindMergeableSituation(SituationType type, Vector3 position)
        {
            for (int i = 0; i < activeSituations.Count; i++)
            {
                var sit = activeSituations[i];
                if (!sit.IsActive) continue;

                // Combat situations always merge with other combat situations
                if (type == SituationType.Combat && sit.Type == SituationType.Combat)
                    return sit;

                if (Vector3.Distance(sit.Position, position) <= SituationMergeRadius)
                    return sit;
            }
            return null;
        }

        void ChangeSituationStimulus(Situation situation, Vector3 newPosition, Stimulus newStimulus,
            StealthAIController reporter)
        {
            situation.Position = newPosition;
            situation.OriginStimulus = newStimulus;
            situation.LastUpdatedTime = Time.time;
            situation.StimulusReached = false;
            situation.SuspicionProfile = newStimulus?.source != null ? newStimulus.source.GetComponent<SuspicionProfile>() : null;

            // The new position may put a candidate far closer than the current investigator
            ReassignInvestigators(situation, notifyChanged: false);

            foreach (var ai in situation.Participants)
                NotifyAIOfSituation(ai, situation, restart: true);
        }

        public void EscalateSituation(Situation situation, SituationType newType,
            Vector3 newPosition, Stimulus newStimulus, StealthAIController reporter = null)
        {
            if (situation.Leader != null)
                newPosition = NavMeshUtil.GetNearestReachablePosition(situation.Leader.transform.position, newPosition);

            situation.Type = newType;
            situation.Config = GetConfig(newType);
            situation.Position = newPosition;
            situation.OriginStimulus = newStimulus;
            situation.LastUpdatedTime = Time.time;
            situation.StimulusReached = false;
            situation.SuspicionProfile = newStimulus?.source != null ? newStimulus.source.GetComponent<SuspicionProfile>() : null;

            // Interrupt any playing barks from the previous situation type
            foreach (var ai in situation.Participants)
            {
                var audioSource = ai.AIController.AudioSource;
                if (audioSource != null && audioSource.isPlaying)
                    audioSource.Stop();
            }

            // New config may change the investigator cap — recompute (all re-notified below)
            ReassignInvestigators(situation, notifyChanged: false);

            // Re-notify all current participants of the escalation
            foreach (var ai in situation.Participants)
                NotifyAIOfSituation(ai, situation, true);

            // The AI that triggered the escalation speaks (e.g. the one who spotted the
            // player); situations escalated without a reporter fall back to the leader
            TrySpeak(situation, reporter ?? situation.Leader, BarkPhase.Initial);
            UpdateHighestSituation();
        }

        void UpdateSituations()
        {
            for (int i = activeSituations.Count - 1; i >= 0; i--)
            {
                var sit = activeSituations[i];
                if (!sit.IsActive) continue;

                if (sit.Type == SituationType.Combat)
                {
                    if (spottingAI.Count > 0)
                    {
                        sit.LastUpdatedTime = Time.time;
                        sit.LastSpottedTime = Time.time;
                        sit.StimulusReached = false; // reset while someone has LOS
                    }
                    else if (!sit.StimulusReached || Time.time - sit.LastSpottedTime < combatDowngradeDelay)
                    {
                        // No spotters — wait until someone reaches the last seen position
                        // and the downgrade delay has passed since LOS was lost
                    }
                    else
                    {
                        // Reached last seen position, player not found — downgrade
                        if (GlobalStimulus != null)
                            GlobalStimulus.position = GlobalLastKnownPlayerPos;

                        foreach (var ai in sit.Participants)
                            ai.OnCombatDowngrade();

                        EscalateSituation(sit, SituationType.AlertInvestigation, GlobalLastKnownPlayerPos, GlobalStimulus);
                    }
                }
                else if (sit.Type == SituationType.TrespassingRestrictedArea)
                {
                    // Player left restricted area — end situation
                    if (sit.SuspicionProfile != null && !sit.SuspicionProfile.HasActiveWarnFactor)
                    {
                        EndSituation(sit);
                    }
                    // Warning period expired — escalate to combat
                    else if (Time.time - sit.CreatedTime >= sit.Config.Duration)
                    {
                        if (sit.OriginStimulus?.source != null)
                            sit.OriginStimulus.position = sit.OriginStimulus.source.transform.position;

                        ReportPlayerSpotted(sit.Leader, sit.OriginStimulus);
                    }
                }
                else if (sit.IsInvestigationSituation())
                {
                    if (sit.StimulusReached && Time.time - sit.LastUpdatedTime >= sit.Config.Duration)
                        EndSituation(sit);
                }
            }
        }

        public void EndSituation(Situation situation)
        {
            // Play a dismissal bark
            if (situation.Participants.Count > 0)
            {
                var speaker = situation.Config.LeaderSpeaksOnly ? situation.Leader : PickRandomParticipant(situation);
                TrySpeak(situation, speaker, BarkPhase.Ended);
            }

            situation.IsActive = false;

            // Copy participants list since OnSituationEnded may modify it
            var participants = situation.Participants.ToArray();
            foreach (var ai in participants)
                ai.OnSituationEnded(situation);

            activeSituations.Remove(situation);
            UpdateHighestSituation();
        }

        void UpdateHighestSituation()
        {
            var highest = SituationType.NoSituation;
            for (int i = 0; i < activeSituations.Count; i++)
            {
                if (activeSituations[i].IsActive && activeSituations[i].Type > highest)
                    highest = activeSituations[i].Type;
            }

            if (HighestSituationType != highest)
            {
                HighestSituationType = highest;
                OnHighestSituationChanged?.Invoke(highest);
            }
        }

        StealthAIController PickRandomParticipant(Situation situation)
        {
            if (situation.Participants.Count == 0) return null;
            int index = UnityEngine.Random.Range(0, situation.Participants.Count);
            return situation.Participants.ElementAt(index);
        }

        void AlertNearbyAIs(Vector3 position, StealthAIController reporter,
            Situation situation)
        {
            // No need to alert other AI's for suspicios investigation
            if (situation.Type == SituationType.NoSituation || situation.Type == SituationType.SuspiciousInvestigation)
                return;

            int reporterLayer = 1 << reporter.gameObject.layer;

            // Inner sphere — alert unconditionally
            int innerCount = Physics.OverlapSphereNonAlloc(position, innerAlertRadius,
                sphereResults, reporterLayer, QueryTriggerInteraction.Collide);

            HashSet<StealthAIController> alerted = new();
            for (int i = 0; i < innerCount; i++)
            {
                if (!colliderToAI.TryGetValue(sphereResults[i], out var ai)) continue;
                TryAlertAI(ai, reporter, situation, alerted);
            }

            // Outer sphere — alert only with LOS to reporter
            int outerCount = Physics.OverlapSphereNonAlloc(position, outerAlertRadius,
                sphereResults, reporterLayer, QueryTriggerInteraction.Collide);

            for (int i = 0; i < outerCount; i++)
            {
                if (!colliderToAI.TryGetValue(sphereResults[i], out var ai)) continue;
                if (ai == reporter) continue;
                if (alerted.Contains(ai)) continue;

                if (AIUtil.HasLineOfSight(ai.EyePosition, reporter.EyePosition))
                    TryAlertAI(ai, reporter, situation, alerted);
            }
        }

        bool TryAlertAI(StealthAIController ai, StealthAIController reporter, Situation situation,
            HashSet<StealthAIController> alerted)
        {
            if (ai == null || ai == reporter || !allAI.Contains(ai)) return false;

            // Already in an equal-or-higher situation — never demote
            if (ai.CurrentSituation != null && ai.CurrentSituation.Type >= situation.Type)
                return false;

            // Check if already alerted
            if (!alerted.Add(ai)) return false;

            // Leave the lower-priority situation cleanly before joining
            if (ai.CurrentSituation != null)
            {
                var oldSituation = ai.CurrentSituation;
                oldSituation.RemoveParticipant(ai);
                ai.OnSituationEnded(oldSituation);

                if (oldSituation.Participants.Count == 0)
                    EndSituation(oldSituation);
            }

            situation.AddParticipant(ai);
            ai.CurrentSituation = situation;
            NotifyAIOfSituation(ai, situation);
            return true;
        }

        /// <summary>
        /// Assigns the participants closest to the stimulus (by navmesh path) as investigators,
        /// up to the config cap. During the situation's creation frame the pick is a full
        /// re-rank — all simultaneous hearers of one sound join within that frame, so the
        /// closest of them wins regardless of who reported first. After that, incumbents keep
        /// their role: others only fill vacant slots, or take over when they're closer by more
        /// than investigatorSwapDistance — the margin keeps the investigator from flip-flopping
        /// mid-walk over minor differences. Selection policy lives only here — future
        /// eligibility rules (e.g. AI tags) plug into the ranking.
        /// </summary>
        void ReassignInvestigators(Situation situation, bool notifyChanged = true)
        {
            // Investigations and trespassing assign mover roles (investigate / follow);
            // combat coordinates through targets and has no roles to assign
            if (situation.Type == SituationType.Combat || situation.Type == SituationType.NoSituation)
                return;

            // At most half the participants investigate; the rest stay alert nearby
            int slots = Mathf.Min(situation.Config.MaxInvestigators, (situation.Participants.Count + 1) / 2);

            // Only run if we need more investigators or if the position of the stimulus changed above a threshold
            // Also, we should always run if it's called in the created frame, since we don't know the when the closest AI will report
            bool creationFrame = Time.frameCount == situation.CreatedFrame;
            bool mustRun = creationFrame
                || situation.Investigators.Count < slots
                || Vector3.Distance(situation.Position, situation.LastAssignPosition) > investigatorSwapDistance * 0.5f;
            if (!mustRun) return;

            previousInvestigators.Clear();
            previousInvestigators.UnionWith(situation.Investigators);

            // Rank everyone by path distance to the stimulus. Outside the creation frame, exsiting investigators get 
            // a head start of investigatorSwapDistance, a candidate must be closer than that to justify stopping the 
            // current investigator mid-walk, this keeps the role from flip-flopping over minor differences
            rankedParticipants.Clear();
            participantScores.Clear();
            foreach (var ai in situation.Participants)
            {
                float score;
                if (situation.Type == SituationType.TrespassingRestrictedArea && ai == situation.Leader)
                {
                    // The trespassing leader delivers the warnings, so he always confronts
                    score = float.MinValue;
                }
                else
                {
                    score = NavMeshUtil.GetNavMeshDistance(ai.transform.position, situation.Position);

                    if (!creationFrame && previousInvestigators.Contains(ai))
                        score -= investigatorSwapDistance;
                }

                rankedParticipants.Add(ai);
                participantScores[ai] = score;
            }
            rankedParticipants.Sort((a, b) => participantScores[a].CompareTo(participantScores[b]));

            situation.Investigators.Clear();
            for (int i = 0; i < rankedParticipants.Count && i < slots; i++)
                situation.Investigators.Add(rankedParticipants[i]);

            situation.LastAssignPosition = situation.Position;

            if (!notifyChanged) return;

            foreach (var ai in situation.Participants)
            {
                if (previousInvestigators.Contains(ai) != situation.Investigators.Contains(ai))
                    NotifyAIOfSituation(ai, situation);
            }
        }

        // ── Notify AI based on situation type ────────────────────────────

        void NotifyAIOfSituation(StealthAIController ai, Situation situation, bool restart = false)
        {
            ai.ClearSituationKeys();

            switch (situation.Type)
            {
                case SituationType.Combat:
                    ai.OnCombatSituation(situation.OriginStimulus, situation.Position, situation);
                    break;

                case SituationType.SuspiciousInvestigation:
                case SituationType.AlertInvestigation:
                    ai.OnInvestigateSituation(situation.OriginStimulus, situation, situation.IsInvestigator(ai), restart);
                    break;

                case SituationType.TrespassingRestrictedArea:
                    ai.OnTrespassingSituation(situation, follow: situation.IsInvestigator(ai));
                    break;
            }
        }

        
        /// <summary>
        /// It's not enough to alert the nearby AIs once at the start of the situation.
        /// PropagateAlerts will alert nearby AIs periodically (every propagationInterval)
        /// </summary>
        void PropagateAlerts()
        {
            if (Time.time - lastPropagationTime < propagationInterval)
                return;
            lastPropagationTime = Time.time;

            // Only propagate for the highest-priority active situation
            Situation situation = null;
            foreach (var sit in activeSituations)
            {
                if (sit.Type == SituationType.NoSituation ||
                    sit.Type == SituationType.SuspiciousInvestigation)
                    continue;
                if (situation == null || sit.Type > situation.Type)
                    situation = sit;
            }
            if (situation == null) return;

            // Snapshot participants (TryAlertAI modifies the set)
            participantSnapshot.Clear();
            participantSnapshot.AddRange(situation.Participants);

            propagationAlerted.Clear();
            propagationAlerted.UnionWith(participantSnapshot);

            foreach (var participant in participantSnapshot)
            {
                int participantLayer = 1 << participant.gameObject.layer;
                int count = Physics.OverlapSphereNonAlloc(participant.transform.position, propagationRadius,
                    sphereResults, participantLayer, QueryTriggerInteraction.Collide);

                for (int i = 0; i < count; i++)
                {
                    if (!colliderToAI.TryGetValue(sphereResults[i], out var ai)) continue;

                    TryAlertAI(ai, participant, situation, propagationAlerted);
                }
            }
        }

        // ── Report methods ───────────────────────────────────────────────

        public void ReportPlayerSpotted(StealthAIController reporter, Stimulus stimulus)
        {
            spottingAI.Add(reporter);

            GlobalLastKnownPlayerPos = stimulus.position;

            GlobalStimulus = new Stimulus()
            {
                modality = stimulus.modality,
                priority = stimulus.priority,
                importance = stimulus.importance,
                source = stimulus.source,
                position = stimulus.position,
            };

            CreateOrJoinSituation(SituationType.Combat, stimulus.position, GlobalStimulus, reporter);

            // Keep combat situation positions fresh so CheckStimulusReached (and the
            // downgrade that follows) triggers at the latest last-known position
            UpdateCombatLastKnownPositions(stimulus.position);
        }

        /// <summary>
        /// Called when a combat participant takes a close-range hit from the player. The hit is
        /// treated as fresh contact: the last known position snaps to just PAST where the attack
        /// came from and the downgrade timer restarts, so the AI keeps hunting instead of dropping
        /// to investigation right after being attacked.
        /// </summary>
        public void ReportHitByPlayer(StealthAIController reporter, Vector3 attackerPos)
        {
            var situation = reporter.CurrentSituation;
            if (situation == null || !situation.IsActive || situation.Type != SituationType.Combat)
                return;

            // While any AI has live sight of the player, vision owns the position — a hit-derived
            // overshoot would only drag the shared position away from the live-tracked truth
            if (spottingAI.Count > 0)
                return;

            // Anchor the position beyond the attack spot, not on it: so that the player can't just hit and then
            // hide behind a cover nearby immedeatly
            Vector3 toAttacker = attackerPos - reporter.transform.position;
            toAttacker.y = 0f;
            Vector3 approachDir = toAttacker.sqrMagnitude > 0.01f ? toAttacker.normalized : reporter.transform.forward;
            Vector3 sweepPos = attackerPos + approachDir * AIManager.Instance.StimulusOvershootDistance;
            sweepPos = NavMeshUtil.GetNearestReachablePosition(reporter.transform.position, sweepPos);

            GlobalLastKnownPlayerPos = sweepPos;
            if (GlobalStimulus != null)
                GlobalStimulus.position = sweepPos;

            situation.LastSpottedTime = Time.time;
            situation.LastUpdatedTime = Time.time;
            situation.StimulusReached = false;
            UpdateCombatLastKnownPositions(sweepPos);
        }

        void UpdateCombatLastKnownPositions(Vector3 lastKnownPos)
        {
            for (int i = 0; i < activeSituations.Count; i++)
            {
                var sit = activeSituations[i];
                if (!sit.IsActive || sit.Type != SituationType.Combat) continue;

                sit.Position = sit.Leader != null
                    ? NavMeshUtil.GetNearestReachablePosition(sit.Leader.transform.position, lastKnownPos)
                    : lastKnownPos;
            }
        }

        public void ReportPlayerLost(StealthAIController reporter)
        {
            spottingAI.Remove(reporter);
        }

        /// <summary>
        /// Called when an investigator reaches the stimulus source.
        /// Triggers a one-time "arrived" bark from the AI that reached it.
        /// </summary>
        public void OnStimulusReached(Situation situation, StealthAIController ai)
        {
            if (situation == null || situation.StimulusReached) return;
            situation.StimulusReached = true;
            situation.LastUpdatedTime = Time.time;

            TrySpeak(situation, ai, BarkPhase.Arrived);
        }

        /// <summary>
        /// Used to resolve a stimulus. Once resolved the AI will discard the same stimulus
        /// eg: AI will not react to the same dead body when it sees it again
        /// </summary>
        public void ResolveStimulus(Stimulus stimulus)
        {
            if (stimulus != null)
            {
                stimulus.resolved = true;

                if (stimulus.modality == StimulusModality.Visual && stimulus.source != null)
                {
                    var visualStimulus = stimulus.source.GetComponent<VisualStimulus>();
                    
                    if (visualStimulus.Type != VisualStimulusType.Player && visualStimulus.Type != VisualStimulusType.Threat)
                        visualStimulus.Resolved = true;
                }
            }
        }

        public bool CanPursueStimulus(StealthAIController ai, Stimulus stimulus)
        {
            var situation = ai.CurrentSituation;
            if (situation != null)
            {
                if (situation.Type == SituationType.Combat)
                    return stimulus.priority == StimulusPriority.Critical;

                if (situation.Type == SituationType.TrespassingRestrictedArea)
                    return stimulus.priority == StimulusPriority.Critical;

                if (situation.Type == SituationType.AlertInvestigation)
                {
                    if (stimulus.TypePriority >= situation.OriginStimulus.TypePriority)
                        return true;

                    // After reaching the stimulus, accept any audio (e.g. nearby footsteps)
                    // Before reaching it, only accept equal-or-higher priority
                    if (situation.StimulusReached && stimulus.modality == StimulusModality.Audio)
                        return true;

                    return IsStimulusResolved(situation.OriginStimulus);
                }
            }

            return true;
        }

        public bool IsStimulusResolved(Stimulus stimulus)
        {
            return stimulus != null && stimulus.resolved;
        }

        // ── Dialogue coordination ────────────────────────────────────────

        public enum BarkPhase { Initial, Ongoing, Ended, Arrived }

        static readonly List<string> tagBuffer = new List<string>(8);

        // Gets bark tags based on the situation and it's phase
        static List<string> GetBarkTags(Situation situation, BarkPhase phase)
        {
            tagBuffer.Clear();

            // Phase tag
            switch (phase)
            {
                case BarkPhase.Initial: tagBuffer.Add("initial"); break;
                case BarkPhase.Ongoing: tagBuffer.Add("ongoing"); break;
                case BarkPhase.Ended:   tagBuffer.Add("ended");   break;
                case BarkPhase.Arrived: tagBuffer.Add("arrived"); break;
            }

            if (situation.Type == SituationType.TrespassingRestrictedArea)
            {
                tagBuffer.Add($"warning_{situation.WarningCount+1}");
                return tagBuffer;
            }

            var stimulus = situation.OriginStimulus;
            if (stimulus == null) return tagBuffer;

            switch (stimulus.modality)
            {
                case StimulusModality.Audio:
                    tagBuffer.Add("sound");
                    tagBuffer.Add(stimulus.soundType.ToString().ToLower());
                    break;

                case StimulusModality.Visual:
                    tagBuffer.Add("visual");
                    if (stimulus.source != null)
                    {
                        var vs = stimulus.source.GetComponent<VisualStimulus>();
                        if (vs != null && !string.IsNullOrEmpty(vs.DialogueBarkTag))
                            tagBuffer.Add(vs.DialogueBarkTag);
                    }
                    break;

                case StimulusModality.Damage:
                    tagBuffer.Add("damage");
                    break;

                case StimulusModality.Touch:
                    tagBuffer.Add("touch");
                    break;
            }

            return tagBuffer;
        }

        void UpdateSituationDialogue()
        {
            float now = Time.time;

            for (int i = 0; i < activeSituations.Count; i++)
            {
                var sit = activeSituations[i];
                if (!sit.IsActive) continue;

                // One-shot situations only speak once (initial bark already played at creation)
                if (sit.Config.DialogueIsOneShot)
                    continue;

                // Cooldown check
                if (now - sit.LastDialogueTime < sit.Config.DialogueCooldown)
                    continue;

                // Investigation ongoing dialogue waits until an AI reaches the stimulus
                if (sit.IsInvestigationSituation() && !sit.StimulusReached)
                    continue;

                // Don't play ongoing dialogue if the situation is about to end
                float timeRemaining = sit.Config.Duration - (now - sit.LastUpdatedTime);
                if (timeRemaining < 3f)
                    continue;

                // Pick a random participant for periodic dialogue
                PlaySituationDialogue(sit);
            }
        }

        void PlaySituationDialogue(Situation situation)
        {
            var ai = situation.Config.LeaderSpeaksOnly ? situation.Leader : PickRandomParticipant(situation);
            if (ai != null)
                TrySpeak(situation, ai, BarkPhase.Ongoing);
        }

        bool TrySpeak(Situation situation, StealthAIController ai,
            BarkPhase phase)
        {
            if (ai == null) return false;

            var barkSet = ai.GetBarkSetForSituation(situation);
            if (barkSet == null) return false;

            var tags = GetBarkTags(situation, phase);
            var audio = barkSet.Pick(tags);
            if (audio == null) return false;

            situation.LastDialogueTime = Time.time;
            ai.AIController.Speak(audio);

            if (situation.Type == SituationType.TrespassingRestrictedArea)
                situation.WarningCount++;

            return true;
        }

#if UNITY_EDITOR
        private void OnGUI()
        {
            if (!showSituationDebugInfo) return;

            var style = new GUIStyle();
            style.fontSize = 20;
            GUILayout.Label("Highest Situation : " + HighestSituationType.ToString(), style);

            GUILayout.Label($"Active Situations : {activeSituations.Count}", style);
            foreach (var sit in activeSituations)
            {
                GUILayout.Label($"  {sit.Type} — {sit.Participants.Count} AI — Leader: {sit.Leader?.name}", style);
            }
        }

        private void OnDrawGizmos()
        {
            foreach (var sit in activeSituations)
            {
                if (!sit.IsActive) continue;

                Gizmos.color = sit.Type switch
                {
                    SituationType.Combat => Color.red,
                    SituationType.AlertInvestigation => Color.yellow,
                    SituationType.TrespassingRestrictedArea => new Color(1f, 0.5f, 0f),
                    _ => Color.cyan
                };

                Gizmos.DrawWireSphere(sit.Position + Vector3.up * 0.25f, 1.0f);

                UnityEditor.Handles.Label(
                    sit.Position + Vector3.up * 1.5f,
                    $"{sit.Type}\nParticipants: {sit.Participants.Count}\nLeader: {sit.Leader?.name}");
            }

            // Last known target position (shared across all AI)
            if (AIManager.Instance != null)
            {
                var lastSeenPos = AIManager.Instance.LastKnownTargetPos;
                Gizmos.color = Color.blue;
                Gizmos.DrawWireSphere(lastSeenPos + Vector3.up * 0.25f, 0.8f);

                UnityEditor.Handles.Label(lastSeenPos + Vector3.up * 2.5f, "LastSeenPos");
            }
        }
#endif
    }
}
