using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using scgFullBodyController;

namespace GAMECON.PlayerTools
{
    /// <summary>
    /// Player life bar + damage feedback: the bar drains (with a white "trail"), the screen edges flash red when
    /// you are hit, blood splatters land on the screen and fade, and a red pulse warns you when life is low.
    /// </summary>
    public class PlayerHealthUI : MonoBehaviour
    {
        [Header("Bar")]
        public Image fill;
        public Image trail;
        public Text label;
        public Gradient colour;

        [Header("Screen effects")]
        public Image vignette;
        public RectTransform splatterRoot;
        public Sprite[] splatSprites;

        [Header("Colours")]
        public Color healthyColor = new Color(0.62f, 0.82f, 0.28f, 1f);     // olive green
        public Color lowColor = new Color(0.92f, 0.22f, 0.14f, 1f);      // signal red, used once life is 50% or less

        public HealthController Health { get; private set; }
        bool oldHudHidden;
        bool lowWarned;

        float lastHealth = -1f;
        float flash;                       // red vignette flash from a fresh hit
        float trailValue = 1f;
        readonly List<Splat> splats = new List<Splat>();

        class Splat { public Image img; public float born, life; public float a0; }

        void Update()
        {
            // the original HUD life number ("350 +") is replaced by the life bar
            if (!oldHudHidden)
            {
                var hud = FindObjectOfType<hudController>();
                if (hud != null && hud.uiHealth != null) { hud.uiHealth.gameObject.SetActive(false); oldHudHidden = true; }
            }

            if (Health == null)
            {
                var uc = FindObjectOfType<ThirdPersonUserControl>();
                if (uc != null) Health = uc.GetComponent<HealthController>();
            }

            float max = 1f, cur = 0f;
            if (Health != null)
            {
                max = Mathf.Max(1f, Health.MaxHealth > 0f ? Health.MaxHealth : Health.health);
                cur = Mathf.Clamp(Health.health, 0f, max);
            }
            float frac = Mathf.Clamp01(cur / max);

            // detect damage (regen only ever increases, so a decrease = a hit)
            if (lastHealth >= 0f && Health != null && cur < lastHealth - 0.01f)
                OnHit((lastHealth - cur) / max);
            lastHealth = Health != null ? cur : -1f;

            // voice warning once when life gets low (re-armed above 50%)
            if (Health != null)
            {
                if (frac < 0.3f && frac > 0f && !lowWarned) { lowWarned = true; GAMECON.Voice.VoiceAssistant.Say(GAMECON.Voice.VoiceLine.LowHealth); }
                else if (frac > 0.5f) lowWarned = false;
            }

            // bar
            Color barCol = frac > 0.5f ? healthyColor : lowColor;
            if (fill != null)
            {
                fill.fillAmount = frac;
                Color c = barCol;
                if (frac <= 0.25f && frac > 0f) c = Color.Lerp(c, Color.white, 0.30f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f)));   // warning blink
                fill.color = c;
            }
            trailValue = Mathf.MoveTowards(trailValue, frac, Time.deltaTime * 0.35f);
            if (trailValue < frac) trailValue = frac;
            if (trail != null) trail.fillAmount = trailValue;
            if (label != null) label.text = "LIFE  " + Mathf.CeilToInt(cur) + " / " + Mathf.CeilToInt(max);

            // vignette: fresh-hit flash + slow red pulse when low
            flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 1.6f);
            float low = frac < 0.35f ? (1f - frac / 0.35f) : 0f;
            float pulse = low * (0.28f + 0.18f * Mathf.Sin(Time.time * 6.5f));
            if (vignette != null)
            {
                var c = vignette.color; c.a = Mathf.Clamp01(flash * 0.85f + pulse); vignette.color = c;
            }

            // splatters
            for (int i = splats.Count - 1; i >= 0; i--)
            {
                var s = splats[i];
                float age = Time.time - s.born;
                if (age >= s.life || s.img == null) { if (s.img != null) Destroy(s.img.gameObject); splats.RemoveAt(i); continue; }
                float f = age < 0.15f ? age / 0.15f : 1f - Mathf.Clamp01((age - s.life * 0.35f) / (s.life * 0.65f));
                var c = s.img.color; c.a = s.a0 * Mathf.Clamp01(f); s.img.color = c;
                // blood slowly runs down the screen
                var rt = s.img.rectTransform; rt.anchoredPosition += Vector2.down * (Time.deltaTime * 14f);
            }
        }

        void OnHit(float fractionLost)
        {
            flash = Mathf.Clamp01(flash + 0.5f + fractionLost * 2f);
            trailValue = Mathf.Max(trailValue, Mathf.Clamp01(fractionLost + 0.0f) + (Health != null ? Mathf.Clamp01(Health.health / Mathf.Max(1f, Health.MaxHealth)) : 0f));
            if (splatterRoot == null || splatSprites == null || splatSprites.Length == 0) return;
            int n = 1 + Mathf.Min(3, Mathf.RoundToInt(fractionLost * 12f));
            for (int i = 0; i < n; i++) AddSplat();
        }

        void AddSplat()
        {
            if (splats.Count > 14) { var o = splats[0]; if (o.img != null) Destroy(o.img.gameObject); splats.RemoveAt(0); }
            var go = new GameObject("Splat", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(splatterRoot, false);
            var img = go.GetComponent<Image>();
            img.sprite = splatSprites[Random.Range(0, splatSprites.Length)];
            img.raycastTarget = false;
            float a0 = Random.Range(0.6f, 0.9f);
            img.color = new Color(0.55f, 0.02f, 0.03f, 0f);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(Random.value, Random.value);
            // favour the screen edges, where splatters read best
            if (Random.value < 0.65f)
            {
                float edge = Random.value < 0.5f ? Random.Range(0.02f, 0.2f) : Random.Range(0.8f, 0.98f);
                if (Random.value < 0.5f) rt.anchorMin = rt.anchorMax = new Vector2(edge, Random.value);
                else rt.anchorMin = rt.anchorMax = new Vector2(Random.value, edge);
            }
            rt.anchoredPosition = Vector2.zero;
            float size = Random.Range(220f, 520f);
            rt.sizeDelta = new Vector2(size, size);
            rt.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            splats.Add(new Splat { img = img, born = Time.time, life = Random.Range(3.5f, 5.5f), a0 = a0 });
        }
    }
}
