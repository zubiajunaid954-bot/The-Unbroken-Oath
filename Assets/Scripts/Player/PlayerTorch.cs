using UnityEngine;

namespace GAMECON.PlayerTools
{
    /// <summary>
    /// Hand-held torch for the player. Press the toggle key (F by default) to switch it on/off.
    /// A spot light lights up what you look at, a small point light glows around the torch,
    /// and the flame effect + sound follow the state.
    /// </summary>
    public class PlayerTorch : MonoBehaviour
    {
        public KeyCode toggleKey = KeyCode.F;
        public bool startOn = true;

        [Header("Parts")]
        public Light beam;               // spot light, aimed where the camera looks
        public Light glow;               // small point light around the torch
        public ParticleSystem flame;
        public ParticleSystem halo;
        public AudioSource clickSource;
        public AudioClip clickClip;

        [Header("Look")]
        public float beamIntensity = 4.5f;
        public float glowIntensity = 1.4f;
        public float fadeSpeed = 12f;
        [Range(0f, 0.3f)] public float flicker = 0.06f;

        [Tooltip("The torch can only be used at night (set by the DayNightController).")]
        public bool allowed = true;

        public bool IsOn { get; private set; }

        void SetAllowedOff()
        {
            IsOn = false;
            if (flame != null) flame.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (halo != null) halo.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        public void SetAllowed(bool a)
        {
            if (allowed == a) return;
            allowed = a;
            if (!a && IsOn) { IsOn = false; if (flame != null) flame.Stop(true, ParticleSystemStopBehavior.StopEmitting); if (halo != null) halo.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
            else if (a) IsOn = true;       // night falls: the torch comes on by itself
        }
        float level;                     // 0..1 smoothed on/off
        float seed;

        void Start()
        {
            seed = Random.value * 100f;
            IsOn = startOn && allowed;
            level = IsOn ? 1f : 0f;
            Apply();
        }

        void Update()
        {
            if (allowed && Input.GetKeyDown(toggleKey)) Toggle();
            if (!allowed && IsOn) SetAllowedOff();

            float target = IsOn ? 1f : 0f;
            level = Mathf.MoveTowards(level, target, Time.deltaTime * fadeSpeed);
            Apply();
        }

        public void Toggle()
        {
            IsOn = !IsOn;
            if (clickSource != null && clickClip != null) clickSource.PlayOneShot(clickClip, 0.8f);
            if (flame != null) { if (IsOn) flame.Play(); else flame.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
            if (halo != null) { if (IsOn) halo.Play(); else halo.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
        }

        void Apply()
        {
            float f = 1f - flicker + Mathf.PerlinNoise(seed, Time.time * 7f) * flicker * 2f;
            if (beam != null) { beam.enabled = level > 0.01f; beam.intensity = beamIntensity * level * f; }
            if (glow != null) { glow.enabled = level > 0.01f; glow.intensity = glowIntensity * level * f; }
        }
    }
}
