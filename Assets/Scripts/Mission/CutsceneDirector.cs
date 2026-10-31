using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using scgFullBodyController;
using GAMECON.Voice;

namespace GAMECON.Mission
{
    /// <summary>
    /// The rescue cut scene. When Samrah (the player) cuts a teammate free: black bars slide in, the camera moves in on the
    /// kneeling teammate, the ropes fall away, she stands up, turns to Samrah and says her line ("Samrah, I had faith in you...");
    /// then a reverse shot of Samrah's reply. Gameplay is frozen (Time.timeScale = 0) and restored afterwards.
    /// </summary>
    public partial class CutsceneDirector : MonoBehaviour
    {
        public static CutsceneDirector Instance { get; private set; }
        public bool Playing { get; private set; }

        public string playerName = "Samrah";
        public Sprite roundedSprite;           // optional fallback for builds
        GameObject bubble;

        static readonly Dictionary<string, string> thanks = new Dictionary<string, string>
        {
            { "Duaa",  "Samrah... I had faith in you. I knew you would come." },
            { "Zubia", "Samrah! I never doubted you. I knew you would find us." },
            { "Maha",  "Samrah, you came for us. I knew you would never leave us behind." },
        };
        // what Samrah says to each of them (every teammate gets her own answer)
        static readonly Dictionary<string, string> answers = new Dictionary<string, string>
        {
            { "Duaa",  "Duaa, you are safe now. I've got you." },
            { "Zubia", "Zubia, stay close to me. We are getting out of here." },
            { "Maha",  "Maha, hold on to me. I would never leave without you." },
        };
        static readonly string[] replies =
        {
            "Stay close. Let's get out of here.",
            "I've got you. We are leaving, together.",
            "Hold on. I am not leaving anyone behind.",
        };
        const string lastReply = "That's everyone. Time to get out of here.";

        void Awake() { Instance = this; }

        public void PlayRescue(Hostage h, bool last, Action onDone)
        {
            if (Playing) { if (onDone != null) onDone(); return; }
            StartCoroutine(Run(h, last, onDone));
        }

        // ------------------------------------------------------------------------------------------------
        IEnumerator Run(Hostage h, bool last, Action onDone)
        {
            Playing = true;
            var mgr = MissionManager.Instance;
            Transform player = mgr != null ? mgr.Player : null;
            Camera mainCam = Camera.main;

            // ---- freeze gameplay, hide the HUD, block the player ----
            float oldScale = Time.timeScale;
            Time.timeScale = 0f;
            var blocked = new List<Behaviour>();
            if (player != null)
                foreach (var b in player.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (b == null || !b.enabled) continue;
                    if (b is ThirdPersonUserControl || b is CameraController || b is GunController || b is GunManager)
                    { b.enabled = false; blocked.Add(b); }
                }
            // Samrah (the player) must not be seen in the cut scene: hide her body, her arms and her weapon
            var hiddenRenderers = new List<Renderer>();
            if (player != null)
                foreach (var r in player.GetComponentsInChildren<Renderer>(true))
                    if (r != null && r.enabled) { r.enabled = false; hiddenRenderers.Add(r); }
            var hidden = new List<Canvas>();
            foreach (var c in FindObjectsOfType<Canvas>())
                if (c.enabled) { c.enabled = false; hidden.Add(c); }

            // ---- cut scene camera + cinematic bars + subtitle ----
            var camGo = new GameObject("CutsceneCamera");
            var cam = camGo.AddComponent<Camera>();
            if (mainCam != null)
            {
                cam.CopyFrom(mainCam);
                cam.depth = mainCam.depth + 20f;
            }
            cam.fieldOfView = 42f;
            cam.nearClipPlane = 0.05f;
            camGo.tag = "Untagged";
            if (mainCam != null) mainCam.enabled = false;

            Text subtitle, nameLabel; RectTransform topBar, bottomBar;
            var ui = BuildUi(out subtitle, out nameLabel, out topBar, out bottomBar);

            // ---- geometry of the shot: everything is built around her FACE ----
            Transform holder = h.transform;
            Vector3 toPlayer = player != null ? player.position - holder.position : holder.forward;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 0.01f) toPlayer = holder.forward;
            toPlayer.Normalize();
            Vector3 facing0 = holder.forward; facing0.y = 0f; facing0.Normalize();
            Vector3 up = Vector3.up;

            // ---- 1. push in on the kneeling teammate, seen from the front (bars slide in) ----
            float t = 0f;
            float dur1 = 1.6f;
            while (t < dur1)
            {
                t += Time.unscaledDeltaTime;
                float k = Ease(t / dur1);
                SetBars(topBar, bottomBar, Mathf.Clamp01(t / 0.5f));
                Vector3 head = h.HeadWorld();
                Vector3 right = Vector3.Cross(up, facing0);
                float dist = Mathf.Lerp(2.3f, 1.6f, k);
                cam.transform.position = head + facing0 * dist + right * Mathf.Lerp(0.55f, 0.30f, k) + up * Mathf.Lerp(0.30f, 0.06f, k);
                cam.transform.rotation = Quaternion.LookRotation((head - cam.transform.position), up);
                yield return null;
            }

            // ---- 2. the ropes fall away, she stands up and turns to Samrah; the camera rises with her face ----
            h.CutRopes();
            Quaternion yaw0 = holder.rotation;
            Quaternion yaw1 = Quaternion.LookRotation(toPlayer, up);
            float stand = 4.2f; t = 0f;
            while (t < stand)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / stand);                     // the rig eases between its own key poses
                h.SetStanding(k);
                // she turns to Samrah while getting up (mostly during the lunge)
                holder.rotation = Quaternion.Slerp(yaw0, yaw1, Ease(Mathf.Clamp01((t - stand * 0.30f) / (stand * 0.55f))));
                Vector3 head = h.HeadWorld();
                Vector3 camDir = Vector3.Slerp(facing0, toPlayer, Ease(Mathf.Clamp01(t / stand)));
                Vector3 right = Vector3.Cross(up, camDir);
                float kk = Ease(k);
                cam.transform.position = head + camDir * Mathf.Lerp(1.6f, 1.75f, kk) + right * 0.30f + up * Mathf.Lerp(0.06f, 0.10f, kk);
                cam.transform.rotation = Quaternion.LookRotation(head - cam.transform.position, up);
                yield return null;
            }
            h.SetStanding(1f);
            holder.rotation = yaw1;
            yield return Wait(0.3f);

            // ---- 3. her line, to Samrah (camera in front of her face) ----
            string who = h.displayName;
            string line = thanks.ContainsKey(who) ? thanks[who] : "Samrah... I knew you would come.";
            var clip = Resources.Load<AudioClip>("Audio/Cutscene/" + who + "_Thanks");
            h.SetTalking(true);
            yield return SpeakFollow(clip, who.ToUpper(), line, subtitle, nameLabel, cam, h, toPlayer, 1.55f, 1.15f, 0.28f);
            h.SetTalking(false);

            // ---- 4. Samrah answers (off screen, her voice only) - the camera stays on the teammate, who listens ----
            string reply = answers.ContainsKey(who) ? answers[who] : replies[0];
            string clipName = answers.ContainsKey(who) ? "Samrah_To_" + who : "Samrah_Reply_0";
            var rclip = Resources.Load<AudioClip>("Audio/Cutscene/" + clipName);
            h.SetListening(true);
            yield return SpeakFollow(rclip, playerName.ToUpper(), reply, subtitle, nameLabel, cam, h, toPlayer, 1.15f, 1.0f, 0.22f);
            if (last)      // the final rescue: Samrah adds one more line
            {
                var lclip = Resources.Load<AudioClip>("Audio/Cutscene/Samrah_Last");
                yield return SpeakFollow(lclip, playerName.ToUpper(), lastReply, subtitle, nameLabel, cam, h, toPlayer, 1.0f, 0.95f, 0.2f);
            }
            h.SetListening(false);

            // ---- end: bars slide out, everything goes back ----
            t = 0f;
            while (t < 0.4f)
            {
                t += Time.unscaledDeltaTime;
                SetBars(topBar, bottomBar, 1f - Mathf.Clamp01(t / 0.4f));
                yield return null;
            }
            Destroy(ui);
            Destroy(camGo);
            if (mainCam != null) mainCam.enabled = true;
            foreach (var r in hiddenRenderers) if (r != null) r.enabled = true;
            foreach (var c in hidden) if (c != null) c.enabled = true;
            foreach (var b in blocked) if (b != null) b.enabled = true;
            Time.timeScale = oldScale <= 0f ? 1f : oldScale;
            Playing = false;
            if (onDone != null) onDone();
        }

        // keeps the camera in front of her face while she talks (she is facing "dir")
        IEnumerator SpeakFollow(AudioClip clip, string name, string text, Text subtitle, Text nameLabel, Camera cam, Hostage h,
                                Vector3 dir, float dist0, float dist1, float side)
        {
            float len = clip != null ? clip.length : 2.2f;
            if (clip != null) AudioSourceFor(cam).PlayOneShot(clip, 1f);
            string prefix = "<b><color=#ffc83d>" + Nice(name) + " \u2014</color></b>  ";
            if (bubble != null) bubble.SetActive(true);
            float dur = Mathf.Max(2.2f, len + 0.5f), t = 0f;
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Ease(t / dur);
                subtitle.text = prefix + text.Substring(0, Mathf.Clamp(Mathf.FloorToInt(text.Length * Mathf.Clamp01(t / (dur * 0.75f))), 0, text.Length));
                Vector3 head = h.HeadWorld();
                cam.transform.position = head + dir * Mathf.Lerp(dist0, dist1, k) + right * side * (1f - 0.5f * k) + Vector3.up * 0.02f;
                cam.transform.rotation = Quaternion.LookRotation(head - cam.transform.position, Vector3.up);
                yield return null;
            }
            subtitle.text = "";
            if (bubble != null) bubble.SetActive(false);
        }

        static string Nice(string n) { return string.IsNullOrEmpty(n) ? n : n.Substring(0, 1).ToUpper() + n.Substring(1).ToLower(); }

        IEnumerator Speak(AudioClip clip, string name, string text, Text subtitle, Text nameLabel, Camera cam, Vector3 from, Vector3 to, Vector3 look)
        {
            float len = clip != null ? clip.length : 2.2f;
            if (clip != null) AudioSourceFor(cam).PlayOneShot(clip, 1f);
            string prefix = "<b><color=#ffc83d>" + Nice(name) + " \u2014</color></b>  ";
            if (bubble != null) bubble.SetActive(true);
            float dur = Mathf.Max(2.2f, len + 0.5f), t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Ease(t / dur);
                subtitle.text = prefix + text.Substring(0, Mathf.Clamp(Mathf.FloorToInt(text.Length * Mathf.Clamp01(t / (dur * 0.75f))), 0, text.Length));
                cam.transform.position = Vector3.Lerp(from, to, k);
                cam.transform.rotation = Quaternion.LookRotation(look - cam.transform.position, Vector3.up);
                yield return null;
            }
            subtitle.text = "";
            if (bubble != null) bubble.SetActive(false);
        }

        static AudioSource AudioSourceFor(Camera cam)
        {
            var s = cam.GetComponent<AudioSource>();
            if (s == null) { s = cam.gameObject.AddComponent<AudioSource>(); s.spatialBlend = 0f; s.playOnAwake = false; }
            return s;
        }

        static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
        }

        static float Ease(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }

        static Bounds BoundsOf(Hostage h)
        {
            var rs = h.GetComponentsInChildren<Renderer>().Where(r => !(r is SpriteRenderer)).ToArray();
            if (rs.Length == 0) return new Bounds(h.transform.position + Vector3.up, new Vector3(0.6f, 1.2f, 0.6f));
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // ------------------------------------------------------------------------------------------------ UI
        GameObject BuildUi(out Text subtitle, out Text nameLabel, out RectTransform top, out RectTransform bottom)
        {
            var root = new GameObject("CutsceneCanvas", typeof(Canvas), typeof(CanvasScaler));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 500;
            var sc = root.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;           // the whole 1920x1080 layout always fits, on any screen shape
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            top = Bar(root.transform, true);
            bottom = Bar(root.transform, false);

            // cream speech bubble with a dark outline, above the bottom bar
            Sprite rounded = null;
#if UNITY_EDITOR
            rounded = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Environment/Blood/Icon_RoundedRect.png");
#endif
            if (rounded == null) rounded = roundedSprite;
            bubble = new GameObject("Bubble", typeof(RectTransform), typeof(Image));
            bubble.transform.SetParent(root.transform, false);
            var bo = bubble.GetComponent<Image>(); bo.sprite = rounded; bo.type = rounded != null ? Image.Type.Sliced : Image.Type.Simple;
            bo.color = new Color(1f, 0.78f, 0.22f, 1f); bo.raycastTarget = false;
            var brt = bubble.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = new Vector2(0f, 150f); brt.sizeDelta = new Vector2(1320f, 150f);
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(bubble.transform, false);
            var fi = fill.GetComponent<Image>(); fi.sprite = rounded; fi.type = rounded != null ? Image.Type.Sliced : Image.Type.Simple;
            fi.color = new Color(0.07f, 0.09f, 0.075f, 0.95f); fi.raycastTarget = false;
            var frt = fill.GetComponent<RectTransform>(); frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = new Vector2(4, 4); frt.offsetMax = new Vector2(-4, -4);

            var tgo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            tgo.transform.SetParent(bubble.transform, false);
            subtitle = tgo.GetComponent<Text>();
            subtitle.font = font; subtitle.fontSize = 34; subtitle.color = new Color(0.93f, 0.94f, 0.87f); subtitle.alignment = TextAnchor.MiddleLeft;
            subtitle.supportRichText = true; subtitle.raycastTarget = false;
            subtitle.horizontalOverflow = HorizontalWrapMode.Wrap; subtitle.verticalOverflow = VerticalWrapMode.Overflow;
            var trt = tgo.GetComponent<RectTransform>(); trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(36, 12); trt.offsetMax = new Vector2(-36, -12);
            nameLabel = subtitle;           // (the speaker's name is part of the bubble text)
            bubble.SetActive(false);
            return root;
        }

        static RectTransform Bar(Transform parent, bool isTop)
        {
            var go = new GameObject(isTop ? "TopBar" : "BottomBar", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>(); img.color = Color.black; img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, isTop ? 1 : 0); rt.anchorMax = new Vector2(1, isTop ? 1 : 0);
            rt.pivot = new Vector2(0.5f, isTop ? 1 : 0);
            rt.sizeDelta = new Vector2(0, 0);
            return rt;
        }

        static void SetBars(RectTransform top, RectTransform bottom, float k)
        {
            float h = Mathf.Lerp(0f, 130f, Ease(k));
            top.sizeDelta = new Vector2(0, h);
            bottom.sizeDelta = new Vector2(0, h);
        }

        static Text Label(Transform parent, Font font, int size, Color color, float y)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font; t.fontSize = size; t.color = color; t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            var o = go.GetComponent<Outline>(); o.effectColor = new Color(0, 0, 0, 0.95f); o.effectDistance = new Vector2(2f, -2f);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0, y - 100f); rt.sizeDelta = new Vector2(1500, 60);
            return t;
        }
    }
}
