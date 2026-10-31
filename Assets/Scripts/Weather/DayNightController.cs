using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using GAMECON.Voice;

namespace GAMECON.Weather
{
    /// <summary>
    /// Two times of day in one level. The mission starts on a clear MORNING (sun, blue sky, no rain, no thunder, torches off).
    /// When the first teammate is rescued the game changes to NIGHT with rain: "ENTERING NIGHT" is shown, the sun sets, the sky
    /// darkens, fog thickens, rain and thunder fade in and the fires / torches light up. The look of both times of day is
    /// described by a Look (edit the numbers in the Inspector).
    /// </summary>
    public class DayNightController : MonoBehaviour
    {
        public static DayNightController Instance { get; private set; }

        [Serializable]
        public class Look
        {
            public Color sunColor = Color.white;
            public float sunIntensity = 1f;
            public Vector3 sunEuler = new Vector3(45, -35, 0);
            public Color ambient = Color.gray;
            public Color fogColor = Color.gray;
            public float fogDensity = 0.002f;
            public Color skyTint = Color.gray;
            public Color skyGround = Color.gray;
            public float skyExposure = 1f;
            public float skyAtmosphere = 1f;
            public float sunDisc = 0.045f;           // 0 = no visible sun / moon disc
            public float reflection = 1f;
            [Range(0f, 1f)] public float lights = 0f;       // fires, lamps and torches
            [Range(0f, 1f)] public float weather = 0f;      // rain, thunder, lightning
            public float musicVolume = 0.2f;
            public float ambienceVolume = 0.5f;             // birds + wind
            public bool torch = false;                      // is the player's torch allowed
        }

        public static Look DefaultDay()
        {
            return new Look
            {
                sunColor = new Color(1f, 0.95f, 0.84f), sunIntensity = 1.25f, sunEuler = new Vector3(48f, -35f, 0f),
                ambient = new Color(0.50f, 0.55f, 0.64f), fogColor = new Color(0.55f, 0.66f, 0.78f), fogDensity = 0.0022f,
                skyTint = new Color(0.5f, 0.5f, 0.5f), skyGround = new Color(0.37f, 0.35f, 0.33f), skyExposure = 1.25f, skyAtmosphere = 1f,
                sunDisc = 0.045f, reflection = 1f, lights = 0f, weather = 0f, musicVolume = 0.18f, ambienceVolume = 0.55f, torch = false
            };
        }

        public static Look DefaultNight()
        {
            return new Look
            {
                sunColor = new Color(0.42f, 0.52f, 0.85f), sunIntensity = 0.45f, sunEuler = new Vector3(38f, -35f, 0f),
                ambient = new Color(0.05f, 0.065f, 0.11f), fogColor = new Color(0.035f, 0.045f, 0.075f), fogDensity = 0.016f,
                skyTint = new Color(0.12f, 0.15f, 0.30f), skyGround = new Color(0.02f, 0.025f, 0.05f), skyExposure = 0.35f, skyAtmosphere = 0.35f,
                sunDisc = 0f, reflection = 0.25f, lights = 1f, weather = 1f, musicVolume = 0.4f, ambienceVolume = 0f, torch = true
            };
        }

        public Look day = DefaultDay();
        public Look night = DefaultNight();

        [Header("Scene objects (set by the editor script)")]
        public Light sun;                       // the one directional light (sun by day, moon by night)
        public Material nightSkyMaterial;       // procedural sky; copied so we can fade its values
        public StormController storm;           // rain / thunder
        public AudioSource music;               // background music
        public AudioClip dayAmbience;           // birds and wind
        public Text timeBanner;                 // "MORNING" / "ENTERING NIGHT"

        [Header("Transition")]
        public float transitionSeconds = 9f;

        public bool IsNight { get; private set; }
        public bool Changing { get; private set; }

        Material sky;
        AudioSource ambience;
        FlickerLight[] flickers;
        PlayerTools_Torch torchRef;
        float nextGi;

        // the torch lives in another namespace: keep the reference loose
        class PlayerTools_Torch { public GAMECON.PlayerTools.PlayerTorch t; }

        void Awake() { Instance = this; }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (sun == null) sun = RenderSettings.sun;
            sky = nightSkyMaterial != null ? new Material(nightSkyMaterial) : null;
            if (sky != null) RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;

            ambience = gameObject.AddComponent<AudioSource>();
            ambience.clip = dayAmbience; ambience.loop = true; ambience.spatialBlend = 0f; ambience.playOnAwake = false;
            if (dayAmbience != null) ambience.Play();

            flickers = FindObjectsOfType<FlickerLight>(true);
            var pt = FindObjectOfType<GAMECON.PlayerTools.PlayerTorch>(true);
            torchRef = new PlayerTools_Torch { t = pt };

            ApplyBlend(day, day, 0f, true);              // the game starts in the morning
            IsNight = false;
            if (!GAMECON.Mission.MissionBriefing.Enabled) ShowMorning();       // with a briefing, the title comes after it
        }

        // ------------------------------------------------------------------ public
        public void ShowMorning()
        {
            if (!IsNight) StartCoroutine(Banner("MORNING", "06:30   -   clear skies", 4.5f, 0.3f));
        }

        /// <summary>Night falls (called when the first teammate has been rescued).</summary>
        public void GoNight()
        {
            if (IsNight || Changing) return;
            StartCoroutine(ToNight());
        }

        IEnumerator ToNight()
        {
            Changing = true;
            StartCoroutine(Banner("ENTERING NIGHT", "21:00   -   rain and thunder", 5.5f, 0.2f));
            VoiceAssistant.Say(VoiceLine.NightFalls);

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(1f, transitionSeconds);
                ApplyBlend(day, night, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)), false);
                yield return null;
            }
            ApplyBlend(night, night, 1f, true);
            IsNight = true;
            Changing = false;
        }

        // ------------------------------------------------------------------ the look
        static Color Warm(Color c, float k) { return Color.Lerp(c, new Color(1f, 0.5f, 0.2f), k); }

        void ApplyBlend(Look a, Look b, float t, bool final)
        {
            float dusk = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);         // 0 at both ends, 1 in the middle: sunset glow

            if (sun != null)
            {
                // the sun dips towards the horizon while the light changes
                Vector3 e = Vector3.Lerp(a.sunEuler, b.sunEuler, t);
                e.x -= 38f * dusk;
                sun.transform.rotation = Quaternion.Euler(e);
                sun.color = Warm(Color.Lerp(a.sunColor, b.sunColor, t), 0.55f * dusk);
                sun.intensity = Mathf.Lerp(a.sunIntensity, b.sunIntensity, t) * Mathf.Lerp(1f, 0.55f, dusk);
                sun.shadows = LightShadows.Soft;
            }

            RenderSettings.ambientLight = Color.Lerp(a.ambient, b.ambient, t);
            RenderSettings.fogColor = Warm(Color.Lerp(a.fogColor, b.fogColor, t), 0.3f * dusk);
            RenderSettings.fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, t);
            RenderSettings.reflectionIntensity = Mathf.Lerp(a.reflection, b.reflection, t);

            if (sky != null)
            {
                sky.SetColor("_SkyTint", Warm(Color.Lerp(a.skyTint, b.skyTint, t), 0.35f * dusk));
                sky.SetColor("_GroundColor", Color.Lerp(a.skyGround, b.skyGround, t));
                sky.SetFloat("_Exposure", Mathf.Lerp(a.skyExposure, b.skyExposure, t));
                sky.SetFloat("_AtmosphereThickness", Mathf.Lerp(a.skyAtmosphere, b.skyAtmosphere, t));
                float disc = Mathf.Lerp(a.sunDisc, b.sunDisc, t);
                sky.SetFloat("_SunSize", Mathf.Max(0.001f, disc));
                bool showDisc = disc > 0.005f;
                sky.SetFloat("_SunDisk", showDisc ? 1f : 0f);
                if (showDisc) { sky.DisableKeyword("_SUNDISK_NONE"); sky.EnableKeyword("_SUNDISK_SIMPLE"); }
                else { sky.EnableKeyword("_SUNDISK_NONE"); sky.DisableKeyword("_SUNDISK_SIMPLE"); }
            }

            // fires, lamps, torches
            float lights = Mathf.Lerp(a.lights, b.lights, t);
            if (flickers != null) foreach (var f in flickers) if (f != null) f.master = lights;

            // rain + thunder
            float weather = Mathf.Lerp(a.weather, b.weather, t);
            if (storm != null) storm.weather = weather;

            // sound
            if (music != null) music.volume = Mathf.Lerp(a.musicVolume, b.musicVolume, t);
            if (ambience != null) ambience.volume = Mathf.Lerp(a.ambienceVolume, b.ambienceVolume, t);

            // the player's torch is only useful (and allowed) at night
            if (torchRef != null && torchRef.t != null && (final || t > 0.6f))
                torchRef.t.SetAllowed(Mathf.Lerp(a.torch ? 1f : 0f, b.torch ? 1f : 0f, t) > 0.5f);

            // ambient light / reflections are cached by Unity: refresh them now and then while the sky changes
            if (final || Time.unscaledTime >= nextGi) { DynamicGI.UpdateEnvironment(); nextGi = Time.unscaledTime + 0.5f; }
        }

        // ------------------------------------------------------------------ the on-screen time text
        IEnumerator Banner(string title, string sub, float seconds, float delay)
        {
            if (timeBanner == null) yield break;
            if (delay > 0f) yield return new WaitForSeconds(delay);
            timeBanner.text = "<size=70>" + title + "</size>\n<size=26>" + sub + "</size>";
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                float a = Mathf.Clamp01(t / 0.8f) * Mathf.Clamp01((seconds - t) / 1.0f);
                var c = timeBanner.color; c.a = a; timeBanner.color = c;
                // slight letter spacing animation feels like a title card
                timeBanner.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.96f, 1.0f, Mathf.Clamp01(t / 1.2f));
                yield return null;
            }
            timeBanner.text = "";
        }
    }
}
