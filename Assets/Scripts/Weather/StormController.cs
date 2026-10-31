using System.Collections;
using UnityEngine;

namespace GAMECON.Weather
{
    /// <summary>
    /// Night-storm runtime: keeps the rain over the player and fires random lightning
    /// (light flash first, then delayed thunder, like real life).
    /// </summary>
    public class StormController : MonoBehaviour
    {
        [Header("Time of day")]
        [Range(0f, 1f)] [Tooltip("0 = clear morning (no rain, no thunder), 1 = rainy night. Set by the DayNightController.")]
        public float weather = 1f;

        [Header("Follow")]
        public string followTag = "Player";
        public Vector3 followOffset = new Vector3(0, 22, 0);

        [Header("Rain placement")]
        public Transform groundFollower;          // splashes + mist, kept on the floor under the player
        public ParticleSystem[] rainSystems;      // emitters thinned out when sheltered
        public AudioSource rainAudio;
        public float rainAudioVolume = 0.55f;
        const float shelteredRainMultiplier = 0.3f;   // (const: ignores older values saved in the scene)
        const float shelteredAudioMultiplier = 0.5f;
        const float shelterCheckHeight = 6f;           // only a roof/ceiling within this height counts as shelter
        public bool disableShelter = true;         // true = full rain everywhere. Untick to thin the rain out under roofs
        public bool isSheltered;                   // (read-only) shows what the check currently decides
        public LayerMask groundMask = ~0;

        [Header("Lightning")]
        public Light moonLight;               // directional light flashed during lightning
        public Color flashColor = new Color(0.75f, 0.82f, 1f);
        public float flashIntensity = 2.2f;
        public Vector2 interval = new Vector2(9f, 26f);

        [Header("Thunder")]
        public AudioSource thunderSource;
        public AudioClip[] thunderNear;       // flash is close  -> short delay
        public AudioClip[] thunderFar;        // flash is distant -> long delay
        public Vector2 thunderDelay = new Vector2(0.15f, 4.5f);

        Transform target;
        float[] baseRates;
        float shelter;            // 0 = open sky, 1 = under a roof
        float groundY;
        bool hasGround;
        float baseIntensity;
        Color baseColor;
        Color baseAmbient;

        void Start()
        {
            if (moonLight != null) { baseIntensity = moonLight.intensity; baseColor = moonLight.color; }
            baseAmbient = RenderSettings.ambientLight;
            if (rainSystems != null)
            {
                baseRates = new float[rainSystems.Length];
                for (int i = 0; i < rainSystems.Length; i++)
                    if (rainSystems[i] != null) baseRates[i] = rainSystems[i].emission.rateOverTime.constant;
            }
            StartCoroutine(LightningLoop());
        }

        void LateUpdate()
        {
            if (target == null)
            {
                // NB: enemy bots also carry the "Player" tag, so find the real player by its controller
                var uc = FindObjectOfType<scgFullBodyController.ThirdPersonUserControl>();
                if (uc != null) target = uc.transform;
                else { var p = GameObject.FindGameObjectWithTag(followTag); if (p != null) target = p.transform; }
            }
            if (target == null) return;
            transform.position = target.position + followOffset;
            UpdateGroundAndShelter();
        }

        void UpdateGroundAndShelter()
        {
            // floor under the player (for splashes / mist)
            Vector3 head = target.position + Vector3.up * 1.6f;
            if (Physics.Raycast(head, Vector3.down, out RaycastHit down, 60f, groundMask, QueryTriggerInteraction.Ignore))
            { groundY = down.point.y; hasGround = true; }
            if (groundFollower != null && hasGround)
            {
                Vector3 p = target.position; p.y = groundY + 0.02f;
                groundFollower.position = p;
            }

            // roof directly above the player -> thin the rain out and muffle the sound
            bool covered = false;
            if (!disableShelter)
            {
                var hits = Physics.RaycastAll(head, Vector3.up, shelterCheckHeight, groundMask, QueryTriggerInteraction.Ignore);
                foreach (var h in hits)
                {
                    // ignore the player, soldiers, weapons and their torches
                    if (h.collider.transform.IsChildOf(target.root) || h.collider.GetComponentInParent<scgFullBodyController.AiController>() != null) continue;
                    if (h.collider.attachedRigidbody != null) continue;
                    covered = true; break;
                }
            }
            isSheltered = covered;
            shelter = Mathf.MoveTowards(shelter, covered ? 1f : 0f, Time.deltaTime * 1.5f);
            bool raining = weather > 0.01f;
            if (groundFollower != null && groundFollower.gameObject.activeSelf != raining) groundFollower.gameObject.SetActive(raining);
            if (rainSystems != null && baseRates != null)
                for (int i = 0; i < rainSystems.Length; i++)
                {
                    if (rainSystems[i] == null) continue;
                    if (raining && !rainSystems[i].isPlaying) rainSystems[i].Play();
                    var em = rainSystems[i].emission;
                    em.rateOverTime = new ParticleSystem.MinMaxCurve(baseRates[i] * Mathf.Lerp(1f, shelteredRainMultiplier, shelter) * weather);
                }
            if (rainAudio != null)
            {
                rainAudio.volume = rainAudioVolume * Mathf.Lerp(1f, shelteredAudioMultiplier, shelter) * weather;
                if (raining && !rainAudio.isPlaying) rainAudio.Play();
                else if (!raining && rainAudio.isPlaying) rainAudio.Pause();
            }
        }

        IEnumerator LightningLoop()
        {
            yield return new WaitForSeconds(Random.Range(4f, 9f));
            while (true)
            {
                while (weather < 0.6f) yield return new WaitForSeconds(1f);        // clear morning: no storm
                float delay = Random.Range(thunderDelay.x, thunderDelay.y);
                yield return Flash();
                StartCoroutine(PlayThunder(delay));
                yield return new WaitForSeconds(Random.Range(interval.x, interval.y));
            }
        }

        IEnumerator Flash()
        {
            if (moonLight != null) { baseIntensity = moonLight.intensity; baseColor = moonLight.color; }
            baseAmbient = RenderSettings.ambientLight;
            int strikes = Random.value < 0.5f ? 2 : 1; // double flicker
            for (int i = 0; i < strikes; i++)
            {
                SetFlash(1f);
                yield return new WaitForSeconds(Random.Range(0.05f, 0.1f));
                SetFlash(0.15f);
                yield return new WaitForSeconds(Random.Range(0.04f, 0.09f));
            }
            // fade out
            for (float t = 0; t < 1; t += Time.deltaTime * 4f)
            {
                SetFlash(Mathf.Lerp(0.6f, 0f, t));
                yield return null;
            }
            SetFlash(0f);
        }

        void SetFlash(float k)
        {
            if (moonLight != null)
            {
                moonLight.intensity = Mathf.Lerp(baseIntensity, flashIntensity, k);
                moonLight.color = Color.Lerp(baseColor, flashColor, k);
            }
            RenderSettings.ambientLight = Color.Lerp(baseAmbient, flashColor * 0.6f, k);
        }

        IEnumerator PlayThunder(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (thunderSource == null) yield break;
            bool near = delay < (thunderDelay.x + thunderDelay.y) * 0.4f;
            var set = near ? thunderNear : thunderFar;
            if (set == null || set.Length == 0) yield break;
            thunderSource.PlayOneShot(set[Random.Range(0, set.Length)], near ? 1f : 0.8f);
        }
    }
}
