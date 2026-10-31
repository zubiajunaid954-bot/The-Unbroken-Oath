using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using GAMECON.Mission;

namespace GAMECON.Menu
{
    /// <summary>
    /// The story scene: a few lines of text over a dark, slowly breathing background, with a SKIP INTRO button (SPACE / ENTER / ESC also skip).
    /// It ends in the main game scene. The lines are editable in the inspector.
    /// </summary>
    public class StoryIntro : MonoBehaviour
    {
        [TextArea(2, 4)]
        public string[] lines =
        {
            "<color=#ffc83d>OPERATION  SUN  TEMPLE</color>",
            "Four warriors.  One faith.  An unbreakable bond.",
            "Duaa, Zubia and Maha were taken in the night.",
            "They are held inside the temple, in three separate rooms behind locked doors.",
            "Samrah, you are the only one left.",
            "Go in.  Bring them home.",
            "<color=#ffc83d>The oath will not be broken.</color>",
        };
        public string nextScene = "SunTemple_Night";

        [Header("Intro video (Assets/StreamingAssets)")]
        public string videoFile = "GAME.mp4";
        public VideoPlayer video;
        public RawImage videoImage;
        public AudioSource videoAudio;
        public GameObject decor;                       // the dark background decoration of the text version (hidden while the video plays)

        public Text bodyText;
        public CanvasGroup bodyGroup;
        public MenuButton skipButton;
        public CanvasGroup hintGroup;
        public Image glow, fade;
        public AudioSource music, sfx;
        public AudioClip hoverClip, clickClip;

        bool leaving;
        float born;
        int lastLog = -1;

        void Awake()
        {
            GameSettings.Apply();
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            Font f = FailScreen.GetFont();
            foreach (var t in FindObjectsOfType<Text>(true)) t.font = f;
            if (skipButton != null && skipButton.label != null) skipButton.label.font = FailScreen.GetSerifFont();
        }

        void Start()
        {
            born = Time.unscaledTime;
            if (fade != null) fade.color = Color.black;
            if (bodyGroup != null) bodyGroup.alpha = 0f;
            if (hintGroup != null) hintGroup.alpha = 0f;
            if (skipButton != null)
            {
                skipButton.sfx = sfx; skipButton.hoverClip = hoverClip; skipButton.clickClip = clickClip;
                skipButton.HideInstantly(); skipButton.Show(1.2f); skipButton.Clicked += Proceed; skipButton.SetSelected(true);
            }
            if (music != null) { music.volume = 0f; music.loop = true; music.Play(); }       // (stopped again when the video plays)
            StartCoroutine(Run());
        }

        void Update()
        {
            float t = Time.unscaledTime - born;
            if (fade != null && !leaving) { var c = fade.color; c.a = Mathf.Clamp01(1f - t / 1.2f); fade.color = c; }
            if (music != null && music.isPlaying) music.volume = leaving ? Mathf.MoveTowards(music.volume, 0f, Time.unscaledDeltaTime / 1.0f) : GameSettings.Music * 0.8f * Mathf.Clamp01(t / 3f);
            if (leaving && videoAudio != null) videoAudio.volume = Mathf.MoveTowards(videoAudio.volume, 0f, Time.unscaledDeltaTime / 1.0f);
            if (glow != null) { var c = glow.color; c.a = 0.16f + 0.07f * Mathf.Sin(Time.unscaledTime * 0.9f); glow.color = c; }
            if (hintGroup != null && t > 1.6f) hintGroup.alpha = Mathf.Clamp01((t - 1.6f) / 1.0f) * 0.7f;
            if (!leaving && t > 0.4f && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Escape)))
            {
                if (skipButton != null) skipButton.Trigger(); else Proceed();
            }
        }

        // ---------------------------------------------------------------------------------------------- the intro video
        IEnumerator Run()
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, videoFile);
            if (video == null || videoImage == null || !System.IO.File.Exists(path))
            {
                Debug.LogWarning("[StoryIntro] intro video not found (" + path + "): showing the text version.");
                yield return Play();
                yield break;
            }

            // the video replaces the text version
            if (decor != null) decor.SetActive(false);
            if (music != null) music.Stop();
            var rt = new RenderTexture(848, 480, 0, RenderTextureFormat.ARGB32);
            rt.Create();
            video.playOnAwake = false; video.isLooping = false; video.skipOnDrop = true;
            video.source = VideoSource.Url; video.url = path;
            video.renderMode = VideoRenderMode.RenderTexture; video.targetTexture = rt;
            video.audioOutputMode = VideoAudioOutputMode.AudioSource;
            video.controlledAudioTrackCount = 1; video.EnableAudioTrack(0, true); video.SetTargetAudioSource(0, videoAudio);
            videoImage.texture = rt;
            bool failed = false, finished = false;
            video.errorReceived += (v, msg) => { failed = true; Debug.LogWarning("[StoryIntro] video error: " + msg); };
            video.loopPointReached += v => finished = true;
            video.Prepare();
            float wait = 0f;
            while (!video.isPrepared && !failed && wait < 12f && !leaving) { wait += Time.unscaledDeltaTime; yield return null; }
            if (failed || !video.isPrepared)
            {
                if (!leaving)
                {
                    Debug.LogWarning("[StoryIntro] the video could not be played: showing the text version.");
                    videoImage.gameObject.SetActive(false);
                    if (decor != null) decor.SetActive(true);
                    if (music != null) music.Play();
                    yield return Play();
                }
                yield break;
            }

            float len = (float)video.length;
            Debug.Log("[StoryIntro] video prepared, length " + len.ToString("0.0") + " s, " + video.width + "x" + video.height + ", audio tracks " + video.audioTrackCount);
            video.Play();
            float played = 0f;
            while (!finished && !failed && !leaving && played < len + 3f)
            {
                played += Time.unscaledDeltaTime;
                if (Mathf.FloorToInt(played) % 5 == 0 && Mathf.FloorToInt(played) != lastLog) { lastLog = Mathf.FloorToInt(played); Debug.Log("[StoryIntro] video time " + video.time.ToString("0.0") + " playing=" + video.isPlaying + " frame=" + video.frame); }
                videoImage.color = new Color(1f, 1f, 1f, Mathf.Clamp01(played / 0.4f));
                yield return null;
            }
            Debug.Log("[StoryIntro] video ended (finished=" + finished + ", failed=" + failed + ", skipped=" + leaving + ")");
            if (!leaving) Proceed();
        }

        IEnumerator Play()
        {
            yield return Wait(1.6f);
            for (int i = 0; i < lines.Length && !leaving; i++)
            {
                string line = lines[i];
                bodyText.text = "";
                // fade in while typing
                float t = 0f; float typeTime = Mathf.Max(0.8f, Plain(line).Length / 34f);
                while (t < typeTime && !leaving)
                {
                    t += Time.unscaledDeltaTime;
                    bodyGroup.alpha = Mathf.Clamp01(t / 0.5f);
                    bodyText.text = Reveal(line, Mathf.Clamp01(t / typeTime));
                    yield return null;
                }
                bodyText.text = line; bodyGroup.alpha = 1f;
                yield return Wait(1.7f + Plain(line).Length * 0.025f);
                t = 0f;
                while (t < 0.7f && !leaving) { t += Time.unscaledDeltaTime; bodyGroup.alpha = 1f - Mathf.Clamp01(t / 0.7f); yield return null; }
                bodyGroup.alpha = 0f;
                yield return Wait(0.35f);
            }
            if (!leaving) Proceed();
        }

        static IEnumerator Wait(float s) { float t = 0f; while (t < s) { t += Time.unscaledDeltaTime; yield return null; } }

        void Proceed()
        {
            if (leaving) return;
            leaving = true;
            SceneTransition.Load(nextScene, 1.0f, 1.2f);
        }

        // ---- typewriter that keeps rich text tags intact ----
        static string Plain(string s)
        {
            var sb = new System.Text.StringBuilder(); bool tag = false;
            foreach (char c in s) { if (c == '<') tag = true; else if (c == '>') tag = false; else if (!tag) sb.Append(c); }
            return sb.ToString();
        }

        static string Reveal(string s, float k)
        {
            int visible = Mathf.CeilToInt(Plain(s).Length * k), seen = 0;
            var sb = new System.Text.StringBuilder(); bool tag = false; var open = new System.Collections.Generic.Stack<string>();
            string cur = "";
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<') { tag = true; cur = ""; }
                if (tag)
                {
                    sb.Append(c); cur += c;
                    if (c == '>')
                    {
                        tag = false;
                        if (cur.StartsWith("</")) { if (open.Count > 0) open.Pop(); } else open.Push(cur);
                    }
                    continue;
                }
                if (seen >= visible) break;
                sb.Append(c); seen++;
            }
            foreach (var o in open) { if (o.StartsWith("<color")) sb.Append("</color>"); else if (o.StartsWith("<b")) sb.Append("</b>"); else if (o.StartsWith("<i")) sb.Append("</i>"); }
            return sb.ToString();
        }
    }
}
