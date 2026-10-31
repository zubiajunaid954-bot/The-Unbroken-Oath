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
    /// The game-over cut scene. When Samrah dies the world drops into slow motion and fades to black. Then:
    ///   1. close-up, low to the ground: her hand is raised, trembling, and it falls to the ground;
    ///   2. a drone shot high above her, slowly descending and turning, as she lies there: "I failed... I couldn't save you guys...",
    ///      the operator calls out to her in vain. Heartbeat slows and stops, soft piano and strings.
    /// Then the MISSION FAILED screen appears over the last frame. SPACE (or ESC) skips.
    /// </summary>
    public partial class CutsceneDirector
    {
        [Header("Death scene")]
        public GameObject samrahPrefab;                  // Samrah_Rigged.prefab (made by GAMECON > Rig Teammates)
        public GameObject gunPrefab;                     // the rifle that falls from her hand (barrel = +Z, up = +Y, centred; Gun_DeathProp.prefab)

        public bool HasDeathScene { get { return samrahPrefab != null; } }
        public bool DeathPlaying { get; private set; }

        public void PlayDeath(Vector3 pos, float yaw, Action onDone)
        {
            if (DeathPlaying || samrahPrefab == null) { if (onDone != null) onDone(); return; }
            StartCoroutine(RunDeath(pos, yaw, onDone));
        }

        class DLine { public AudioClip clip; public string who, text; }

        IEnumerator RunDeath(Vector3 pos, float yaw, Action onDone)
        {
            DeathPlaying = true; Playing = true;
            Camera mainCam = Camera.main;
            if (mainCam == null) { var tagged = GameObject.FindGameObjectWithTag("MainCamera"); if (tagged != null) mainCam = tagged.GetComponent<Camera>(); }

            // the slow-motion world: rain falls slowly, everybody is nearly frozen
            float oldScale = Time.timeScale;
            Time.timeScale = 0.08f;

            // Only the cut-scene Samrah may be seen: the live player, her weapon and the stock ragdoll (which spawns a moment after
            // the player dies, and parents the camera to its head) are hidden - now and for the first seconds while they appear
            Action cleanse = () =>
            {
                foreach (var rc in FindObjectsOfType<ragdollCamera>())
                {
                    if (rc == null || rc.isAi) continue;
                    if (mainCam != null && mainCam.transform.IsChildOf(rc.transform)) mainCam.transform.SetParent(null, true);
                    rc.gameObject.SetActive(false);
                }
                var pl = MissionManager.Instance != null ? MissionManager.Instance.Player : null;
                if (pl != null) foreach (var r in pl.GetComponentsInChildren<Renderer>(true)) if (r != null) r.enabled = false;
            };
            cleanse();
            float nextClean = 0f;

            // hide the HUD, quieten the world's own sounds
            var hudCanvases = new List<Canvas>();
            foreach (var c in FindObjectsOfType<Canvas>()) if (c.enabled) { c.enabled = false; hudCanvases.Add(c); }
            var quieted = new Dictionary<AudioSource, float>();
            foreach (var a in FindObjectsOfType<AudioSource>()) { quieted[a] = a.volume; a.volume *= 0.3f; }

            // ---- camera + UI ----
            var camGo = new GameObject("DeathCamera");
            var cam = camGo.AddComponent<Camera>();
            if (mainCam != null) { cam.CopyFrom(mainCam); mainCam.enabled = false; }
            cam.depth = 100f; cam.nearClipPlane = 0.03f; cam.farClipPlane = 600f; cam.fieldOfView = 36f;
            camGo.tag = "Untagged";
            foreach (var l in FindObjectsOfType<AudioListener>()) l.enabled = false;          // (the old camera may be parented to something that disappears)
            camGo.AddComponent<AudioListener>();

            Text subtitle, nameLabel; RectTransform topBar, bottomBar;
            var ui = BuildUi(out subtitle, out nameLabel, out topBar, out bottomBar);
            var fadeGo = new GameObject("Fade", typeof(RectTransform), typeof(Image));
            fadeGo.transform.SetParent(ui.transform, false);
            var fade = fadeGo.GetComponent<Image>(); fade.color = Color.black; fade.raycastTarget = false;
            var frt = fade.rectTransform; frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = frt.offsetMax = Vector2.zero;
            // the operator's portrait (the same picture as in the game's dialogue box) appears beside the bubble when she speaks
            Image portrait = null;
            if (VoiceAssistant.Dialogue != null && VoiceAssistant.Dialogue.portrait != null)
            {
                var src = VoiceAssistant.Dialogue.portrait.GetComponent<Image>();
                if (src != null && src.sprite != null)
                {
                    var pg = new GameObject("OperatorPortrait", typeof(RectTransform), typeof(Image));
                    pg.transform.SetParent(ui.transform, false);
                    portrait = pg.GetComponent<Image>(); portrait.sprite = src.sprite; portrait.preserveAspect = true; portrait.raycastTarget = false;
                    var prt = portrait.rectTransform;
                    prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0f); prt.pivot = new Vector2(0f, 0f);
                    prt.anchoredPosition = new Vector2(-915f, 138f); prt.sizeDelta = new Vector2(290f, 317f);
                    pg.SetActive(false);
                }
            }
            Action<float> setFade = a => { var c = fade.color; c.a = Mathf.Clamp01(a); fade.color = c; };
            setFade(1f);

            // ---- Samrah, lying where she fell (her feet point the way she was facing) ----
            Vector3 g = pos;
            var hits = Physics.RaycastAll(pos + Vector3.up * 0.6f, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance);
            foreach (var h in hits) if (h.normal.y > 0.5f) { g = h.point; break; }
            var body = Instantiate(samrahPrefab, g, Quaternion.Euler(0f, yaw, 0f));
            body.name = "Samrah_Dead";
            var crig = body.GetComponent<CaptiveRig>();
            var death = body.AddComponent<DeathRig>();
            death.Setup(crig);
            Transform rt = body.transform;

            // lying pose first, then lower / raise her so that she really rests ON the ground (not floating above it)
            death.fallT = 99f; death.tremble = 0f; death.Apply();
            var smr = body.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null)
            {
                var baked = new Mesh();
                smr.BakeMesh(baked);
                var verts = baked.vertices; var m = smr.transform.localToWorldMatrix;
                if (verts.Length > 0)
                {
                    var ys = new float[verts.Length];
                    for (int i = 0; i < verts.Length; i++) ys[i] = m.MultiplyPoint3x4(verts[i]).y;
                    Array.Sort(ys);
                    float low = ys[Mathf.Min(ys.Length - 1, (int)(ys.Length * 0.05f))];      // the part of her resting on the ground
                    rt.position += Vector3.up * (g.y + 0.01f - low);
                }
                Destroy(baked);
            }
            Vector3 wristGround = death.HandWorld();
            death.fallT = -1f; death.tremble = 1f; death.Apply();
            GAMECON.FX.BloodFX.Pool(g);

            // a cold light on her (the "spotlight" of the drone shot)
            var lightGo = new GameObject("DeathLight");
            lightGo.transform.position = rt.TransformPoint(0f, 9f, -0.1f);
            lightGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var spot = lightGo.AddComponent<Light>();
            spot.type = LightType.Spot; spot.spotAngle = 34f; spot.range = 26f; spot.intensity = 0f;
            spot.color = new Color(0.78f, 0.88f, 1f); spot.shadows = LightShadows.Soft;

            // sounds
            var audioGo = new GameObject("DeathAudio");
            var heart = audioGo.AddComponent<AudioSource>(); heart.spatialBlend = 0f; heart.playOnAwake = false; heart.volume = 0.45f;
            var music = audioGo.AddComponent<AudioSource>(); music.spatialBlend = 0f; music.playOnAwake = false; music.volume = 0.40f;
            var voice = audioGo.AddComponent<AudioSource>(); voice.spatialBlend = 0f; voice.playOnAwake = false; voice.volume = 1f;
            heart.clip = Resources.Load<AudioClip>("Audio/Cutscene/Death_Heart");
            music.clip = Resources.Load<AudioClip>("Audio/Cutscene/Death_Music");
            var dropClip = Resources.Load<AudioClip>("Audio/Cutscene/Gun_Drop");
            AudioListener.pause = false;

            var shot2Lines = new List<DLine>
            {
                new DLine { clip = Resources.Load<AudioClip>("Audio/Cutscene/Samrah_Death_1"), who = "Samrah", text = "I failed... I couldn't save you guys." },
                new DLine { clip = Resources.Load<AudioClip>("Audio/Cutscene/Samrah_Death_2"), who = "Samrah", text = "I'm so sorry... forgive me." },
                new DLine { clip = Resources.Load<AudioClip>("Audio/Cutscene/Operator_Fail_0"), who = "Operator", text = "Samrah... Samrah, can you hear me? Please... answer me." },
                new DLine { clip = Resources.Load<AudioClip>("Audio/Cutscene/Operator_Fail_1"), who = "Operator", text = "Mission failed." },
            };
            var shot1Line = new DLine { clip = Resources.Load<AudioClip>("Audio/Cutscene/Samrah_Death_0"), who = "Samrah", text = "Duaa... Zubia... Maha..." };

            // subtitle state
            DLine cur = null; float curStart = 0f, curDur = 0f;
            Action<DLine, float> startLine = (l, now) =>
            {
                cur = l; curStart = now;
                curDur = (l.clip != null ? l.clip.length : 2.5f) + 0.6f;
                if (l.clip != null) voice.PlayOneShot(l.clip, 1f);
                if (bubble != null) bubble.SetActive(true);
            };
            Action<float> tickLine = now =>
            {
                if (cur == null) return;
                float a = now - curStart;
                if (a > curDur) { cur = null; subtitle.text = ""; if (bubble != null) bubble.SetActive(false); if (portrait != null) portrait.gameObject.SetActive(false); return; }
                if (portrait != null)
                {
                    portrait.gameObject.SetActive(cur.who == "Operator");
                    float slide = 1f - Ease(a / 0.4f);
                    portrait.rectTransform.anchoredPosition = new Vector2(-915f, 138f - 60f * slide + 3f * Mathf.Sin(now * 5f));
                    var pc = portrait.color; pc.a = Ease(a / 0.3f); portrait.color = pc;
                }
                string prefix = "<b><color=#ffc83d>" + cur.who + " —</color></b>  ";
                int n = Mathf.Clamp(Mathf.FloorToInt(cur.text.Length * Mathf.Clamp01(a / (curDur * 0.72f))), 0, cur.text.Length);
                subtitle.text = prefix + cur.text.Substring(0, n);
            };

            foreach (var l in shot2Lines.Concat(new[] { shot1Line }))
                if (l.clip == null) Debug.LogWarning("[Death scene] voice clip missing for: " + l.text + "  (Assets/Resources/Audio/Cutscene)");

            // the rifle in her hand
            Transform gunT = null;
            if (gunPrefab != null) { gunT = Instantiate(gunPrefab).transform; gunT.name = "Samrah_Rifle"; }
            const float releaseAt = 2.6f, gravity = 5.5f;
            float landY = g.y + 0.045f;
            Vector3 landPos = wristGround + rt.TransformDirection(-0.25f, 0f, 0.42f); landPos.y = landY;
            Quaternion heldRot = Quaternion.LookRotation(rt.forward, rt.up) * Quaternion.Euler(0f, 0f, 10f);
            Quaternion lieRot = Quaternion.LookRotation(Quaternion.Euler(0f, 38f, 0f) * rt.forward, rt.up) * Quaternion.Euler(0f, 0f, 78f);   // lies on its side
            bool released = false, dropSounded = false;
            Vector3 p0 = Vector3.zero, vXZ = Vector3.zero; float flightDur = 0.6f, releaseT = 0f;

            float realStart = Time.unscaledTime;
            bool skip = false;
            Func<bool> skipPressed = () => Time.unscaledTime - realStart > 1.5f && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape));

            // ---- brief black beat ----
            float t = 0f;
            while (t < 0.5f) { t += Time.unscaledDeltaTime; yield return null; }
            if (heart.clip != null) heart.Play();
            if (music.clip != null) music.Play();

            // =====================================================  SHOT 1: the rifle slips from her hand and falls
            t = 0f;
            const float shot1 = 7.6f;
            float armFallAt = releaseAt + 0.5f;
            Vector3 lookAt = wristGround + Vector3.up * 0.7f;
            bool lineStarted = false;
            while (t < shot1 && !skip)
            {
                float dt = Time.unscaledDeltaTime; t += dt;
                if (skipPressed()) { skip = true; break; }
                if (Time.unscaledTime < realStart + 5f && Time.unscaledTime >= nextClean) { nextClean = Time.unscaledTime + 0.1f; cleanse(); }
                SetBars(topBar, bottomBar, Mathf.Clamp01(t / 0.7f));
                float a = t < 0.9f ? 1f - Ease(t / 0.9f) : 0f;
                if (t > shot1 - 0.5f) a = Ease((t - (shot1 - 0.5f)) / 0.5f);
                setFade(a);

                death.tremble = t < releaseAt ? Mathf.Clamp01(0.45f + t * 0.2f) : 0f;
                death.fallT = t < armFallAt ? -1f : t - armFallAt;
                death.Apply();

                // the rifle: held in the trembling hand, then it slips out and falls (smoothly, slowly), bounces once and settles
                Vector3 gunPos = Vector3.zero; Quaternion gunRot = heldRot;
                if (gunT != null)
                {
                    if (!released)
                    {
                        Vector3 wrist = death.HandWorld();
                        gunPos = wrist + rt.up * 0.05f + rt.forward * 0.10f + rt.right * 0.015f * Mathf.Sin(t * 22f) * death.tremble;
                        gunRot = heldRot * Quaternion.Euler(2.5f * Mathf.Sin(t * 19f) * death.tremble, 0f, 3f * Mathf.Sin(t * 14f) * death.tremble);
                        if (t >= releaseAt)
                        {
                            released = true; releaseT = t; p0 = gunPos;
                            flightDur = Mathf.Sqrt(2f * Mathf.Max(0.2f, p0.y - landY) / gravity);
                            vXZ = new Vector3(landPos.x - p0.x, 0f, landPos.z - p0.z) / flightDur;
                        }
                    }
                    if (released)
                    {
                        float tau = t - releaseT;
                        if (tau < flightDur)
                        {
                            gunPos = new Vector3(p0.x + vXZ.x * tau, p0.y - 0.5f * gravity * tau * tau, p0.z + vXZ.z * tau);
                            gunRot = Quaternion.Slerp(heldRot, lieRot, Ease(tau / flightDur));
                        }
                        else
                        {
                            float s = tau - flightDur;
                            float vb = 0.25f * gravity * flightDur;                          // bounce speed
                            float hop = Mathf.Max(0f, vb * s - 0.5f * gravity * s * s);
                            float slide = (1f - Mathf.Exp(-5f * s)) * 0.05f;
                            gunPos = new Vector3(landPos.x + vXZ.x * slide, landY + hop, landPos.z + vXZ.z * slide);
                            gunRot = lieRot * Quaternion.Euler(7f * Mathf.Exp(-3f * s) * Mathf.Sin(15f * s), 0f, 0f);
                            if (!dropSounded) { dropSounded = true; if (dropClip != null) voice.PlayOneShot(dropClip, 0.9f); }
                        }
                    }
                    gunT.position = gunPos; gunT.rotation = gunRot;
                }

                if (!lineStarted && t > 0.9f) { lineStarted = true; startLine(shot1Line, t); }
                tickLine(t);

                // low camera, looking along the ground at the rifle; a slow push-in after it has landed
                float push = Ease((t - releaseT - 2.4f) / 3f) * 0.28f;
                Vector3 offset = rt.TransformDirection(new Vector3(0.95f, 0.36f, 0.95f)) * (1f - push);
                Vector3 sway = new Vector3(Mathf.Sin(t * 0.9f) * 0.015f, Mathf.Sin(t * 1.3f) * 0.01f, 0f);
                cam.transform.position = wristGround + offset + sway;
                Vector3 target = gunT != null ? gunT.position : death.HandWorld();
                lookAt = Vector3.Lerp(lookAt, target, 1f - Mathf.Exp(-6f * dt));
                cam.transform.rotation = Quaternion.LookRotation(lookAt - cam.transform.position, Vector3.up);
                cam.fieldOfView = Mathf.Lerp(40f, 32f, Ease(t / shot1));
                yield return null;
            }
            if (cur != null) { cur = null; subtitle.text = ""; if (bubble != null) bubble.SetActive(false); if (portrait != null) portrait.gameObject.SetActive(false); voice.Stop(); }
            death.fallT = 99f; death.tremble = 0f; death.Apply();
            if (gunT != null) { gunT.position = new Vector3(landPos.x, landY, landPos.z); gunT.rotation = lieRot; }

            // =====================================================  SHOT 2: the drone shot
            Vector3 centre = rt.TransformPoint(0f, 0.1f, -0.1f);
            float az0 = yaw + 25f;
            t = 0f;
            int next = 0; float nextAt = 1.6f; float endAt = float.MaxValue;
            while (t < endAt && !skip)
            {
                float dt = Time.unscaledDeltaTime; t += dt;
                if (skipPressed()) { skip = true; break; }
                if (Time.unscaledTime < realStart + 5f && Time.unscaledTime >= nextClean) { nextClean = Time.unscaledTime + 0.1f; cleanse(); }
                SetBars(topBar, bottomBar, 1f);

                float p = Mathf.Clamp01(t / 26f);
                float e = Ease(p);
                float h = Mathf.Lerp(12.5f, 5.6f, e);
                float r = Mathf.Lerp(1.6f, 3.3f, e);
                float az = (az0 + 48f * e) * Mathf.Deg2Rad;
                cam.transform.position = centre + new Vector3(Mathf.Sin(az) * r, h, Mathf.Cos(az) * r);
                Vector3 fwd = (centre - cam.transform.position).normalized;
                Vector3 upv = Quaternion.AngleAxis(14f * e, fwd) * Vector3.up;
                cam.transform.rotation = Quaternion.LookRotation(fwd, upv);
                cam.fieldOfView = Mathf.Lerp(32f, 44f, e);

                // fade in from black, the cold light finds her
                float a = Ease(t / 1.4f);
                float outA = endAt < float.MaxValue ? Ease((t - (endAt - 1.8f)) / 1.8f) : 0f;
                setFade(Mathf.Max(1f - a, outA * 0.65f));
                spot.intensity = Mathf.Lerp(0f, 7.5f, Ease(t / 2.5f)) * (1f - 0.4f * outA);

                death.breath = Mathf.Clamp01(1f - t / 6f);                         // her breathing fades away
                death.Apply();

                // the lines, one after the other
                if (cur == null && next < shot2Lines.Count && t >= nextAt) { startLine(shot2Lines[next], t); next++; }
                if (cur != null && t - curStart > curDur) nextAt = t + (next == 3 ? 1.4f : 0.9f);
                tickLine(t);
                if (cur == null && next >= shot2Lines.Count && endAt == float.MaxValue) endAt = t + 3.2f;
                yield return null;
            }

            // ---- the end: the camera stays on her under the MISSION FAILED panel ----
            cur = null; subtitle.text = ""; if (bubble != null) bubble.SetActive(false); if (portrait != null) portrait.gameObject.SetActive(false);
            SetBars(topBar, bottomBar, 0f);
            if (skip) { setFade(0.65f); spot.intensity = 5f; }
            ui.GetComponent<Canvas>().sortingOrder = -10;                         // below the HUD canvas, so the fail panel is on top
            foreach (var kv in quieted) if (kv.Key != null) kv.Key.volume = kv.Value;
            Time.timeScale = oldScale <= 0f ? 1f : oldScale;
            Playing = false; DeathPlaying = false;
            if (onDone != null) onDone();
        }
    }
}
