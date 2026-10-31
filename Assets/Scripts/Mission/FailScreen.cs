using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GAMECON.Mission
{
    /// <summary>
    /// The MISSION FAILED screen in the game's tactical amber / olive style: a dark cinematic backdrop, the operation tag, a big red title,
    /// a status panel with each teammate (rescued / still captive) and a pulsing "press ENTER to try again". Everything fades and slides in.
    /// </summary>
    public class FailScreen : MonoBehaviour
    {
        class Item { public RectTransform rt; public CanvasGroup g; public float delay, slide; public Vector2 basePos; public float scaleFrom = 1f; }
        readonly List<Item> items = new List<Item>();
        CanvasGroup backdrop;
        CanvasGroup prompt;
        float born;
        AudioSource music;
        readonly Dictionary<AudioSource, float> ducked = new Dictionary<AudioSource, float>();
        float musicVolume = 0.8f;

        static readonly Color Amber = new Color(1f, 0.78f, 0.22f, 1f);
        static readonly Color Panel = new Color(0.07f, 0.09f, 0.075f, 0.94f);
        static readonly Color Red = new Color(0.93f, 0.20f, 0.14f, 1f);
        static readonly Color Green = new Color(0.62f, 0.82f, 0.28f, 1f);

        static Font font;
        static Font serif;
        /// <summary>The serif face of the title (Constantia on Windows) for the menu entries and panel titles.</summary>
        public static Font GetSerifFont()
        {
            if (serif != null) return serif;
            try { serif = Font.CreateDynamicFontFromOSFont(new[] { "Constantia", "Georgia", "Times New Roman" }, 64); }
            catch { serif = null; }
            if (serif == null) serif = GetFont();
            return serif;
        }

        public static Font GetFont()
        {
            if (font != null) return font;
            // the font that ships with the game (Rajdhani, SIL Open Font License): the same look on every PC and in the build
            font = Resources.Load<Font>("Fonts/Rajdhani-SemiBold");
            if (font != null) return font;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");        // (never an OS font: a variable font such as Bahnschrift is drawn squeezed in a build)
            return font;
        }

        class Row { public string name, status; public bool ok; }
        class Config
        {
            public string tag = "OPERATION  SUN  TEMPLE", title, subtitle, header, countText, promptTail, music;
            public Color titleColor, okColor = Green, badColor = Red;
            public List<Row> rows = new List<Row>();
            public KeyCode key; public float musicVol = 0.8f;
        }

        /// <summary>MISSION FAILED.</summary>
        public static void Show(List<Hostage> hostages, KeyCode restartKey, string subtitle)
        {
            var c = new Config
            {
                title = "MISSION FAILED", titleColor = Red, key = restartKey, header = "TEAMMATES", promptTail = "TO TRY AGAIN", music = "Fail_Music", musicVol = 0.8f,
                subtitle = string.IsNullOrEmpty(subtitle) ? "Samrah is down. The mission cannot continue." : subtitle
            };
            int rescued = 0, n = hostages != null ? hostages.Count : 3;
            for (int i = 0; i < n; i++)
            {
                var hs = hostages != null && i < hostages.Count ? hostages[i] : null;
                bool freed = hs != null && hs.Freed; if (freed) rescued++;
                c.rows.Add(new Row { name = hs != null ? hs.displayName.ToUpper() : "TEAMMATE", ok = freed, status = freed ? "RESCUED" : "STILL CAPTIVE" });
            }
            c.countText = rescued + " / " + Mathf.Max(1, n) + "  RESCUED";
            Spawn(c);
        }

        /// <summary>MISSION COMPLETE: the whole squad is back together.</summary>
        public static void ShowVictory(KeyCode restartKey)
        {
            var c = new Config
            {
                title = "MISSION COMPLETE", titleColor = new Color(1f, 0.82f, 0.28f, 1f), key = restartKey, header = "SQUAD", promptTail = "TO PLAY AGAIN",
                music = "Victory_Outro", musicVol = 0.75f, subtitle = "The squad is reunited. Together, stronger and better.",
                countText = "4 / 4  SAFE", okColor = Green
            };
            foreach (var n in new[] { "SAMRAH", "DUAA", "ZUBIA", "MAHA" }) c.rows.Add(new Row { name = n, ok = true, status = "SAFE" });
            Spawn(c);
        }

        static void Spawn(Config cfg)
        {
            var go = new GameObject("FailScreen", typeof(Canvas), typeof(CanvasScaler));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 700;
            var sc = go.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;           // the whole 1920x1080 layout always fits, on any screen shape
            go.AddComponent<FailScreen>().Build(cfg);
        }

        void Build(Config cfg)
        {
            born = Time.unscaledTime;
            var root = (RectTransform)transform;
            musicVolume = cfg.musicVol;

            // background music; the rest of the world's sound is turned down
            foreach (var a in FindObjectsOfType<AudioSource>()) ducked[a] = a.volume;
            music = gameObject.AddComponent<AudioSource>();
            music.clip = Resources.Load<AudioClip>("Audio/Cutscene/" + cfg.music);
            music.loop = true; music.spatialBlend = 0f; music.volume = 0f; music.playOnAwake = false;
            if (music.clip != null) music.Play();
            else Debug.LogWarning("[FailScreen] Resources/Audio/Cutscene/" + cfg.music + " not found");

            // backdrop: dark, with cinematic bars at the top and bottom
            var bd = NewRect("Backdrop", root); Stretch(bd);
            backdrop = bd.gameObject.AddComponent<CanvasGroup>();
            var dim = Img(bd, "Dim", Vector2.zero, Vector2.zero, new Color(0.02f, 0.025f, 0.02f, 0.66f)); Stretch(dim.rectTransform);
            var topBar = Img(bd, "Top", Vector2.zero, new Vector2(0, 150), new Color(0f, 0f, 0f, 0.7f)); Anchor(topBar.rectTransform, 0.5f, 1f, 1f); topBar.rectTransform.sizeDelta = new Vector2(0, 150);
            var botBar = Img(bd, "Bottom", Vector2.zero, new Vector2(0, 150), new Color(0f, 0f, 0f, 0.7f)); Anchor(botBar.rectTransform, 0.5f, 0f, 0f); botBar.rectTransform.sizeDelta = new Vector2(0, 150);
            topBar.rectTransform.anchorMin = new Vector2(0, 1); topBar.rectTransform.anchorMax = new Vector2(1, 1);
            botBar.rectTransform.anchorMin = new Vector2(0, 0); botBar.rectTransform.anchorMax = new Vector2(1, 0);

            // operation tag + divider
            Add(Label(root, cfg.tag, 30, Amber, FontStyle.Bold), new Vector2(0, 290), 0.5f, 22f);
            var line = NewRect("Divider", root); Place(line, new Vector2(0, 238), new Vector2(820, 2));
            var lineImg = line.gameObject.AddComponent<Image>(); lineImg.color = new Color(1f, 0.78f, 0.22f, 0.85f); lineImg.raycastTarget = false;
            Add(line, new Vector2(0, 238), 0.7f, 0f);
            var diamond = NewRect("Diamond", root); Place(diamond, new Vector2(0, 238), new Vector2(16, 16)); diamond.localRotation = Quaternion.Euler(0, 0, 45);
            var dImg = diamond.gameObject.AddComponent<Image>(); dImg.color = Amber; dImg.raycastTarget = false;
            Add(diamond, new Vector2(0, 238), 0.8f, 0f);

            // the title
            var title = Label(root, cfg.title, 150, cfg.titleColor, FontStyle.Bold);
            title.gameObject.GetComponent<Outline>().effectDistance = new Vector2(3f, -3f);
            var tit = Add(title, new Vector2(0, 118), 0.25f, 0f); tit.scaleFrom = 1.35f;
            Add(Label(root, cfg.subtitle, 36, new Color(0.92f, 0.93f, 0.86f, 0.9f), FontStyle.Normal), new Vector2(0, 14), 0.9f, 14f);

            // the status panel
            int n = Mathf.Max(1, cfg.rows.Count);
            float pw = 900f, ph = 170f;
            var frame = NewRect("PanelFrame", root); Place(frame, new Vector2(0, -130), new Vector2(pw, ph));
            var fi = frame.gameObject.AddComponent<Image>(); fi.color = Amber; fi.raycastTarget = false;
            var back = NewRect("PanelBack", frame); Stretch(back); back.offsetMin = new Vector2(3, 3); back.offsetMax = new Vector2(-3, -3);
            var bi = back.gameObject.AddComponent<Image>(); bi.color = Panel; bi.raycastTarget = false;
            var head = Label(frame, cfg.header, 22, Amber, FontStyle.Bold); head.alignment = TextAnchor.UpperLeft;
            Place(head.rectTransform, new Vector2(26, -14), new Vector2(300, 30), new Vector2(0f, 1f));
            var count = Label(frame, cfg.countText, 22, new Color(1f, 1f, 1f, 0.85f), FontStyle.Bold); count.alignment = TextAnchor.UpperRight;
            Place(count.rectTransform, new Vector2(-26, -14), new Vector2(360, 30), new Vector2(1f, 1f));
            var sep = NewRect("Sep", frame); Place(sep, new Vector2(0, -52), new Vector2(pw - 52, 1), new Vector2(0.5f, 1f));
            var si = sep.gameObject.AddComponent<Image>(); si.color = new Color(1f, 0.78f, 0.22f, 0.35f); si.raycastTarget = false;
            int perRow = n;
            float colW = (pw - 60f) / perRow;
            int nameSize = n > 3 ? 32 : 40;
            for (int i = 0; i < n; i++)
            {
                var row = cfg.rows[i];
                float cx = (i - (n - 1) * 0.5f) * colW;
                float dx = n > 3 ? 78f : 98f;
                var dot = NewRect("Dot" + i, frame); Place(dot, new Vector2(cx - dx - 22f, -100f), new Vector2(16, 16), new Vector2(0.5f, 1f)); dot.localRotation = Quaternion.Euler(0, 0, 45);
                var di = dot.gameObject.AddComponent<Image>(); di.color = row.ok ? cfg.okColor : cfg.badColor; di.raycastTarget = false;
                var nt = Label(frame, row.name, nameSize, Color.white, FontStyle.Bold); nt.alignment = TextAnchor.MiddleLeft;
                Place(nt.rectTransform, new Vector2(cx - dx, -82f), new Vector2(200, 46), new Vector2(0.5f, 1f)); nt.rectTransform.pivot = new Vector2(0f, 1f);
                nt.rectTransform.anchoredPosition = new Vector2(cx - dx, -76f);
                var st = Label(frame, row.status, 20, row.ok ? cfg.okColor : cfg.badColor, FontStyle.Bold); st.alignment = TextAnchor.MiddleLeft;
                Place(st.rectTransform, new Vector2(cx - dx, -122f), new Vector2(200, 28), new Vector2(0.5f, 1f)); st.rectTransform.pivot = new Vector2(0f, 1f);
                st.rectTransform.anchoredPosition = new Vector2(cx - dx, -120f);
            }
            Add(frame, new Vector2(0, -130), 1.2f, 30f);

            // the prompt (pulses)
            string key = cfg.key == KeyCode.Return ? "ENTER" : cfg.key.ToString().ToUpper();
            var pr = Label(root, "PRESS  <color=#ffc83d>[ " + key + " ]</color>  " + cfg.promptTail, 38, Color.white, FontStyle.Bold);
            pr.supportRichText = true;
            var pit = Add(pr, new Vector2(0, -300), 1.9f, 0f);
            prompt = pit.g;
        }

        void Update()
        {
            float t = Time.unscaledTime - born;
            if (backdrop != null) backdrop.alpha = Ease(t / 0.9f);
            if (music != null) music.volume = musicVolume * Ease((t - 0.3f) / 3.5f);                     // fades in slowly
            float duck = Mathf.Lerp(1f, 0.08f, Ease(t / 1.8f));
            foreach (var kv in ducked) if (kv.Key != null && kv.Key != music) kv.Key.volume = kv.Value * duck;
            foreach (var it in items)
            {
                float k = Ease((t - it.delay) / 0.65f);
                it.g.alpha = k;
                it.rt.anchoredPosition = it.basePos + new Vector2(0f, -it.slide * (1f - k));
                it.rt.localScale = Vector3.one * Mathf.Lerp(it.scaleFrom, 1f, k);
            }
            if (prompt != null && t > 1.9f) prompt.alpha = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin((t - 1.9f) * 3.2f));
        }

        // ------------------------------------------------------------------ helpers
        static float Ease(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }

        Item Add(Component c, Vector2 pos, float delay, float slide)
        {
            var rt = c.GetComponent<RectTransform>();
            var g = rt.gameObject.GetComponent<CanvasGroup>(); if (g == null) g = rt.gameObject.AddComponent<CanvasGroup>();
            g.alpha = 0f;
            var it = new Item { rt = rt, g = g, delay = delay, slide = slide, basePos = pos };
            items.Add(it);
            return it;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        static Image Img(Transform parent, string name, Vector2 pos, Vector2 size, Color c)
        {
            var rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>(); img.color = c; img.raycastTarget = false;
            return img;
        }

        static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }

        static void Anchor(RectTransform rt, float x, float y, float pivotY) { rt.anchorMin = rt.anchorMax = new Vector2(x, y); rt.pivot = new Vector2(0.5f, pivotY); }

        static void Place(RectTransform rt, Vector2 pos, Vector2 size, Vector2? anchor = null)
        {
            Vector2 a = anchor ?? new Vector2(0.5f, 0.5f);
            rt.anchorMin = rt.anchorMax = a; rt.pivot = a == new Vector2(0.5f, 0.5f) ? a : (a.y >= 1f ? new Vector2(a.x, 1f) : a);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
        }

        static Text Label(Transform parent, string text, int size, Color color, FontStyle style)
        {
            var rt = NewRect("Text", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = GetFont(); t.text = text; t.fontSize = size; t.color = color; t.fontStyle = style;
            t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            var o = rt.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, 0.9f); o.effectDistance = new Vector2(1.5f, -1.5f);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1500, size * 1.5f);
            return t;
        }
    }
}
