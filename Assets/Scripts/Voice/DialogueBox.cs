using UnityEngine;
using UnityEngine.UI;

namespace GAMECON.Voice
{
    /// <summary>
    /// The speech-bubble dialogue box at the bottom of the screen: a cream bubble with "Operator - text" typed out letter by letter,
    /// with the operator's portrait beside it (like a visual-novel / adventure game dialogue).
    /// </summary>
    public class DialogueBox : MonoBehaviour
    {
        public RectTransform root;
        public CanvasGroup group;
        public RectTransform portrait;
        public Text body;
        public Sprite operatorPortrait, samrahPortrait;      // the picture beside the bubble follows who is speaking
        Image portraitImage;
        public string nameColour = "#ffc83d";      // amber: matches the tactical HUD

        string prefix = "";
        string full = "";
        float born, duration, typeTime;
        bool active;
        float basePortraitY;
        Vector2 baseRootPos;
        bool init;

        void Init()
        {
            if (init) return;
            init = true;
            baseRootPos = root.anchoredPosition;
            if (portrait != null)
            {
                basePortraitY = portrait.anchoredPosition.y;
                portraitImage = portrait.GetComponent<Image>();
                if (operatorPortrait == null && portraitImage != null) operatorPortrait = portraitImage.sprite;
            }
            if (group != null) group.alpha = 0f;
        }

        void Start() { Init(); }

        public void Show(string speaker, string text, float seconds)
        {
            Init();
            prefix = "<b><color=" + nameColour + ">" + speaker + " —</color></b>  ";
            if (portraitImage != null)
            {
                bool samrah = speaker == "Samrah";
                Sprite sp = samrah ? samrahPortrait : operatorPortrait;
                if (sp != null) portraitImage.sprite = sp;
                portraitImage.enabled = sp != null;                // no picture (rather than the wrong one) if a portrait is missing
            }
            full = text;
            born = Time.unscaledTime;
            duration = Mathf.Max(1.2f, seconds);
            typeTime = Mathf.Max(0.5f, duration * 0.72f);          // the whole line is typed before the voice finishes
            active = true;
            gameObject.SetActive(true);
        }

        /// <summary>Fade the box out now.</summary>
        public void Hide()
        {
            if (!active) return;
            born = Time.unscaledTime - duration;     // the "line is over" moment: the fade-out starts
        }

        void Update()
        {
            Init();
            float age = Time.unscaledTime - born;

            if (active && age > duration) active = false;

            // slide up + fade in, fade out at the end
            float fadeIn = Mathf.Clamp01(age / 0.25f);
            float fadeOut = active ? 1f : Mathf.Clamp01(1f - (age - duration) / 0.35f);
            if (group != null) group.alpha = fadeIn * fadeOut;
            float e = fadeIn * fadeIn * (3f - 2f * fadeIn);
            root.anchoredPosition = baseRootPos + new Vector2(0f, Mathf.Lerp(-50f, 0f, e));
            if (!active && fadeOut <= 0f) { body.text = ""; return; }

            // typewriter
            int len = full.Length;
            int n = Mathf.Clamp(Mathf.FloorToInt(len * Mathf.Clamp01(age / typeTime)), 0, len);
            body.text = prefix + full.Substring(0, n);

            // the operator "talks": a small bounce while the text is still being typed
            if (portrait != null)
            {
                bool talking = n < len;
                float bob = talking ? Mathf.Abs(Mathf.Sin(age * 9f)) * 7f : 0f;
                var p = portrait.anchoredPosition; p.y = Mathf.Lerp(p.y, basePortraitY + bob, Time.unscaledDeltaTime * 18f);
                portrait.anchoredPosition = p;
                float s = talking ? 1f + 0.015f * Mathf.Sin(age * 9f) : 1f;
                portrait.localScale = Vector3.Lerp(portrait.localScale, new Vector3(s, s, 1f), Time.unscaledDeltaTime * 12f);
            }
        }
    }
}
