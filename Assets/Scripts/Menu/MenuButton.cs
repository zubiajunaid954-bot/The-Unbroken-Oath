using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GAMECON.Menu
{
    /// <summary>
    /// A button that feels alive: it slides in, a light sweeps across it, the frame and diamond react to the pointer / keyboard selection
    /// (glow, gold text, a small push to the left, a turning diamond), it squashes and flashes when pressed and it ticks and clicks.
    /// </summary>
    public class MenuButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        public RectTransform root;
        public CanvasGroup group;
        public Image fill, frame, glow, shine, flash, diamond, arrow;
        public Text label;
        public bool primary;                               // the PLAY button pulses gently
        public bool minimal;                               // text menu style: no box, a thin line, a wash and a diamond on hover
        public bool centered;
        public AudioSource sfx;
        public AudioClip hoverClip, clickClip;

        public event Action Clicked;
        public event Action<MenuButton> Hovered;

        public bool Interactable = true;
        public bool IsSelected { get; private set; }

        public static readonly Color Amber = new Color(1f, 0.78f, 0.22f, 1f);
        static readonly Color AmberBright = new Color(1f, 0.90f, 0.50f, 1f);
        static readonly Color PanelIdle = new Color(0.07f, 0.09f, 0.075f, 0.88f);
        static readonly Color PanelHover = new Color(0.16f, 0.14f, 0.07f, 0.94f);
        static readonly Color TextIdle = new Color(0.93f, 0.94f, 0.87f, 1f);
        static readonly Color TextHover = new Color(1f, 0.86f, 0.42f, 1f);

        Vector2 basePos;
        float hover, press, flashA, appearAt = -1f, phase;
        bool pointerOver, pointerDown, baseCaptured, shown;
        float labelBaseX;
        public Vector2 slideFrom = new Vector2(140f, 0f);

        void Awake() { Capture(); phase = UnityEngine.Random.value * 5f; }

        void Capture()
        {
            if (baseCaptured || root == null) return;
            basePos = root.anchoredPosition; baseCaptured = true;
            if (label != null) labelBaseX = label.rectTransform.anchoredPosition.x;
        }

        /// <summary>Slide the button in after "delay" seconds.</summary>
        public void Show(float delay)
        {
            Capture();
            appearAt = Time.unscaledTime + delay; shown = true;
        }

        public void HideInstantly()
        {
            Capture(); shown = false; appearAt = -1f;
            if (group != null) group.alpha = 0f;
        }

        public void ShowInstantly() { Capture(); shown = true; appearAt = Time.unscaledTime - 5f; }

        public void SetSelected(bool selected) { IsSelected = selected; }

        /// <summary>Activates the button as if it had been clicked (keyboard).</summary>
        public void Trigger()
        {
            if (!Interactable || !shown) return;
            PlayClick();
            flashA = 1f; press = 1f;
            if (Clicked != null) Clicked();
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (!Interactable || !shown) return;
            pointerOver = true;
            if (sfx != null && hoverClip != null) sfx.PlayOneShot(hoverClip, 0.8f);
            if (Hovered != null) Hovered(this);
        }

        public void OnPointerExit(PointerEventData e) { pointerOver = false; pointerDown = false; }
        public void OnPointerDown(PointerEventData e) { if (Interactable && shown) pointerDown = true; }
        public void OnPointerUp(PointerEventData e) { pointerDown = false; }

        public void OnPointerClick(PointerEventData e)
        {
            if (!Interactable || !shown || e.button != PointerEventData.InputButton.Left) return;
            PlayClick();
            flashA = 1f;
            if (Clicked != null) Clicked();
        }

        void PlayClick() { if (sfx != null && clickClip != null) sfx.PlayOneShot(clickClip, 1f); }

        void UpdateMinimal(float t, float hover, float press, float k)
        {
            float pulse = primary ? 0.5f + 0.5f * Mathf.Sin(t * 2.0f + phase) : 0f;
            if (frame != null)
            {
                float a = Mathf.Lerp(primary ? 0.50f + 0.18f * pulse : 0.22f, 1f, hover);
                frame.color = new Color(Amber.r, Amber.g, Amber.b, a);
                frame.rectTransform.localScale = new Vector3(Mathf.Lerp(primary ? 0.78f : 0.55f, 1f, hover), 1f, 1f);
            }
            if (fill != null) fill.color = new Color(0.98f, 0.74f, 0.30f, 0.34f * hover + (primary ? 0.05f * pulse : 0f));
            if (flash != null) { var c = Color.white; c.a = 0.40f * flashA; flash.color = c; }
            if (diamond != null)
            {
                var c = Amber; c.a = Mathf.Clamp01(hover * 1.2f); diamond.color = c;
                diamond.rectTransform.localScale = Vector3.one * (0.4f + 0.7f * hover);
                diamond.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f + 90f * hover);
            }
            if (label != null)
            {
                label.color = Color.Lerp(new Color(0.93f, 0.91f, 0.84f), TextHover, hover);
                var lp = label.rectTransform.anchoredPosition; lp.x = labelBaseX + (centered ? 0f : 22f * hover); label.rectTransform.anchoredPosition = lp;
            }
        }

        static float Ease(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }

        void Update()
        {
            if (root == null) return;
            Capture();
            float t = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            float target = (shown && Interactable && (pointerOver || IsSelected)) ? 1f : 0f;
            hover = Mathf.Lerp(hover, target, 1f - Mathf.Exp(-13f * dt));
            press = Mathf.Lerp(press, pointerDown ? 1f : 0f, 1f - Mathf.Exp(-26f * dt));
            flashA = Mathf.MoveTowards(flashA, 0f, dt * 3.2f);

            float k = appearAt < 0f ? 0f : Ease((t - appearAt) / 0.65f);
            if (!shown) k = 0f;
            if (group != null) group.alpha = k * (Interactable ? 1f : 0.45f);
            if (group != null) { group.blocksRaycasts = k > 0.6f; }

            Vector2 pos = basePos + slideFrom * (1f - k) + new Vector2(minimal ? 0f : -14f * hover, 0f);
            root.anchoredPosition = pos;
            root.localScale = Vector3.one * (1f + (minimal ? 0.015f : 0.045f) * hover - (minimal ? 0.03f : 0.04f) * press);
            if (minimal) { UpdateMinimal(t, hover, press, k); return; }

            float pulse = primary ? 0.5f + 0.5f * Mathf.Sin(t * 2.2f + phase) : 0f;
            if (frame != null) { var c = Color.Lerp(new Color(Amber.r, Amber.g, Amber.b, 0.72f + 0.2f * pulse), AmberBright, hover); frame.color = c; }
            if (fill != null) fill.color = Color.Lerp(PanelIdle, PanelHover, hover);
            if (glow != null) { var c = Amber; c.a = Mathf.Clamp01(0.62f * hover + (primary ? 0.10f + 0.10f * pulse : 0f)); glow.color = c; }

            // the light sweep: a slow one now and then, a quick one while hovered
            if (shine != null)
            {
                float speed = Mathf.Lerp(0.20f, 0.75f, hover);
                float s = Mathf.Repeat(t * speed + phase * 0.13f, 1.6f) - 0.3f;                    // -0.3 .. 1.3 across the button
                float w = root.rect.width;
                shine.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-w * 0.5f, w * 0.5f, s), 0f);
                float edge = Mathf.Clamp01(Mathf.Min(s + 0.1f, 1.15f - s) * 6f);
                var c = Color.white; c.a = (0.06f + 0.22f * hover) * edge; shine.color = c;
            }

            if (flash != null) { var c = Color.white; c.a = 0.45f * flashA; flash.color = c; }

            if (diamond != null)
            {
                diamond.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f + 180f * hover);
                diamond.rectTransform.localScale = Vector3.one * (1f + 0.55f * hover + 0.1f * pulse);
                diamond.color = Color.Lerp(new Color(Amber.r, Amber.g, Amber.b, 0.85f), Color.white, 0.5f * hover);
            }
            if (arrow != null)
            {
                var c = Amber; c.a = hover; arrow.color = c;
                arrow.rectTransform.anchoredPosition = new Vector2(-34f + 12f * hover + 4f * Mathf.Sin(t * 7f) * hover, 0f);
            }
            if (label != null)
            {
                label.color = Color.Lerp(TextIdle, TextHover, hover);
                var lp = label.rectTransform.anchoredPosition; lp.x = labelBaseX + 12f * hover; label.rectTransform.anchoredPosition = lp;
            }
        }
    }
}
