using System.Collections;
using UnityEngine;

namespace FS_StealthSystem
{
    /// <summary>
    /// Demo-only helper: drives a light so its lit area changes over time, giving each
    /// light in a demo a distinct, observable behaviour.
    ///
    /// Every mode keeps the <see cref="StealthLight"/> in step with what the light is
    /// actually doing, so what the player sees always matches what the AI samples.
    /// The stealth system has no dependency on this script.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class StealthLightMotion : MonoBehaviour
    {
        // Existing scenes serialize this by index, so new modes are only ever appended.
        public enum MotionMode { Sweep, Blink, SweepAndBlink, Pulse, Travel }

        [Tooltip("How this light behaves: sweep its beam, blink on and off, both, pulse in intensity, or slide back and forth.")]
        [SerializeField] MotionMode mode = MotionMode.Sweep;

        [Header("Sweep / Swing")]
        [Tooltip("Total arc swept, in degrees (the light swings half this each way).")]
        [SerializeField] float sweepArc = 50f;
        [Tooltip("Seconds for one full back-and-forth sweep.")]
        [SerializeField] float sweepPeriod = 6f;
        [Tooltip("Axis to swing around, in the light's local space. Up sweeps a floor-facing spot sideways.")]
        [SerializeField] Vector3 sweepAxis = Vector3.up;

        [Header("Blink")]
        [Tooltip("Seconds the light stays on in each blink cycle.")]
        [SerializeField] float onDuration = 5f;
        [Tooltip("Seconds the light stays off in each blink cycle.")]
        [SerializeField] float offDuration = 3f;
        [Tooltip("Seconds to offset this light's cycle, so neighbouring lights are not in sync.")]
        [SerializeField] float phaseOffset;

        [Header("Pulse")]
        [Tooltip("Intensity at the dimmest point of the pulse.")]
        [SerializeField] float pulseMinIntensity = 4f;
        [Tooltip("Intensity at the brightest point of the pulse.")]
        [SerializeField] float pulseMaxIntensity = 40f;
        [Tooltip("Seconds for one fade between the dimmest and brightest intensity.")]
        [SerializeField] float pulsePeriod = 4f;
        [Tooltip("Seconds the light holds at the dimmest intensity before the next pulse.")]
        [SerializeField] float pulsePosePeriod = 0;

        [Header("Travel")]
        [Tooltip("Direction the light slides along, in local space.")]
        [SerializeField] Vector3 travelAxis = Vector3.forward;
        [Tooltip("Distance travelled each way from the starting point.")]
        [SerializeField] float travelDistance = 8f;
        [Tooltip("Seconds for one full back-and-forth trip.")]
        [SerializeField] float travelPeriod = 8f;

        Light lightSource;
        StealthLight stealthLight;
        Quaternion baseRotation;
        Vector3 basePosition;
        float clock;

        /// <summary>What this light is doing right now, for a demo readout.</summary>
        public string StateLabel
        {
            get
            {
                switch (mode)
                {
                    case MotionMode.Blink: return lightSource != null && lightSource.enabled ? "ON" : "OFF";
                    case MotionMode.Pulse: return lightSource == null ? "-" : Mathf.RoundToInt(lightSource.intensity) + " cd";
                    default: return mode.ToString();
                }
            }
        }

        void Awake()
        {
            lightSource = GetComponent<Light>();
            stealthLight = GetComponent<StealthLight>();
            baseRotation = transform.localRotation;
            basePosition = transform.localPosition;
            clock = phaseOffset;
        }

        void Update()
        {
            clock += Time.deltaTime;

            if (mode == MotionMode.Sweep || mode == MotionMode.SweepAndBlink)
            {
                float phase = Mathf.Sin(clock / Mathf.Max(0.01f, sweepPeriod) * Mathf.PI * 2f);
                transform.localRotation = baseRotation * Quaternion.AngleAxis(phase * sweepArc * 0.5f, sweepAxis.normalized);
            }

            if (mode == MotionMode.Blink || mode == MotionMode.SweepAndBlink)
            {
                float cycle = Mathf.Max(0.01f, onDuration + offDuration);
                SetLit(Mathf.Repeat(clock, cycle) < onDuration);
            }

            if (mode == MotionMode.Pulse)
            {
                PulseLight();
            }

            if (mode == MotionMode.Travel)
            {
                float phase = Mathf.Sin(clock / Mathf.Max(0.01f, travelPeriod) * Mathf.PI * 2f);
                transform.localPosition = basePosition + travelAxis.normalized * (phase * travelDistance);
            }
        }

        void SetLit(bool on)
        {
            if (lightSource.enabled != on)
                lightSource.enabled = on;

            // LightVision walks StealthLight.ActiveLights and reads Light.isActiveAndEnabled, but the
            // StealthLight itself must go too - otherwise a dark lamp still registers as a live source.
            if (stealthLight != null && stealthLight.enabled != on)
                stealthLight.enabled = on;
        }
        bool isInPulsePose = false;
        private float pulseTimer;
        private bool isIncreasing;

        private void PulseLight()
        {
            pulseTimer += Time.deltaTime;

            if (isInPulsePose)
            {
                if (pulseTimer >= pulsePosePeriod)
                {
                    pulseTimer = 0f;
                    isInPulsePose = false;
                    isIncreasing = true;
                }

                return;
            }

            float t = Mathf.Clamp01(pulseTimer / pulsePeriod);

            if (isIncreasing)
            {
                // MIN -> MAX
                lightSource.intensity = Mathf.Lerp(
                    pulseMinIntensity,
                    pulseMaxIntensity,
                    t
                );

                if (t >= 1f)
                {
                    pulseTimer = 0f;
                    isIncreasing = false;
                }
            }
            else
            {
                // MAX -> MIN
                lightSource.intensity = Mathf.Lerp(
                    pulseMaxIntensity,
                    pulseMinIntensity,
                    t
                );

                if (t >= 1f)
                {
                    pulseTimer = 0f;
                    isInPulsePose = true;
                    lightSource.intensity = pulseMinIntensity;
                }
            }
        }
    }
}
