using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using scgFullBodyController;

namespace GAMECON.Mission
{
    /// <summary>
    /// The victory cut scene, played after the third teammate is freed. Samrah, Duaa, Zubia and Maha walk in side by side somewhere in the
    /// open, stop and strike hero-squad poses (hands on hips, fist raised, kneeling), one close-up for each with a name plate, and then
    /// the whole squad together: "We are back again... TOGETHER, stronger and better!" with a victory theme. SPACE skips.
    /// </summary>
    public partial class CutsceneDirector
    {
        [Header("Victory scene")]
        public GameObject[] heroPrefabs;                 // Samrah, Duaa, Zubia, Maha (the rigged prefabs)

        public bool HasVictoryScene
        {
            get
            {
                if (heroPrefabs == null || heroPrefabs.Length < 4) return false;
                foreach (var p in heroPrefabs) if (p == null) return false;
                return true;
            }
        }
        public bool VictoryPlaying { get; private set; }

        public void PlayVictory(Vector3 origin, Action onDone)
        {
            if (VictoryPlaying || !HasVictoryScene) { if (onDone != null) onDone(); return; }
            StartCoroutine(RunVictory(origin, onDone));
        }

        class Hero
        {
            public string name; public Transform t; public HeroRig rig; public Vector3 slot, start; public float dist, startAt, travelled, arrivedAt = -1f, groundY;
            public float kneelOffset;
        }

        // the timeline (seconds from the start of the music)
        const float TWalkEnd = 6.3f, TShot = 2.4f, TGroup = 15.9f, TVo = 16.5f, THit = 18.1f, TFadeOut = 24.2f, TEnd = 25.8f;
        const float WalkSpeed = 1.5f, CycleLength = 1.39f;

        // ---------------------------------------------------------------------------------------------------------------- the stage
        static float Clear(Vector3 p, Vector3 dir, float max)
        {
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up * 1.0f, dir, out hit, max, ~0, QueryTriggerInteraction.Ignore)) return hit.distance;
            return max;
        }

        static float GroundY(Vector3 p, float fallback)
        {
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up * 2.5f, Vector3.down, out hit, 6f, ~0, QueryTriggerInteraction.Ignore) && hit.normal.y > 0.5f) return hit.point.y;
            return fallback;
        }

        // an open, flat spot near the player (the squad needs about 8 m of free space in front of the camera)
        static void FindStage(Vector3 origin, out Vector3 center, out Vector3 towardCam)
        {
            float bestScore = float.MinValue; center = origin; towardCam = Vector3.forward;
            var radii = new[] { 0f, 7f, 12f, 18f, 26f, 34f };
            foreach (float r in radii)
            {
                int steps = r == 0f ? 1 : 16;
                for (int a = 0; a < steps; a++)
                {
                    Vector3 p = origin + Quaternion.Euler(0f, a * 360f / steps, 0f) * Vector3.forward * r;
                    NavMeshHit nh;
                    if (!NavMesh.SamplePosition(p, out nh, 4f, NavMesh.AllAreas)) continue;
                    p = nh.position;

                    // free space around the group; a clear line for the camera (in front of the squad) AND for the walk-in (behind it)
                    float minClear = 99f, bestSum = -1f; float bestDirAngle = 0f;
                    for (int k = 0; k < 16; k++)
                    {
                        Vector3 d = Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward;
                        float c = Clear(p, d, 14f), cb = Clear(p, -d, 12f);
                        if (c < minClear) minClear = c;
                        if (c >= 11f && cb >= 8.8f && c + cb > bestSum) { bestSum = c + cb; bestDirAngle = k * 22.5f; }
                    }
                    if (minClear < 3.0f || bestSum < 0f) continue;
                    float bestDir = bestSum;

                    // flat ground
                    float y0 = GroundY(p, p.y), spread = 0f;
                    for (int k = 0; k < 4; k++) spread = Mathf.Max(spread, Mathf.Abs(GroundY(p + Quaternion.Euler(0f, k * 90f, 0f) * Vector3.forward * 2.5f, y0) - y0));
                    if (spread > 0.35f) continue;

                    float score = bestDir + minClear * 2f - r * 0.35f - spread * 10f;
                    if (score > bestScore)
                    {
                        bestScore = score; center = new Vector3(p.x, y0, p.z);
                        towardCam = Quaternion.Euler(0f, bestDirAngle, 0f) * Vector3.forward;
                    }
                }
            }
            if (bestScore == float.MinValue)
            {
                // nothing ideal: use the player's spot, with the camera on the side with the most room (and the walk-in from the other side)
                float best = -1f;
                for (int k = 0; k < 16; k++)
                {
                    Vector3 d = Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward;
                    float c = Clear(origin, d, 14f) + Clear(origin, -d, 12f);
                    if (c > best) { best = c; towardCam = d; }
                }
                center = new Vector3(origin.x, GroundY(origin, origin.y), origin.z);
            }
        }

        static float Ease3(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }

        // ---------------------------------------------------------------------------------------------------------------- the scene
        IEnumerator RunVictory(Vector3 origin, Action onDone)
        {
            VictoryPlaying = true; Playing = true;
            var mgr = MissionManager.Instance;
            Camera mainCam = Camera.main;
            float oldScale = Time.timeScale;
            Time.timeScale = 0.5f;                                            // the rain falls a little slower: it feels cinematic

            // only the four heroes may be seen: hide the live player and the teammates standing in their rooms, and the HUD
            var hidden = new List<Renderer>();
            Action hideWorldHeroes = () =>
            {
                if (mgr == null) return;
                if (mgr.Player != null) foreach (var r in mgr.Player.GetComponentsInChildren<Renderer>(true)) if (r != null && r.enabled) { r.enabled = false; hidden.Add(r); }
                foreach (var h in mgr.hostages) if (h != null) foreach (var r in h.GetComponentsInChildren<Renderer>(true)) if (r != null && r.enabled) { r.enabled = false; hidden.Add(r); }
            };
            hideWorldHeroes();
            foreach (var rc in FindObjectsOfType<ragdollCamera>()) if (rc != null && !rc.isAi) rc.gameObject.SetActive(false);
            var hudCanvases = new List<Canvas>();
            foreach (var c in FindObjectsOfType<Canvas>()) if (c.enabled) { c.enabled = false; hudCanvases.Add(c); }
            var quieted = new Dictionary<AudioSource, float>();
            foreach (var a in FindObjectsOfType<AudioSource>()) { quieted[a] = a.volume; a.volume *= 0.25f; }
            if (GAMECON.Voice.VoiceAssistant.Dialogue != null) GAMECON.Voice.VoiceAssistant.Dialogue.Hide();

            // ---- camera + UI ----
            var camGo = new GameObject("VictoryCamera");
            var cam = camGo.AddComponent<Camera>();
            if (mainCam != null) { cam.CopyFrom(mainCam); mainCam.enabled = false; }
            cam.depth = 100f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 600f; cam.fieldOfView = 38f;
            camGo.tag = "Untagged";
            foreach (var l in FindObjectsOfType<AudioListener>()) l.enabled = false;
            camGo.AddComponent<AudioListener>();

            Text subtitle, nameLabel; RectTransform topBar, bottomBar;
            var ui = BuildUi(out subtitle, out nameLabel, out topBar, out bottomBar);
            var fadeGo = new GameObject("Fade", typeof(RectTransform), typeof(Image));
            fadeGo.transform.SetParent(ui.transform, false);
            var fade = fadeGo.GetComponent<Image>(); fade.color = Color.black; fade.raycastTarget = false;
            var frt = fade.rectTransform; frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = frt.offsetMax = Vector2.zero;
            Action<float> setFade = a => { var c = fade.color; c.a = Mathf.Clamp01(a); fade.color = c; };
            setFade(1f);
            Font font = FailScreen.GetFont();

            // name plate (lower third)
            var plate = new GameObject("NamePlate", typeof(RectTransform), typeof(CanvasGroup)); plate.transform.SetParent(ui.transform, false);
            var plateRt = (RectTransform)plate.transform; plateRt.anchorMin = plateRt.anchorMax = new Vector2(0f, 0f); plateRt.pivot = new Vector2(0f, 0f);
            plateRt.anchoredPosition = new Vector2(150f, 190f); plateRt.sizeDelta = new Vector2(700f, 130f);
            var plateGroup = plate.GetComponent<CanvasGroup>(); plateGroup.alpha = 0f;
            var accent = NewImg(plate.transform, "Accent", new Color(1f, 0.78f, 0.22f, 1f)); SetRect(accent.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(8f, 118f));
            var nameText = NewText(plate.transform, font, "", 92, Color.white, FontStyle.Bold, TextAnchor.LowerLeft); SetRect(nameText.rectTransform, new Vector2(0f, 0f), new Vector2(30f, 28f), new Vector2(660f, 100f));
            var tagText = NewText(plate.transform, font, "SUN TEMPLE SQUAD", 28, new Color(1f, 0.78f, 0.22f), FontStyle.Bold, TextAnchor.LowerLeft); SetRect(tagText.rectTransform, new Vector2(0f, 0f), new Vector2(34f, 0f), new Vector2(500f, 34f));

            // the line the squad says (big, in the middle of the lower part of the screen)
            var line1 = NewText(ui.transform, font, "", 76, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            SetRect(line1.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 330f), new Vector2(1600f, 100f), true);
            var line2 = NewText(ui.transform, font, "", 112, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            SetRect(line2.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 205f), new Vector2(1800f, 140f), true);
            line2.supportRichText = true; line1.supportRichText = true;
            line1.gameObject.GetComponent<Outline>().effectDistance = new Vector2(3f, -3f);
            line2.gameObject.GetComponent<Outline>().effectDistance = new Vector2(4f, -4f);

            // ---- the stage and the four heroes ----
            Vector3 center, fwd;
            FindStage(origin, out center, out fwd);
            fwd.y = 0f; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            // Samrah kneels in front, the others stand behind her: Duaa (left), Zubia (middle, fist raised), Maha (right)
            string[] names = { "SAMRAH", "DUAA", "ZUBIA", "MAHA" };
            HeroRig.Pose[] poses = { HeroRig.Pose.Kneel, HeroRig.Pose.HipsWide, HeroRig.Pose.FistUp, HeroRig.Pose.Hips };
            Vector2[] slots = { new Vector2(0f, 0.75f), new Vector2(-1.45f, -0.35f), new Vector2(0f, -0.85f), new Vector2(1.45f, -0.35f) };
            float[] dists = { 6.8f, 7.0f, 7.5f, 7.2f };
            float[] delays = { 0.3f, 0.45f, 0.6f, 0.35f };
            var heroes = new List<Hero>();
            for (int i = 0; i < 4; i++)
            {
                var go = Instantiate(heroPrefabs[i]);
                go.name = names[i] + "_Hero";
                var cr = go.GetComponent<CaptiveRig>();
                var hr = go.AddComponent<HeroRig>();
                hr.Setup(cr); hr.pose = poses[i]; hr.seed = i * 1.7f;
                var h = new Hero { name = names[i], t = go.transform, rig = hr, dist = dists[i], startAt = delays[i] };
                h.slot = center + right * slots[i].x + fwd * slots[i].y;
                h.start = h.slot - fwd * h.dist;
                h.groundY = GroundY(h.start, center.y);
                go.transform.position = new Vector3(h.start.x, h.groundY, h.start.z);
                go.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                hr.phase = i * 0.9f; hr.walkWeight = 0f; hr.poseWeight = 0f; hr.Apply();
                heroes.Add(h);
            }

            // lights: a warm key from the camera side, cool and amber rims from behind
            var lights = new GameObject("VictoryLights");
            Light Spot(string n, Vector3 pos, Vector3 target, Color c, float range, float angle)
            {
                var g = new GameObject(n); g.transform.SetParent(lights.transform, false);
                g.transform.position = pos; g.transform.rotation = Quaternion.LookRotation(target - pos);
                var l = g.AddComponent<Light>(); l.type = LightType.Spot; l.color = c; l.range = range; l.spotAngle = angle; l.intensity = 0f; l.shadows = LightShadows.Soft;
                return l;
            }
            Vector3 gc = center + Vector3.up * 1.0f;
            Light key = Spot("Key", center + fwd * 7f + right * -3f + Vector3.up * 5.5f, gc, new Color(1f, 0.86f, 0.68f), 30f, 48f);
            Light rimA = Spot("RimBlue", center - fwd * 5f + right * -4.5f + Vector3.up * 4.5f, gc, new Color(0.55f, 0.75f, 1f), 24f, 58f);
            Light rimB = Spot("RimAmber", center - fwd * 5f + right * 4.5f + Vector3.up * 4.5f, gc, new Color(1f, 0.62f, 0.22f), 24f, 58f);

            // sound
            var audioGo = new GameObject("VictoryAudio");
            var music = audioGo.AddComponent<AudioSource>(); music.spatialBlend = 0f; music.playOnAwake = false; music.volume = 0.85f;
            var vo = audioGo.AddComponent<AudioSource>(); vo.spatialBlend = 0f; vo.playOnAwake = false; vo.volume = 1f;
            music.clip = Resources.Load<AudioClip>("Audio/Cutscene/Victory_Music");
            var voClip = Resources.Load<AudioClip>("Audio/Cutscene/Victory_VO");
            if (music.clip == null) Debug.LogWarning("[Victory scene] Resources/Audio/Cutscene/Victory_Music not found");
            if (voClip == null) Debug.LogWarning("[Victory scene] Resources/Audio/Cutscene/Victory_VO not found");

            float realStart = Time.unscaledTime;
            bool skip = false, musicStarted = false, voStarted = false, hit = false;
            float T = -0.5f;                                                  // 0.5 s of black first; the music starts at T = 0
            string plateFor = "";
            float plateT = 0f;
            float hitT = -10f;
            Vector3 lookSmooth = center + Vector3.up * 1.2f;
            int shotIndex = -1;
            string[] shotOrder = { "DUAA", "MAHA", "ZUBIA", "SAMRAH" };

            while (T < TEnd && !skip)
            {
                float dt = Time.unscaledDeltaTime; T += dt;
                if (Time.unscaledTime - realStart > 2f && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape))) { skip = true; break; }
                if (Time.unscaledTime - realStart < 4f) hideWorldHeroes();

                if (!musicStarted && T >= 0f) { musicStarted = true; if (music.clip != null) music.Play(); }
                if (!voStarted && T >= TVo) { voStarted = true; if (voClip != null) vo.PlayOneShot(voClip, 1f); }
                if (!hit && T >= THit) { hit = true; hitT = T; }

                // ---------------- the heroes
                foreach (var h in heroes)
                {
                    var rg = h.rig;
                    float remaining = h.dist - h.travelled;
                    if (T >= h.startAt && remaining > 0.02f)
                    {
                        float speed = Mathf.Min(WalkSpeed, Mathf.Max(0.3f, remaining * 1.4f + 0.2f));
                        h.travelled += speed * dt;
                        rg.phase += speed / CycleLength * Mathf.PI * 2f * dt;
                        rg.walkWeight = 1f; rg.poseWeight = 0f;
                    }
                    else if (remaining <= 0.02f)
                    {
                        h.travelled = h.dist;
                        if (h.arrivedAt < 0f) h.arrivedAt = T;
                        float since = T - h.arrivedAt;
                        rg.walkWeight = 1f - Ease3(since / 0.5f);
                        rg.poseWeight = Ease3((since - 0.15f) / 1.1f);
                        if (since < 0.5f) rg.phase += 0.45f * (1f - since / 0.5f) / CycleLength * Mathf.PI * 2f * dt;     // the last half step
                    }
                    Vector3 pos = h.start + fwd * h.travelled;
                    h.groundY = Mathf.Lerp(h.groundY, GroundY(pos, h.groundY), 1f - Mathf.Exp(-12f * dt));
                    h.t.position = new Vector3(pos.x, h.groundY, pos.z);
                    h.t.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                    rg.Apply();
                }

                // ---------------- the shots
                Vector3 camPos, look; float fov;
                float fadeA = 0f;
                string wantPlate = "";
                if (T < TWalkEnd)
                {
                    // 1. wide, low: the four of them walk towards the camera
                    float u = Mathf.Clamp01(T / TWalkEnd);
                    camPos = center + fwd * Mathf.Lerp(9.6f, 7.4f, Ease3(u)) + right * Mathf.Lerp(-1.4f, 0.6f, u) + Vector3.up * Mathf.Lerp(0.55f, 0.95f, u);
                    Vector3 avg = Vector3.zero; foreach (var h in heroes) avg += h.t.position; avg /= 4f;
                    look = avg + Vector3.up * 1.15f;
                    fov = Mathf.Lerp(40f, 34f, u);
                    wantPlate = "";
                }
                else if (T < TGroup)
                {
                    // 2. a low-angle close-up for each of them, slowly circling, with a name plate
                    int idx = Mathf.Min(3, (int)((T - TWalkEnd) / TShot));
                    float u = Mathf.Clamp01((T - TWalkEnd - idx * TShot) / TShot);
                    var h = heroes.Find(x => x.name == shotOrder[idx]);
                    bool kneel = h.rig.pose == HeroRig.Pose.Kneel;
                    float side = (idx % 2 == 0 ? -1f : 1f);
                    float ang = side * Mathf.Lerp(20f, -6f, Ease3(u));
                    float d = Mathf.Lerp(kneel ? 3.4f : 4.1f, kneel ? 2.7f : 3.3f, Ease3(u));
                    Vector3 hp = h.t.position;
                    camPos = hp + Quaternion.Euler(0f, ang, 0f) * fwd * d + Vector3.up * (kneel ? 0.35f : 0.45f);
                    look = hp + Vector3.up * (kneel ? 0.78f : 1.12f);
                    fov = Mathf.Lerp(34f, 29f, u);
                    wantPlate = h.name;
                    if (shotIndex != idx) { shotIndex = idx; fadeA = 0f; }
                    // a quick dip at each cut
                    float local = T - TWalkEnd - idx * TShot;
                    fadeA = Mathf.Max(0f, 0.35f - local * 1.8f);
                }
                else
                {
                    // 3. the whole squad: a low, slowly rising sweep with a push-in
                    float u = Mathf.Clamp01((T - TGroup) / (TFadeOut - TGroup));
                    float ang = Mathf.Lerp(-24f, 22f, Ease3(u));
                    float d = Mathf.Lerp(8.6f, 6.0f, Ease3(u));
                    camPos = center + Quaternion.Euler(0f, ang, 0f) * fwd * d + Vector3.up * Mathf.Lerp(0.5f, 1.25f, Ease3(u));
                    look = center + Vector3.up * 1.0f;
                    fov = Mathf.Lerp(40f, 31f, Ease3(u));
                    wantPlate = "";
                    if (T >= TFadeOut) fadeA = Ease3((T - TFadeOut) / (TEnd - TFadeOut));
                }

                // the moment of "TOGETHER": a punch in the lens, a flash of light, a small shake
                float hk = Mathf.Exp(-(T - hitT) * 3.2f); if (!hit) hk = 0f;
                fov -= 3.5f * hk;
                Vector3 shake = hit ? new Vector3(Mathf.Sin(T * 61f), Mathf.Sin(T * 47f + 1f), 0f) * 0.035f * Mathf.Exp(-(T - hitT) * 5f) : Vector3.zero;
                lookSmooth = Vector3.Lerp(lookSmooth, look, T < TWalkEnd ? 1f - Mathf.Exp(-5f * dt) : 1f);
                cam.transform.position = camPos + shake;
                cam.transform.rotation = Quaternion.LookRotation((T < TWalkEnd ? lookSmooth : look) - camPos, Vector3.up);
                cam.fieldOfView = fov;

                // lights
                float lightIn = Ease3((T + 0.5f) / 3f);
                key.intensity = Mathf.Lerp(0f, 5.5f, lightIn) * (1f + 1.0f * hk);
                rimA.intensity = Mathf.Lerp(0f, 6.5f, lightIn) * (1f + 1.4f * hk);
                rimB.intensity = Mathf.Lerp(0f, 6.5f, lightIn) * (1f + 1.4f * hk);

                // ---------------- UI
                SetBars(topBar, bottomBar, Mathf.Clamp01((T + 0.5f) / 0.8f));
                float fin = T < 0f ? 1f : Mathf.Max(1f - Ease3(T / 1.0f), 0f);
                setFade(Mathf.Max(fin, fadeA));
                if (wantPlate != plateFor) { plateFor = wantPlate; plateT = T; nameText.text = plateFor; }
                float pa = plateFor == "" ? 0f : Ease3((T - plateT) / 0.35f) * (1f - Ease3((T - plateT - (TShot - 0.35f)) / 0.3f));
                plateGroup.alpha = pa;
                plateRt.anchoredPosition = new Vector2(150f - 40f * (1f - Ease3((T - plateT) / 0.4f)), 190f);

                // the spoken line
                float vt = T - TVo;
                if (vt >= 0f)
                {
                    string l1 = "WE ARE BACK AGAIN";
                    int n1 = Mathf.Clamp(Mathf.CeilToInt(l1.Length * Mathf.Clamp01((vt + 0.05f) / 1.0f)), 0, l1.Length);
                    line1.text = l1.Substring(0, n1);
                    string l2 = "";
                    if (vt >= 1.6f) l2 += "<color=#ffc83d>TOGETHER</color>";
                    if (vt >= 2.3f) l2 += ", STRONGER";
                    if (vt >= 3.08f) l2 += " AND BETTER!";
                    line2.text = l2;
                    float scale = 1f + 0.18f * hk;
                    line2.rectTransform.localScale = Vector3.one * scale;
                    float la = 1f - Ease3((T - (TFadeOut - 0.8f)) / 0.8f);
                    line1.color = new Color(1f, 1f, 1f, la);
                    var c2 = line2.color; c2.a = la; line2.color = c2;
                }
                yield return null;
            }

            // ---- the end: the squad stays posed under the MISSION COMPLETE screen ----
            if (skip)
            {
                foreach (var h in heroes)
                {
                    h.travelled = h.dist; var pos = h.start + fwd * h.dist; h.groundY = GroundY(pos, h.groundY);
                    h.t.position = new Vector3(pos.x, h.groundY, pos.z);
                    h.rig.walkWeight = 0f; h.rig.poseWeight = 1f; h.rig.Apply();
                }
                Vector3 endPos = center + Quaternion.Euler(0f, 22f, 0f) * fwd * 6.0f + Vector3.up * 1.25f;
                cam.transform.position = endPos; cam.transform.rotation = Quaternion.LookRotation(center + Vector3.up - endPos, Vector3.up); cam.fieldOfView = 31f;
                key.intensity = 5.5f; rimA.intensity = 6.5f; rimB.intensity = 6.5f;
                vo.Stop(); music.Stop();
            }
            line1.text = ""; line2.text = ""; plateGroup.alpha = 0f;
            SetBars(topBar, bottomBar, 0f);
            setFade(0.55f);
            foreach (var h in heroes) h.rig.live = true;
            ui.GetComponent<Canvas>().sortingOrder = -10;
            music.loop = false;
            foreach (var kv in quieted) if (kv.Key != null) kv.Key.volume = kv.Value;
            Time.timeScale = oldScale <= 0f ? 1f : oldScale;
            Playing = false; VictoryPlaying = false;
            if (onDone != null) onDone();
            // the cut-scene music fades out while the result screen's own theme fades in
            float f = 0f;
            while (f < 2f && music != null) { f += Time.unscaledDeltaTime; music.volume = 0.85f * (1f - f / 2f); yield return null; }
            if (music != null) music.Stop();
        }

        // ---------------------------------------------------------------------------------------------------------------- UI helpers
        static Image NewImg(Transform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>(); img.color = c; img.raycastTarget = false;
            return img;
        }

        static Text NewText(Transform parent, Font font, string text, int size, Color color, FontStyle style, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font; t.text = text; t.fontSize = size; t.color = color; t.fontStyle = style; t.alignment = anchor; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            var o = go.GetComponent<Outline>(); o.effectColor = new Color(0, 0, 0, 0.9f); o.effectDistance = new Vector2(2f, -2f);
            return t;
        }

        // bottom-left anchored (or bottom-centre when centred = true)
        static void SetRect(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, bool centred = false)
        {
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = centred ? new Vector2(0.5f, 0f) : new Vector2(0f, 0f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
        }
    }
}
