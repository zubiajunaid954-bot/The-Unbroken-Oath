using UnityEngine;

namespace GAMECON.Weather
{
    /// <summary>Gentle flame flicker for torch / lamp point lights.</summary>
    [RequireComponent(typeof(Light))]
    public class FlickerLight : MonoBehaviour
    {
        public float amount = 0.25f;
        public float speed = 6f;
        [Range(0f, 1f)] public float master = 1f;      // 0 = off (day), 1 = full (night): set by the DayNightController
        Light l; float baseI; float seed;

        void Awake() { l = GetComponent<Light>(); baseI = l.intensity; seed = Random.value * 100f; }

        void Update()
        {
            bool on = master > 0.01f;
            if (l.enabled != on) l.enabled = on;
            if (!on) return;
            float n = Mathf.PerlinNoise(seed, Time.time * speed);
            l.intensity = baseI * master * (1f - amount + n * amount * 2f);
        }
    }
}
