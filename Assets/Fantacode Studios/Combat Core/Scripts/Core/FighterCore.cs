using FS_Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using FS_Util;
using FS_LocomotionSystem;
using FS_InteractionSystem;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FS_Core
{
    public static partial class AnimatorParameters
    {
        public static int combatMode = Animator.StringToHash("combatMode");
        public static int isBlocking = Animator.StringToHash("IsBlocking");
    }
}

namespace FS_CombatCore {

    public enum FighterAction { None, Attacking, Blocking, Dodging, TakingHit, TakingBlockedHit, GettingUp, SwitchingWeapon, Taunt, Shooting, Reloading, Other } 
    public enum AttackType { Single, Combo, Stealth, GroundAttack }

    [RequireComponent(typeof(CombatTargetSensor))]
    public class FighterCore : MonoBehaviour, ITargetValidator
    {
        [Tooltip("Default weapon of the fighter")]
        public CombatWeapon defaultWeapon;
        [field: SerializeField] public float PreferredFightingRange { get; private set; } = 2f;

        [Tooltip("GameObjects in the target layer will be considered as a target")]
        public LayerMask targetLayer;

        [SerializeField] bool changeTargetWhenTakingHit = true;


        // Maximium range at which the fighter can perform an attack (Will be computed automatically)
        public float MaxAttackRange { get; set; }

        [Header("Optional Parameters")]
        [Space(5)]
        public DefaultReactions defaultAnimations = new DefaultReactions();

        [Tooltip("If true, replaces the default blood effects defined by attacks or weapon data.")]
        public bool overrideBloodEffects = false;
        [Tooltip("Blood effect prefab to be instantiated at the hit point when the fighter takes damage")]
        [ShowIf("overrideBloodEffects", true)]
        public GameObject bloodEffectPrefab;

        [Tooltip("Indicates if the fighter can dodge.")]
        [HideInInspector] public bool CanDodge;

        [HideInInspector] public DodgeData dodgeData;

        [Tooltip("If true, fighter will only be able to dodge in combat mode.")]
        [HideInInspector] public bool OnlyDodgeInCombatMode = true;

        [Tooltip("Indicates if the fighter can roll.")]
        [HideInInspector] public bool CanRoll;
        [HideInInspector] public DodgeData rollData;

        [Tooltip("If true, fighter will only be able to roll in combat mode.")]
        [HideInInspector] public bool OnlyRollInCombatMode = true;

        [Space(10)]

        [HideInInspector] public UnityEvent<CombatWeapon> OnWeaponEquipEvent;
        [HideInInspector] public UnityEvent<CombatWeapon> OnWeaponUnEquipEvent;

        public Action<CombatWeapon> OnWeaponEquipAction;
        public Action<CombatWeapon> OnWeaponUnEquipAction;

        public event Action OnResetFighter;


        public event Action<FighterCore> OnTargetSet;
        public event Action<FighterCore> OnTargetChanged;

        public event Action<FighterCore, Vector3, float, bool, HitType> OnGotHit;
        public Action OnGotHitCompleted;
        [HideInInspector] public UnityEvent<FighterCore, Vector3, float> OnGotHitEvent;

        [HideInInspector] public UnityEvent<FighterCore> OnAttackEvent;
        public Action OnStartAction;
        public Action OnEndAction;


        public event Action OnHitComplete;

        public event Action OnDeath;
        public event Action OnDeathAnimationCompleted;
        [HideInInspector] public UnityEvent OnDeathEvent;

        public event Action OnKnockDown;
        [HideInInspector] public UnityEvent OnKnockDownEvent;

        public event Action OnGettingUp;
        [HideInInspector] public UnityEvent OnGettingUpEvent;

        // States
        public FighterAction Action { get; private set; }
        public bool IsDead { get;  set; }
        public bool IsKnockedDown { get; private set; }
        public bool CanDoActions { get; private set; } = true;
        public bool InAction => Action != FighterAction.None && Action != FighterAction.Blocking && Action != FighterAction.Shooting;
        public bool IsBusy => InAction || IsDead || IsKnockedDown;
        public bool InInvinsibleAction => Action == FighterAction.Dodging || Action == FighterAction.GettingUp;
        public bool InAttackableAction => Action == FighterAction.None || Action == FighterAction.Blocking || Action == FighterAction.Attacking;
        // public bool UseRootMotion => Action == FighterAction.Attacking || Action == FighterAction.Dodging || Action == FighterAction.TakingHit || Action == FighterAction.TakingBlockedHit || Action == FighterAction.GettingUp;

        public FighterCore CurrAttacker { get; private set; }
        public FighterCore TargetOfAttack { get; set; }
        public Reaction CurrReaction { get; private set; }

        /// <summary>
        /// True if the most recent TakeHit will start a hit reaction (and therefore
        /// eventually fire OnGotHitCompleted / OnHitComplete). OnGotHit listeners that
        /// track hits with focus or state must ignore hits where this is false —
        /// no completion callback will ever come for them.
        /// </summary>
        public bool LastHitWillPlayReaction { get; private set; }

        /// <summary>
        /// True if the most recent TakeHit seizes the whole body — a non-additive hit reaction, or
        /// a death. False when the hit only layers an additive flinch over the current pose, or
        /// when its reaction was suppressed entirely. Systems that are animating the character
        /// themselves (hotspot interactions, for instance) use this to decide whether the hit
        /// forces them to abort or whether they can keep running underneath it.
        /// Set before OnGotHit fires, so it is valid inside those handlers.
        /// </summary>
        public bool LastHitTakesOverBody { get; private set; }


        public bool IsInSyncedAnimation => ActionExecutor.IsInSyncedAnimation;

        TargetHandler targetHandler;

        public ITargetable Targetable {get; private set;}
        
        FighterCore target;
        public FighterCore Target {
            get => target;
            set {
                var prev = target;
                target = value;
                if (target != prev)
                {
                    if (prev == null)
                    {
                        OnTargetSet?.Invoke(target);
                    }

                    OnTargetChanged?.Invoke(target);

                    Targetable = Target?.GetComponent<ITargetable>();

                    if (targetHandler != null)
                        targetHandler.SetTarget(value != null ? value.transform : null);
                }
            }
        }

        public bool IsBeingAttacked { get; private set; } = false;
        public bool PlayingBlockAnimationEarlier { get; set; } = false;     // For synced blocks, blocking animation should be played earlier

        int hitCount = 0;
        Coroutine additiveReactionCoroutine;


        bool isBlocking;
        public bool IsBlocking {
            get => isBlocking;
            set {
                bool wasPreviouslyBlocking = isBlocking;
                isBlocking = value;
                HandleBlockingChanged(wasPreviouslyBlocking);
            }
        }

        public CombatWeapon CurrentWeapon {
            get {
                return (itemEquipper?.EquippedItem is CombatWeapon meleeWeaponData)
                    ? meleeWeaponData
                    : null;
            }
        }

        public Animator animator { get; private set; }
        public AnimGraph animGraph {get; private set;}
        public ActionExecutor ActionExecutor { get; private set; }
        public LocomotionCore Locomotion { get; private set; }
        public CharacterController characterController {get; private set;}
        public ItemEquipper itemEquipper {get; private set;}
        public CapsuleCollider capsuleCollider {get; private set;}
        public Damagable Damagable {get; private set;}
        //public RagdollExecutor RagdollExecutor {get; private set;}
        public ActiveRagdollSystem ActiveRagdoll { get; private set; }
        public CombatTargetSensor CombatTargetSensor { get; private set; }
        IHitReactionSuppressor[] reactionSuppressors;

        // Null on a character that never interacts with hotspots. Whatever it was doing with one is
        // over the moment something seizes its body — see CancelInteraction.
        InteractionExecutor interactionExecutor;

        /// <summary>
        /// Fired right before the ragdoll impact sequence starts (explosion etc.).
        /// Use this on the player side (e.g. <see cref="ReactionHandler"/>) to focus an
        /// ActionSystemBase so other systems pause during the fall + get-up, without
        /// FighterCore itself reaching into player-only components.
        /// </summary>
        public event Action OnRagdollImpactStarted;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            animGraph = GetComponent<AnimGraph>();
            ActionExecutor = GetComponent<ActionExecutor>();
            Locomotion = GetComponent<LocomotionCore>();
            characterController = GetComponent<CharacterController>();
            capsuleCollider = GetComponent<CapsuleCollider>();
            itemEquipper = GetComponent<ItemEquipper>();
            Damagable = GetComponent<Damagable>();
            ActiveRagdoll = GetComponent<ActiveRagdollSystem>();
            targetHandler = GetComponent<TargetHandler>();

            CombatTargetSensor = GetComponent<CombatTargetSensor>();
            reactionSuppressors = GetComponents<IHitReactionSuppressor>();
            interactionExecutor = GetComponent<InteractionExecutor>();

            if(capsuleCollider  != null) colliderOriginalCenter = capsuleCollider.center;

            // Seed the saved default so ResetPreferredFigtingRangeToDefault() is safe even if it's
            // called before any UpdatePreferredFightingRange() override.
            defaultPreferredFightingRange = PreferredFightingRange;
        }

        private IEnumerator Start()
        {
            OnDeath += () =>
            {
                OnDeathEvent?.Invoke();
            };

            OnDeathAnimationCompleted += () => characterController.enabled = false;

            //RagdollExecutor.OnRagdollEnd += OnRagdollRecovery;

            if (ActiveRagdoll != null)
            {
                ActiveRagdoll.OnSettled += OnActiveRagdollSettled;
                ActiveRagdoll.OnRagdollStart += CancelInteraction;
            }

            if (Damagable != null)
                Damagable.OnImpactDamage += HandleImpactDamage;

            if (targetHandler != null)
            {
                targetHandler.OnTargetChanged += (Transform t) =>
                {
                    if (t == null) { Target = null; return; }
                    if (!IsTarget(t.gameObject)) return;
                    var fc = t.GetComponent<FighterCore>();
                    if (fc != null && !fc.IsDead)
                        Target = fc;
                };
            }

            // Only equip once all start functions are called
            yield return null;
            itemEquipper.EquipItem(defaultWeapon);
        }

        public IEnumerator Dodge(Vector3 dodgeDir, bool canTakeHit = false)
        {
            if (CanDodge)
            {
                var dodge = CurrentWeapon != null && CurrentWeapon.OverrideDodge ? CurrentWeapon.DodgeData : dodgeData;

                if (dodgeDir == Vector3.zero)
                    dodgeDir = dodge.GetDodgeDirection(transform, Target?.transform);

                AnimGraphClipInfo dodgeClip = dodge.GetClip(transform, dodgeDir);

                var dodgeAction = new CharacterAction()
                {
                    clipInfo = dodgeClip,
                    animationParameters = new AnimParameters()
                    {
                        onAnimationUpdate = (float normalizedTime, float time) =>
                        {
                            if (time <= dodgeClip.length * 0.8f && !dodge.useDifferentClipsForDirections)
                                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dodgeDir), 1000 * Time.deltaTime);
                        }
                    }
                };

                Damagable.CanTakeHit = canTakeHit;

                yield return ExecuteFighterAction(dodgeAction, FighterAction.Dodging);

                Damagable.CanTakeHit = true;
            }
        }

        public IEnumerator Roll(Vector3 rollDir, bool canTakeHit = false)
        {
            if (CanRoll)
            {
                var roll = CurrentWeapon != null && CurrentWeapon.OverrideRoll ? CurrentWeapon.RollData : rollData;

                if (rollDir == Vector3.zero)
                    rollDir = roll.GetDodgeDirection(transform, Target?.transform);

                AnimGraphClipInfo rollClip = roll.GetClip(transform, rollDir);

                var rollAction = new CharacterAction()
                {
                    clipInfo = rollClip,
                    animationParameters = new AnimParameters()
                    {
                        onAnimationUpdate = (float normalizedTime, float time) =>
                        {
                            if (time <= rollClip.length * 0.8f && !roll.useDifferentClipsForDirections)
                                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(rollDir), 1000 * Time.deltaTime);
                        }
                    }
                };

                Damagable.CanTakeHit = canTakeHit;

                yield return ExecuteFighterAction(rollAction, FighterAction.Dodging);

                Damagable.CanTakeHit = true;
            }
        }

        void HandleBlockingChanged(bool wasPreviouslyBlocking)
        {
            if (isBlocking && !wasPreviouslyBlocking)
            {
                SetAction(FighterAction.Blocking);
                animGraph.PlayLoopingAnimation(CurrentWeapon.Blocking, mask: Mask.Arm, transitionIn: .1f);
            }
            else if (!isBlocking && wasPreviouslyBlocking)
            {
                ResetActionToNone(FighterAction.Blocking);
                animGraph.StopLoopingAnimations(false);
            }
        }

        public void SetAction(FighterAction state)
        {
            if (Action != state)
            {
                Action = state;
            }

            if(state != FighterAction.None)
                itemEquipper.PreventItemSwitching = true;
        }

        public void ResetActionToNone(FighterAction stateToReset)
        {
            if (Action == stateToReset)
            {
                Action = FighterAction.None;
                itemEquipper.PreventItemSwitching =false;
            }
        }

        public bool IsTarget(GameObject gameObject)
        {
            return LayersUtil.LayerMaskContainsLayer(targetLayer, gameObject.layer);
        }

        public bool OnHitboxEntered(Collider other, BoxCollider weaponCollider, CombatHitData hitData)
        {
            // Ground attacks are allowed even when the target is in InInvinsibleAction is it's a KnockDown
            bool isGroundAttack = hitData.AttackType == AttackType.GroundAttack;
            bool invincible = InInvinsibleAction && !(isGroundAttack && IsKnockedDown && Action != FighterAction.GettingUp);
            if (other.tag == "Hitbox" && Damagable.CanTakeHit && !invincible)
            {
                var attacker = other.GetComponentInParent<FighterCore>();

                // Don't take hit during synced animation or knockdown reaction.
                if (IsInSyncedAnimation || (Action == FighterAction.TakingHit && CurrReaction != null && CurrReaction.willBeKnockedDown))
                    return false;

                // Ground / standing attack pairing:
                //   • Ground attack  → only while IsKnockedDown AND NOT during get-up.
                //                      Get-up phase is invincible to ground attacks because the target is already rising.
                //   • Standing attack → only while NOT IsKnockedDown.
                if (isGroundAttack)
                {
                    if (!IsKnockedDown || Action == FighterAction.GettingUp)
                        return false;
                }
                else
                {
                    if (IsKnockedDown)
                        return false;
                }

                // Don't take hit if the attacker has a SPECIFIC other target locked.
                if (attacker != null && attacker.TargetOfAttack != null
                    && attacker.TargetOfAttack != this && !hitData.CanHitMultipleTargets)
                    return false;

                if (PlayingBlockAnimationEarlier && Action == FighterAction.TakingBlockedHit)
                    return false;

                other.enabled = true;
                var hitPoint = IsBlocking ? other.ClosestPoint(weaponCollider != null ? weaponCollider.transform.position : transform.position) : other.ClosestPoint(transform.position);
                other.enabled = false;
                hitData.HitPoint = hitPoint;

                if (hitData.Reaction == null)
                {
                    if (hitData.HitDirection == HitDirections.FromCollision)
                        hitData.HitDirection = GetHitDirection(hitPoint);
                }

                TakeHit(hitData, attacker);
                return true;
            }
            return false;
        }

        public void TakeHit(CombatHitData hitData, FighterCore attacker = null)
        {
            if (IsDead || !Damagable.CanTakeHit) return;

            // We should not play reaction in cases where the reaction would corrupt the current animation
            // like Synced Animation, Getting up, Knockdown reaction in progress, etc.
            bool inReaction = Action == FighterAction.TakingHit || Action == FighterAction.TakingBlockedHit;
            bool suppressReaction = !hitData.IsFinisher &&
                ((IsKnockedDown && hitData.AttackType != AttackType.GroundAttack)
                || Action == FighterAction.GettingUp
                || (IsInSyncedAnimation && (attacker == null || !attacker.IsInSyncedAnimation))
                || (inReaction && CurrReaction != null && CurrReaction.willBeKnockedDown));

            if (suppressReaction)
                hitData.PlayReaction = false;

            if (hitData.WillBeBlocked == null)
                hitData.WillBeBlocked = isBlocking && !hitData.IsUnBlockableHit;

            var hitDamagable = (hitData.Damagable != null) ? hitData.Damagable : Damagable;

            var attackerTransform = attacker != null ? attacker.transform : null;

            if (!hitData.WillBeBlocked.Value)
            {
                bool damageAccepted;
                if (hitData.IsFinisher)
                    damageAccepted = Damagable.TakeDamage(Damagable.CurrentHealth, hitData.DamageType);
                else
                    damageAccepted = hitDamagable.TakeDamage(hitData.Damage, hitData.DamageType);

                // Immune to this weapon's damage type — no damage was applied, so skip the
                // reaction, target switching and hit events (otherwise an immune target still flinches).
                if (!damageAccepted) return;

                // If no reaction clip has been passed, then find a suitable reaction
                if (hitData.Reaction == null && hitData.PlayReaction)
                {
                    var weaponToUse = CurrentWeapon != null ? CurrentWeapon : defaultWeapon;
                    var reactionData = weaponToUse != null && weaponToUse.ReactionData != null && weaponToUse.ReactionData.HasReactions(hitData.HitType) ? weaponToUse.ReactionData : defaultAnimations.hitReactionData;
                    hitData.Reaction = ChooseHitReaction(reactionData?.reactions, hitData.HitDirection, hitData.ReactionTag, attackerTransform, hitData.HitType);
                }
            }
            else
            {
                float blockedDamagePercent = CurrentWeapon != null ? CurrentWeapon.BlockedDamage : 100f;

                // Immune to this weapon's damage type — skip the block reaction and hit events too.
                if (!hitDamagable.TakeDamage(hitData.Damage * (blockedDamagePercent / 100), hitData.DamageType))
                    return;

                if (hitData.Reaction == null && hitData.PlayReaction)
                {
                    var reactionData = CurrentWeapon != null && CurrentWeapon.ReactionData != null ? CurrentWeapon.BlockReactionData : defaultAnimations.blockedReactionData;
                    hitData.Reaction = ChooseHitReaction(reactionData?.reactions, hitData.HitDirection, hitData.ReactionTag, attackerTransform, hitData.HitType);
                }
            }
            
            // External systems an also supress reactions 
            // (eg. Parkour system can supress reaction when the character is hanging)
            bool isAdditiveReaction = hitData.Reaction?.isAdditiveAnimation ?? false;
            if (hitData.PlayReaction && !hitData.IsFinisher
                && reactionSuppressors.Any(s => s.SuppressHitReaction(isAdditiveReaction)))
            {
                hitData.PlayReaction = false;
                // Clear the discarded reaction so a lethal hit still takes the death-animation branch below 
                // (a leftover willBeKnockedDown reaction would silently skip it).
                hitData.Reaction = null;
            }

            bool wasStealthFinisher = Target == null && hitData.IsFinisher;
            if (!wasStealthFinisher && changeTargetWhenTakingHit && attacker != null && Target != attacker && IsTarget(attacker.gameObject))
                Target = attacker;

            // Only overwrite CurrReaction when this hit will actually play a reaction.
            LastHitWillPlayReaction = hitData.PlayReaction || hitData.IsFinisher;
            if (LastHitWillPlayReaction)
                CurrReaction = hitData.Reaction;

            // Does this hit seize the body, or can something else keep animating the character
            // underneath it? A full-body reaction and a death both take over; an additive flinch
            // layers on top, and a fully suppressed reaction plays nothing at all.
            LastHitTakesOverBody = Damagable.CurrentHealth <= 0
                                || (LastHitWillPlayReaction && !(hitData.Reaction?.isAdditiveAnimation ?? false));


            // A hit that seizes the body ends whatever hotspot interaction was under way — the
            // carried object leaves the hand, a half-entered hide spot is abandoned. Before OnGotHit,
            // so it is undone while the character is still in the pose the reaction replaces.
            if (LastHitTakesOverBody)
                CancelInteraction();

            OnGotHit?.Invoke(attacker, hitData.HitPoint, hitData.HittingTime, hitData.WillBeBlocked.Value, hitData.HitType);
            OnGotHitEvent?.Invoke(attacker, hitData.HitPoint, hitData.HittingTime);

            
            if (Damagable.CurrentHealth > 0 || (hitData.Reaction != null && (hitData.Reaction.willBeKnockedDown || hitData.IsFinisher)))
            {
                if (hitData.PlayReaction || hitData.IsFinisher)
                    StartCoroutine(PlayHitReaction(hitData.Reaction, hitData.WillBeBlocked.Value, isFinisher: hitData.IsFinisher, isAdditiveLayerAnimation: hitData.Reaction?.isAdditiveAnimation ?? false, 
                        waitTimeTillNextReaction : hitData.WaitTimeTillNextReaction, attackName: hitData.AttackName, attacker: attacker));
            }
            else
            {
                StartCoroutine(PlayDeathAnimation(hitData, attacker?.transform));
            }
        }

        IEnumerator PlayHitReaction(Reaction reaction = null, bool isBlockedReaction = false, AnimationClip getUpAnimation = null, bool isFinisher = false, 
            bool isAdditiveLayerAnimation = false,float waitTimeTillNextReaction = 0, string attackName = "", FighterCore attacker = null)
        {
            if (Action == FighterAction.TakingHit
                && animGraph.currentClipStateInfo?.currentClipInfo?.clip == reaction?.animationClipInfo?.clip
                && animGraph.currentClipStateInfo?.normalizedTime <= waitTimeTillNextReaction)
            {
                OnGotHitCompleted?.Invoke();
                yield break;
            }

            bool hasReactionAnimation = reaction?.animationClipInfo?.clip != null;

            // Additive reactions are pose-only overlays — they must not take part in
            // the action lifecycle (Action / CanDoActions / hitCount / OnStartAction).
            if (isAdditiveLayerAnimation)
            {
                if (hasReactionAnimation)
                {
                    // Play directly on AnimGraph so ActionExecutor.currentAction
                    // (locomotion / equipped system) remains in control.
                    if (additiveReactionCoroutine != null)
                        StopCoroutine(additiveReactionCoroutine);

                    additiveReactionCoroutine = StartCoroutine(animGraph.CrossfadeAsync(
                        clip: reaction.animationClipInfo.clip,
                        clipInfo: reaction.animationClipInfo,
                        isLoopingAnimation: false,
                        transitionBack: !IsDead,
                        mask: reaction.animationMask,
                        isAdditiveLayerAnimation: true,
                        overridableByAnimator: true));

                    float additiveClipLength = reaction.animationClipInfo.clip.length;
                    float elapsed = 0f;
                    while (elapsed < additiveClipLength && !IsDead)
                    {
                        elapsed += Time.deltaTime;
                        yield return null;
                    }

                    additiveReactionCoroutine = null;
                }

                OnGotHitCompleted?.Invoke();
                yield break;
            }

            // ── Non-additive reaction ──────────────────────────────────────────────
            // The action lifecycle (Action == TakingHit, CanDoActions = false, ++hitCount)
            // is acquired here and released in the finally below. Using try/finally is what
            // keeps the character from getting permanently stuck: ExecuteAsync can return
            // abnormally (taken over by another action, the playable graph throws, the
            // coroutine is stopped by death/knockdown/scene logic, or currentClipStateInfo
            // is corrupted so it never completes). Without the finally, --hitCount and the
            // Action reset would be skipped and the fighter would stay in TakingHit with
            // CanDoActions == false forever, and OnGotHitCompleted would never fire so any
            // focus holder (ReactionHandler / ShooterController) would never release.
            bool countedHit = false;
            bool willBeDead = false;

            if (hasReactionAnimation)
            {
                CanDoActions = false;
                SetAction(isBlockedReaction ? FighterAction.TakingBlockedHit : FighterAction.TakingHit);

                OnStartAction?.Invoke();
                ++hitCount;
                countedHit = true;

                if (reaction.rotateToAttacker && attacker != null)
                    RotateToAttacker(attacker.transform, reaction.rotationOffset);
            }

            try
            {
                // Death-on-hit (knockdown kill / finisher). Runs whether or not a reaction
                // clip exists, and before the reaction clip is built so transitionBack picks
                // up the resulting IsDead.
                if (Damagable.CurrentHealth == 0 && ((reaction != null && reaction.willBeKnockedDown) || isFinisher))
                {
                    Die();
                    willBeDead = true;
                    OnDeath?.Invoke();
                }

                if (hasReactionAnimation)
                {
                    Coroutine ragdollBlendCo = null;

                    var takeHitAction = new CharacterAction()
                    {
                        clipInfo = reaction.animationClipInfo,
                        ignoreCollision = reaction.ignoreCollision,
                        requiresLedgeBlocking = reaction.requiresLedgeBlocking,
                        isSyncedAnimation = isFinisher || (attacker != null && attacker.IsInSyncedAnimation),
                        animationParameters = new AnimParameters()
                        {
                            transitionBack = !IsDead && !reaction.willBeKnockedDown && !reaction.blendToRagdoll,
                            mask = reaction.animationMask,
                            onAnimationUpdate = reaction.blendToRagdoll ? (normalizedTime, time) =>
                            {
                                if (ragdollBlendCo == null && normalizedTime >= reaction.ragdollBlendStartTime)
                                    ragdollBlendCo = StartCoroutine(BlendToRagdoll(reaction));
                            } : null,
                        },
                    };

                    yield return ActionExecutor.ExecuteAsync(takeHitAction);

                    // If the character is knocked down, then play lying down and getting up animation
                    if (!IsDead && reaction.willBeKnockedDown && !IsKnockedDown)
                        StartCoroutine(GoToKnockedDownState(reaction));
                }
            }
            finally
            {
                if (countedHit)
                {
                    --hitCount;
                    if (hitCount < 0) hitCount = 0;

                    // Restore action state only once every overlapping reaction has finished.
                    if (hitCount == 0 && !IsDead)
                    {
                        CanDoActions = true;

                        OnHitComplete?.Invoke();

                        if (isBlockedReaction && isBlocking)
                            SetAction(FighterAction.Blocking);
                        else
                            ResetActionToNone(isBlockedReaction ? FighterAction.TakingBlockedHit : FighterAction.TakingHit);

                        if (isBlockedReaction && PlayingBlockAnimationEarlier)
                            PlayingBlockAnimationEarlier = false;

                        if (!IsBusy)
                            OnEndAction?.Invoke();
                    }
                }

                if (willBeDead)
                {
                    OnDeathAnimationCompleted?.Invoke();
                    SetAction(FighterAction.None);
                }

                OnGotHitCompleted?.Invoke();
            }
        }

        public IEnumerator PlayDeathAnimation(CombatHitData hitData, Transform attacker = null)
        {
            Die();
            
            if (IsKnockedDown)
            {
                // Already on the ground — a standing death animation would snap the pose. So die in place.
                OnDeath?.Invoke();
                OnDeathAnimationCompleted?.Invoke();
                yield break;
            }

            SetAction(FighterAction.TakingHit);

            OnStartAction?.Invoke();

            AnimGraphClipInfo clipInfo = null;
            Reaction reaction = null;
            if (defaultAnimations?.deathReactionData != null)
            {
                reaction = ChooseHitReaction(defaultAnimations.deathReactionData.reactions, hitData.HitDirection, hitData.ReactionTag, attacker, hitData.HitType);
                clipInfo = reaction?.animationClipInfo;
            }

            if (reaction != null && reaction.rotateToAttacker && attacker != null)
                RotateToAttacker(attacker, reaction.rotationOffset);

            OnDeath?.Invoke();

            if (clipInfo?.clip == null)
            {
                // No death animation available — fire completion and skip execution.
                // Passing a null clipInfo into ActionExecutor.ExecuteAsync would throw.
                OnDeathAnimationCompleted?.Invoke();
            }
            else if (reaction != null && reaction.blendToRagdoll)
            {
                Coroutine ragdollBlendCo = null;
                var deathAction = new CharacterAction()
                {
                    clipInfo = clipInfo,
                    animationParameters = new AnimParameters()
                    {
                        transitionBack = false,
                        OnComplete = OnDeathAnimationCompleted,
                        onAnimationUpdate = (normalizedTime, time) =>
                        {
                            if (ragdollBlendCo == null && normalizedTime >= reaction.ragdollBlendStartTime)
                                ragdollBlendCo = StartCoroutine(BlendToRagdoll(reaction));
                        }
                    }
                };
                yield return ActionExecutor.ExecuteAsync(deathAction);
            }
            else
            {
                var deathAction = new CharacterAction()
                {
                    clipInfo = clipInfo,
                    animationParameters = new AnimParameters()
                    {
                        transitionBack = false,
                        OnComplete = OnDeathAnimationCompleted,
                    }
                };

                yield return ActionExecutor.ExecuteAsync(deathAction);
            }

            ResetActionToNone(FighterAction.TakingHit);
        }

        void Die()
        {
            IsDead = true;
            Target = null;

            // A dead character is not interacting with anything, whichever way it died.
            CancelInteraction();

            // Stop any in-flight additive reaction crossfade so its playable graph
            // mutations don't interfere with subsequent death/knockdown root motion.
            if (additiveReactionCoroutine != null)
            {
                StopCoroutine(additiveReactionCoroutine);
                additiveReactionCoroutine = null;
            }
        }

        public IEnumerator PlayTauntAction(AnimGraphClipInfo tauntClip)
        {
            var taunAction = new CharacterAction()
            {
                clipInfo = tauntClip,
            };

            yield return ExecuteFighterAction(taunAction, FighterAction.Taunt);
        }

        public IEnumerator ExecuteFighterAction(CharacterAction action, FighterAction actionType)
        {
            SetAction(actionType);
            OnStartAction?.Invoke();

            yield return ActionExecutor.ExecuteAsync(action);

            ResetActionToNone(actionType);
            if (!IsBusy)
                OnEndAction?.Invoke();
        }

        void RotateToAttacker(Transform attacker, float rotationOffset = 0f)
        {
            var dispVec = attacker.position - transform.position;
            dispVec.y = 0;
            transform.rotation = Quaternion.LookRotation(dispVec) * Quaternion.Euler(new Vector3(0f, rotationOffset, 0f));
        }

        public bool CheckIfAttackKills(CombatWeapon weapon, float attackDamage, bool willBlock = false)
        {
            float damage = DamageCalculator.CalculateDamage(weapon.GetAttributeValue<float>("Damage"), attackDamage, Damagable);

            if (willBlock)
                damage *= (weapon.BlockedDamage / 100);

            return Damagable.CurrentHealth - damage <= 0;
        }

        Reaction ChooseHitReaction(List<ReactionContainer> reactions, HitDirections hitDirection = HitDirections.Any,
            string reactionTag = null, Transform attacker = null, HitType hitType = HitType.Any)
        {
            if (reactions == null || reactions.Count == 0) return null;

            // Filter by hit type
            if (hitType != HitType.Any)
            {
                var reactionsWithType = reactions.Where(r => r.hitType == hitType || r.hitType == HitType.Any).ToList();
                if (reactionsWithType.Count > 0)
                    reactions = reactionsWithType;
            }

            if (attacker != null)
            {
                // If attacked from behind
                bool attackedFromBehind = Vector3.Angle(transform.forward, attacker.transform.forward) <= 90;
                if (attackedFromBehind)
                {
                    var reactionsFromBehind = reactions.Where(r => r.attackedFromBehind);
                    if (reactionsFromBehind.Count() > 0)
                        reactions = reactionsFromBehind.ToList();
                }
            }

            // Tag match
            if (!String.IsNullOrEmpty(reactionTag))
            {
                var reactionsWithSameTag = reactions.Where(r => !String.IsNullOrEmpty(r.tag) && r.tag.ToLower() == reactionTag.ToLower());
                if (reactionsWithSameTag.Count() > 0)
                    reactions = reactionsWithSameTag.ToList();
                else
                {
                    var reactionsThatContainsTag = reactions.Where(r => !String.IsNullOrEmpty(r.tag)
                        && (r.tag.ToLower().Contains(reactionTag.ToLower()) || reactionTag.ToLower().Contains(r.tag.ToLower())));

                    if (reactionsThatContainsTag.Count() > 0)
                        reactions = reactionsThatContainsTag.ToList();
                }
            }

            // Match direction of the attack
            if (hitDirection != HitDirections.Any)
            {
                var reactionsWithSameDir = reactions.Where(c => c.direction == hitDirection);
                if (reactionsWithSameDir.Count() > 0)
                    reactions = reactionsWithSameDir.ToList();
                else
                {
                    var reactionsWithAnyDir = reactions.Where(c => c.direction == HitDirections.Any);
                    if (reactionsWithAnyDir.Count() > 0)
                        reactions = reactionsWithAnyDir.ToList();
                }
            }
            
            var hasStateMachineReactions = reactions.Where(c => c.isAnimationStateBasedReaction && c.MatchesAnyEntry(animator,0)).ToList();

            if (hasStateMachineReactions.Count > 0)
                reactions = hasStateMachineReactions;
            else
                reactions = reactions.Where(c => c.StateMachineNames.Count == 0).ToList();

            var selectedReactionContainer = reactions.Count > 0 ? reactions.GetRandom() : null;


            return selectedReactionContainer?.reaction;
        }

        public HitDirections GetHitDirection(Vector3 hitPoint)
        {
            var direction = (hitPoint - transform.position + Vector3.up * 0.5f).normalized;
            var right = Vector3.Dot(direction, transform.right);
            var up = Vector3.Dot(direction, transform.up);


            if (Mathf.Abs(right) > Mathf.Abs(up))
                return right > 0 ? HitDirections.Right : HitDirections.Left;
            else if (Mathf.Abs(up) > Mathf.Abs(right))
                return up > 0 ? HitDirections.Top : HitDirections.Bottom;

            return HitDirections.Any;
        }


        IEnumerator GoToKnockedDownState(Reaction reaction)
        {
            IsKnockedDown = true;
            OnKnockDown?.Invoke();
            OnKnockDownEvent.Invoke();

            AdjustColliderForKnockedDownState();

            Locomotion.BlockLocomotion();

            AnimGraphClipInfo lyingDownClip = ResolveLyingDownClip(reaction);
            AnimGraphClipInfo getUpClip = ResolveGetUpClip(reaction);

            if (lyingDownClip != null)
            {
                animGraph.PlayLoopingAnimation(lyingDownClip.clip, lyingDownClip);
                yield return new WaitForSeconds(UnityEngine.Random.Range(reaction.lyingDownTimeRange.x, reaction.lyingDownTimeRange.y));
                yield return new WaitUntil(() => Action != FighterAction.TakingHit);
            }

            // Don't Getup if dead
            if (Damagable.CurrentHealth <= 0)
                yield break;

            ResetColliderAdjustments();
            //animGraph.StopLoopingClip = true;


            if (getUpClip == null)
            {
                Debug.LogWarning("No Get Up Animations is provided to get up from the knocked down state");
                SetAction(FighterAction.None);
                if (lyingDownClip != null)
                    animGraph.StopLoopingAnimations(false);

                Locomotion.UnblockLocomotion();
                yield break;
            }

            SetAction(FighterAction.GettingUp);
            OnGettingUp?.Invoke();
            OnGettingUpEvent.Invoke();

            if (lyingDownClip != null)
                StartCoroutine(AsyncUtil.RunAfterDelay(0.2f, () => animGraph.StopLoopingAnimations(false)));


            var getUpAction = new CharacterAction()
            {
                clipInfo = getUpClip
            };
            yield return ActionExecutor.ExecuteAsync(getUpAction);

            Locomotion.UnblockLocomotion();
            IsKnockedDown = false;
            ResetActionToNone(FighterAction.GettingUp);

        }

        public FighterCore GetEnemyToTarget(Vector3 direction, 
            TargetSelectionCriteria criteria = TargetSelectionCriteria.DirectionAndDistance, 
            float directionScaleFactor=0.1f, float verticalLimit=Mathf.Infinity)
        {
            float minDistance = Mathf.Infinity;
            float minAngle = Mathf.Infinity;
            float minSum = Mathf.Infinity;
            FighterCore closestTarget = null;

            var targetsInRange = CombatTargetSensor.GetTargetsInRange();

            foreach (var target in targetsInRange)
            {
                var vecToEnemy = target.transform.position - transform.position;
                if (Mathf.Abs(vecToEnemy.y) > verticalLimit) continue;
                vecToEnemy.y = 0;

                float distanceToTarget = vecToEnemy.magnitude;

                if (criteria == TargetSelectionCriteria.Direction)
                {
                    float angle = Vector3.Angle(direction, vecToEnemy);
                    if (angle < minAngle)
                    {
                        minAngle = angle;
                        closestTarget = target;
                    }
                }
                else if (criteria == TargetSelectionCriteria.Distance)
                {
                    float distance = distanceToTarget;
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestTarget = target;
                    }
                }
                else if (criteria == TargetSelectionCriteria.DirectionAndDistance)
                {
                    float weightedSum = distanceToTarget + (Vector3.Angle(direction, vecToEnemy) * directionScaleFactor);
                    if (weightedSum < minSum)
                    {
                        minSum = weightedSum;
                        closestTarget = target;
                    }
                }
            }

            return closestTarget;
        }

        /// <summary>
        /// Stops the hotspot interaction this character is in the middle of, if any — and with it,
        /// drops whatever it is carrying (CarryHandler listens for the cancel). Called for the
        /// three things that take the body away from an interaction: a non-additive hit reaction,
        /// a ragdoll starting, and death. An additive flinch is deliberately not one of them: it
        /// layers over the interaction's own clip, and the character can take it and carry on.
        ///
        /// Cancel self-gates, so this costs nothing when there is no interaction to stop.
        /// </summary>
        void CancelInteraction()
        {
            interactionExecutor?.Cancel();
        }

        IEnumerator BlendToRagdoll(Reaction reaction)
        {
            if (ActiveRagdoll == null || !ActiveRagdoll.HasRagdollBones) yield break;

            if (!IsDead)
            {
                OnRagdollImpactStarted?.Invoke();
            }

            yield return ActiveRagdoll.BlendToRagdollAsync(syncRoot: true);
            ActiveRagdoll.StartSettlingMonitor();
        }

        void OnActiveRagdollSettled()
        {
            //if (IsDead || Damagable.IsDead)
            //{
            //    ActiveRagdoll.Deactivate();
            //    return;
            //}
           
            StartCoroutine(RagdollGetUpRoutine());
        }

        IEnumerator RagdollGetUpRoutine()
        {
            IsKnockedDown = true;
            OnKnockDown?.Invoke();
            OnKnockDownEvent.Invoke();

            // Detect orientation BEFORE blending — ragdoll bones still have their physics pose.
            // hips.forward points from spine toward belly:
            //   dot > 0 with Vector3.up → belly faces up → lying on back
            //   dot < 0 with Vector3.up → belly faces down → lying on front
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            bool faceDown = hips != null && Vector3.Dot(hips.forward, Vector3.up) < 0f;

            AnimGraphClipInfo getUpClip = ResolveRagdollGetUpClip(faceDown);

            AdjustColliderForKnockedDownState();

            // Dead — stay down, no get-up.
            if (IsDead || Damagable.IsDead)
            {
                IsKnockedDown = false;
                yield break;
            }

            ResetColliderAdjustments();

            SetAction(FighterAction.GettingUp);
            OnGettingUp?.Invoke();
            OnGettingUpEvent.Invoke();

            if (getUpClip == null)
            {
                Debug.LogWarning("No Get Up / Lying animation available for ragdoll recovery.");
                IsKnockedDown = false;
                yield break;
            }

            var getUpAction = new CharacterAction
            {
                clipInfo = getUpClip
            };
            getUpAction.animationParameters = new AnimParameters();
            getUpAction.animationParameters.transitionIn = 0;
            //getUpAction.ignoreCollision = true;

            if (ActiveRagdoll != null)
            {
                // Disable root motion until AlignAnimatorToRagdoll has committed the
                // correct position and the animator has run one suppressed frame.
                // BlendToAnimatorAsync fires onAligned at exactly that moment.
                getUpAction.usesRootMovement = false;
                yield return ActiveRagdoll.BlendToAnimatorAsync(getUpAction,
                    onAligned: () => getUpAction.usesRootMovement = true);
            }

            IsKnockedDown = false;
            ResetActionToNone(FighterAction.GettingUp);
        }

        /// <summary>
        /// Used to handle heavy impact damages like explosion, vehicle hits, etc. 
        /// </summary>
        /// <param name="impactData">Contains details of the impact like the force to apply, etc</param>
        void HandleImpactDamage(ImpactData impactData)
        {
            bool useRagdoll = defaultAnimations.useRagdollForExplosion && ActiveRagdoll != null && ActiveRagdoll.HasRagdollBones;

            if (useRagdoll) 
            {
                // Threshold check — explosions below ShooterSettings.minExplosionDamageForRagdoll
                // don't ragdoll the victim (animation reaction only, character stays upright).
                float minDamage = defaultAnimations.minExplosionDamageForRagdoll;
                if (impactData.damage > 0f && impactData.damage < minDamage)
                    return;

                // The explosion damage has already been applied by TakeImpactDamage
                // before this event fires — the character may now be dead.
                if (Damagable.IsDead)
                {
                    Die();
                    OnDeath?.Invoke();
                }

                // Fire the event so player-side code (ReactionHandler) can focus its
                // ActionSystemBase for the ragdoll + get-up. FighterCore itself stays
                // agnostic — AI fighters simply have no listener.
                OnRagdollImpactStarted?.Invoke();
                SetAction(FighterAction.TakingHit);
                StartCoroutine(ImpactRagdollSequence(impactData));
            }
            else
            {
                if (Damagable.IsDead)
                {
                    var hitData = new CombatHitData();
                    StartCoroutine(PlayDeathAnimation(hitData));
                }
            }
        }

        IEnumerator ImpactRagdollSequence(ImpactData impact)
        {
            yield return ActiveRagdoll.BlendToRagdollAsync(syncRoot: true); 

            Vector3 forceDir;
            float forceMagnitude;

            if (impact.direction.sqrMagnitude > 0.001f)
            {
                // Directional impact (vehicle hit, etc.) — use the provided direction and force directly.
                forceDir = impact.direction.normalized;
                forceMagnitude = impact.force;
            }
            else
            {
                // Radial impact (explosion) — compute direction from blast center, attenuate by distance.
                forceDir = (transform.position - impact.position).normalized + Vector3.up * 0.5f;
                forceDir = forceDir.normalized;
                float distance = Mathf.Max(Vector3.Distance(transform.position, impact.position), 1f);
                forceMagnitude = impact.force / distance;
            }

            // Cancel any physics forces from the collision (e.g. vehicle pushing ragdoll),
            // then apply only our controlled force.
            StartCoroutine(AsyncUtil.RunAfterDelay(0.1f, () =>
            {
                ActiveRagdoll.ZeroAllVelocities();
                ActiveRagdoll.ApplyForce(HumanBodyBones.Hips, forceDir * forceMagnitude);

                //// OnActiveRagdollSettled → RagdollGetUpRoutine handles recovery.
                ActiveRagdoll.StartSettlingMonitor();
            }));
        }

        // ──────────────────────────────────────────────────────────────────────

        void OnDestroy()
        {
            if (ActiveRagdoll != null)
            {
                ActiveRagdoll.OnSettled -= OnActiveRagdollSettled;
                ActiveRagdoll.OnRagdollStart -= CancelInteraction;
            }
            if (Damagable != null)
                Damagable.OnImpactDamage -= HandleImpactDamage;
        }

        // ── Knocked-down animation resolution with fallback ─────────────────────────
        // Pick the first non-null AnimGraphClipInfo (with a non-null clip) from an ordered list
        // of candidates. Used to fall back across directions and between lying/get-up clips.
        static AnimGraphClipInfo PickClip(params AnimGraphClipInfo[] candidates)
        {
            if (candidates == null) return null;
            for (int i = 0; i < candidates.Length; i++)
            {
                var c = candidates[i];
                if (c != null && c.clip != null) return c;
            }
            return null;
        }

        AnimGraphClipInfo ResolveLyingDownClip(Reaction reaction)
        {
            bool onBack = reaction.knockDownDirection == KnockDownDirection.LyingOnBack;
            var primary   = onBack ? defaultAnimations.lyingOnBackAnimation  : defaultAnimations.lyingOnFrontAnimation;
            var alternate = onBack ? defaultAnimations.lyingOnFrontAnimation : defaultAnimations.lyingOnBackAnimation;
            var getPrim   = onBack ? defaultAnimations.getUpFromBackAnimation  : defaultAnimations.getUpFromFrontAnimation;
            var getAlt    = onBack ? defaultAnimations.getUpFromFrontAnimation : defaultAnimations.getUpFromBackAnimation;

            // Preference: reaction-specific (if overriding) → direction-matched lying → opposite-direction lying →
            //             reaction-specific get-up (if overriding) → direction-matched get-up → opposite-direction get-up.
            return PickClip(
                reaction.overrideLyingDownAnimation ? reaction.lyingDownAnimation : null,
                primary,
                alternate,
                reaction.overrideLyingDownAnimation ? reaction.getUpAnimation : null,
                getPrim,
                getAlt);
        }

        AnimGraphClipInfo ResolveGetUpClip(Reaction reaction)
        {
            bool onBack = reaction.knockDownDirection == KnockDownDirection.LyingOnBack;
            var primary   = onBack ? defaultAnimations.getUpFromBackAnimation  : defaultAnimations.getUpFromFrontAnimation;
            var alternate = onBack ? defaultAnimations.getUpFromFrontAnimation : defaultAnimations.getUpFromBackAnimation;
            var lyingPrim = onBack ? defaultAnimations.lyingOnBackAnimation  : defaultAnimations.lyingOnFrontAnimation;
            var lyingAlt  = onBack ? defaultAnimations.lyingOnFrontAnimation : defaultAnimations.lyingOnBackAnimation;

            return PickClip(
                reaction.overrideLyingDownAnimation ? reaction.getUpAnimation : null,
                primary,
                alternate,
                reaction.overrideLyingDownAnimation ? reaction.lyingDownAnimation : null,
                lyingPrim,
                lyingAlt);
        }

        AnimGraphClipInfo ResolveRagdollGetUpClip(bool faceDown)
        {
            var primary   = faceDown ? defaultAnimations.getUpFromFrontAnimation : defaultAnimations.getUpFromBackAnimation;
            var alternate = faceDown ? defaultAnimations.getUpFromBackAnimation  : defaultAnimations.getUpFromFrontAnimation;
            var lyingPrim = faceDown ? defaultAnimations.lyingOnFrontAnimation : defaultAnimations.lyingOnBackAnimation;
            var lyingAlt  = faceDown ? defaultAnimations.lyingOnBackAnimation  : defaultAnimations.lyingOnFrontAnimation;

            return PickClip(primary, alternate, lyingPrim, lyingAlt);
        }

        Vector3 colliderOriginalCenter;
        void AdjustColliderForKnockedDownState()
        {
            if (capsuleCollider == null) return;
            capsuleCollider.direction = 2;
            capsuleCollider.center = new Vector3(capsuleCollider.center.x, 0, capsuleCollider.center.z);
        }

        void ResetColliderAdjustments()
        {
            if (capsuleCollider == null) return;

            capsuleCollider.direction = 1;
            capsuleCollider.center = colliderOriginalCenter;
        }

        public event Action<FighterCore> OnBeingAttacked;
        public void BeingAttacked(FighterCore attacker)
        {
            OnBeingAttacked?.Invoke(attacker);
            IsBeingAttacked = true;
            CurrAttacker = attacker;
        }

        public void AttackOver(FighterCore attacker)
        {
            if (CurrAttacker == attacker)
            {
                IsBeingAttacked = false;
                CurrAttacker = null;
            }
        }

        public void QuickSwitchWeapon(EquippableItem item = null)
        {
            if (item == null)
                itemEquipper.UnEquipItem(false);
            else
                itemEquipper.EquipItem(item, false);
        }

        public IEnumerator PullBackCharacter(FighterCore attacker, float moveDist = .75f)
        {
            float timer = 0;
            while (timer < moveDist)
            {
                characterController.Move((attacker.transform.forward * moveDist) * Time.deltaTime * 2);
                timer += Time.deltaTime * 2;
                yield return null;
            }
        }

        public void ResetFighter()
        {
            SetAction(FighterAction.None);
            CanDoActions = true;
            animGraph.ResetAnimationGraph();
            TargetOfAttack = null;
            OnResetFighter?.Invoke();
        }

        
        //float ySpeed;
        //public void ApplyAnimationGravity(bool isGrounded, bool systemIsInFocus)
        //{
        //    if (isGrounded) ySpeed = Physics.gravity.y;
        //    if (animGraph.currentClipStateInfo.isPlayingAnimation && animGraph.currentClipStateInfo.currentClipInfo.useGravity 
        //        && !ActionExecutor.IsMotionWarping && systemIsInFocus && !ActionExecutor.IgnoreCollision)
        //    {
        //        ySpeed += Physics.gravity.y * Time.deltaTime;
        //        characterController.Move(ySpeed * Vector3.up * animGraph.currentClipInfo.gravityModifier.GetValue(animGraph.currentClipStateInfo.normalizedTime) * Time.deltaTime);
        //    }
        //    animator.SetBool(AnimatorParameters.IsGrounded, isGrounded);
        //}

        public void TriggerOnWeaponEquip(CombatWeapon weaponData)
        {
            OnWeaponEquipAction?.Invoke(weaponData);
            OnWeaponEquipEvent?.Invoke(weaponData);
        }

        public void TriggerOnWeaponUnEquip(CombatWeapon weaponData)
        {
            OnWeaponUnEquipAction?.Invoke(weaponData);
            OnWeaponUnEquipEvent?.Invoke(weaponData);
        }

        public bool IsTargetValid(Transform targetToCheck)
        {
            if (!IsTarget(targetToCheck.gameObject))
                return false;

            var targetFighter = targetToCheck.GetComponent<FighterCore>();
            if (targetFighter == null|| targetFighter.IsDead)
                return false;

            return true;
        }

        public void SetCombatLocomotionOverride(bool active)
        {
            var weapon = CurrentWeapon;
            if (active && weapon != null && weapon.LocomotionClipSet2D != null)
                animGraph.OverrideLocomotion2D(weapon.LocomotionClipSet2D);
            else if (!active && animGraph != null)
                animGraph.ClearLocomotionOverride();
        }

        public void TriggerOnStartAction() => OnStartAction?.Invoke();
        public void TriggerOnEndAction() => OnEndAction?.Invoke();

        float defaultPreferredFightingRange;
        public void UpdatePreferredFightingRange(float range)
        {
            // defaultPreferredFightingRange = PreferredFightingRange;
            PreferredFightingRange = range;
        }

        public void ResetPreferredFigtingRangeToDefault()
        {
            PreferredFightingRange = defaultPreferredFightingRange;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // Handles.Label(transform.position + Vector3.up * 2f, Action.ToString(), new GUIStyle() { fontSize = 20 });
        }
#endif
    }

    public class CombatHitData
    {
        public float Damage { get; set; }
        public int DamageType { get; set; }
        public HitType HitType { get; set; } = HitType.Any;
        public HitDirections HitDirection { get; set; }
        public Damagable Damagable { get; set; }    // optional - useful in cases where damaged should be applied to certain body parts
        public string ReactionTag { get; set; }
        public bool PlayReaction { get; set; } = true;
        public Reaction Reaction { get; set; }
        public Vector3 HitPoint { get; set; }
        public bool IsFinisher { get; set; }
        public bool? WillBeBlocked { get; set; }
        public float HittingTime { get; set; }
        public AttackType AttackType { get; set; }
        public bool IsUnBlockableHit { get; set; }
        public bool CanHitMultipleTargets { get; set; }
        public float WaitTimeTillNextReaction { get; set; }
        public string AttackName { get; set; }  // For debugging purposes, to identify the attack that caused the hit
    }

    [Serializable]
    public class DefaultReactions
    {
        public ReactionsData hitReactionData;
        public ReactionsData blockedReactionData;
        public DeathReactionsData deathReactionData;

        [Header("Lying down and Get up Animations")]
        public AnimGraphClipInfo lyingOnBackAnimation;
        public AnimGraphClipInfo getUpFromBackAnimation;
        public AnimGraphClipInfo lyingOnFrontAnimation;
        public AnimGraphClipInfo getUpFromFrontAnimation;

        public bool useRagdollForExplosion = true;
        [Tooltip("Minimum damage from a single explosion required to trigger a ragdoll on the victim. Explosions dealing less than this leave the character upright (animation reaction only).")]
        public float minExplosionDamageForRagdoll = 25f;
    }

    [Serializable]
    public class DodgeData
    {
        [HideInInspector]
        public AnimationClip clip;
        public AnimGraphClipInfo clipInfo;

        public DodgeDirection defaultDirection;
        public bool useDifferentClipsForDirections;

        public AnimGraphClipInfo frontClipInfo;
        public AnimGraphClipInfo backClipInfo;
        public AnimGraphClipInfo leftClipInfo;
        public AnimGraphClipInfo rightClipInfo;

        public Vector3 GetDodgeDirection(Transform transform, Transform target)
        {
            if (defaultDirection == DodgeDirection.Forward || (defaultDirection == DodgeDirection.TowardsTarget && target == null))
                return transform.forward;
            else if (defaultDirection == DodgeDirection.Backward || (defaultDirection == DodgeDirection.AwayFromTarget && target == null))
                return -transform.forward;
            else if (defaultDirection == DodgeDirection.AwayFromTarget)
                return -(target.position - transform.position);
            else if (defaultDirection == DodgeDirection.TowardsTarget)
                return (target.position - transform.position);

            return -transform.forward;
        }

        public AnimGraphClipInfo GetClip(Transform transform, Vector3 direction)
        {
            if (!useDifferentClipsForDirections)
                return clipInfo;

            var dir = transform.InverseTransformDirection(direction);
            //Debug.Log(dir);

            float h = dir.x;
            float v = dir.z;

            if (Math.Abs(v) >= Math.Abs(h))
                return (v > 0) ? frontClipInfo : backClipInfo;
            else
                return (h > 0) ? rightClipInfo : leftClipInfo;
        }
    }

    public enum DodgeDirection { AwayFromTarget, TowardsTarget, Backward, Forward }

    public enum TargetSelectionCriteria { DirectionAndDistance, Direction, Distance }
}
