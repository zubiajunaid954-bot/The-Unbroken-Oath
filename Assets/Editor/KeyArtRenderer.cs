using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using GAMECON.Mission;
using GAMECON.Menu;

/// <summary>
/// Renders the key art (main menu / loading screen) from the real game assets: the rigged heroes in hero poses with rifles and swords,
/// in front of the Sun Temple, lit like a film still. Driven by a job file (KeyArt/job.json), polled by the editor, so the framing can
/// be iterated without recompiling. Output: a PNG (plus dump.txt for "dump" jobs).
/// </summary>
[InitializeOnLoad]
public static class KeyArtRenderer
{
    const string Dir = "KeyArt";
    const string Job = Dir + "/job.json";

    [Serializable] public class HeroJob { public string prefab; public float[] pos; public float yaw; public string pose; public string weapon; public float[] look; }
    [Serializable]
    public class JobData
    {
        public string mode = "render";               // render | dump
        public string scene = "Assets/Scenes/SunTemple_Game.unity";
        public string output = "KeyArt/render.png";
        public int width = 1920, height = 1080;
        public float[] camPos, camEuler; public float fov = 35f;
        public float[] sunEuler, sunColor; public float sunIntensity = -1f;
        public float ambient = -1f; public float fogDensity = -1f; public float[] fogColor;
        public string[] hide;                         // names of scene objects to hide
        public List<HeroJob> heroes = new List<HeroJob>();
        public float[] focus;                         // look-at point (overrides camEuler)
        public float exposure = 1f;
        public string method;                         // exec: "Type.Method" (a static method without arguments)
        public float previewTime = -1f;               // menu_preview: < 0 = the final state, otherwise the cut scene at this time
        public int panel;
        public float[] captures; public PlayAction[] actions; public float end = 20f;      // playtest                             // menu_preview: 0 none, 1 settings, 2 controls
        public float[] rimColor; public float rimIntensity = 0f;
        public bool snap = true;                      // positions are x,z + height above the ground
        public List<ViewJob> views = new List<ViewJob>();   // extra views (scouting): rendered with the same scene setup
        public List<LightJob> lights = new List<LightJob>();
        public bool hideRed = true;                    // hides small red gameplay markers of the demo scene
    }

    [Serializable] public class LightJob { public string type = "Directional"; public float[] pos, euler, color; public float intensity = 1f, range = 20f, angle = 40f; public bool shadows = true; public float[] target; }

    [Serializable] public class PlayAction { public float t; public string type, method; public string call; }
    [Serializable] public class PlayState { public string original, output; public float[] captures; public PlayAction[] actions; public float end; }

    [Serializable] public class ViewJob { public string name; public float[] camPos, focus; public float fov = 35f; }

    static bool busy;
    static double nextPoll;

    const string PlayFile = Dir + "/play_state.json";
    static PlayState play; static float playStart; static readonly List<string> playErrors = new List<string>(); static bool playHooked;
    static readonly HashSet<int> playCaptured = new HashSet<int>(), playActed = new HashSet<int>();

    static KeyArtRenderer()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += OnPlayState;
        if (File.Exists(PlayFile) && EditorApplication.isPlaying) BeginPlayTest();
    }

    static void BeginPlayTest()
    {
        if (playHooked) return;
        SessionState.SetBool("ka_playing", true);
        play = JsonUtility.FromJson<PlayState>(File.ReadAllText(PlayFile));
        playHooked = true; playStart = -1f;
        Application.logMessageReceived += OnLog;
        EditorApplication.update += PlayTick;
    }

    static void OnLog(string cond, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) playErrors.Add(type + ": " + cond + "\n" + stack);
        else if (cond.StartsWith("[StoryIntro]") || cond.StartsWith("[SceneTransition]")) playErrors.Add("LOG: " + cond);
    }

    static int lastBeat = -1;

    static void OnPlayState(PlayModeStateChange s)
    {
        File.AppendAllText(Dir + "/play_heartbeat.txt", "state " + s + " at " + DateTime.Now.ToString("HH:mm:ss") + "\n");
        if (s == PlayModeStateChange.EnteredPlayMode && File.Exists(PlayFile)) BeginPlayTest();
        if (s == PlayModeStateChange.EnteredEditMode && File.Exists(PlayFile) && SessionState.GetBool("ka_playing", false)) { SessionState.SetBool("ka_playing", false); EndPlayTest(); }
    }

    static void PlayTick()
    {
        if (play == null || !Application.isPlaying) return;
        if (playStart < 0f) playStart = Time.realtimeSinceStartup;
        float t = Time.realtimeSinceStartup - playStart;
        if (Mathf.FloorToInt(t) != lastBeat) { lastBeat = Mathf.FloorToInt(t); File.AppendAllText(Dir + "/play_heartbeat.txt", "t=" + t.ToString("0.0") + " scene=" + SceneManager.GetActiveScene().name + " frame=" + Time.frameCount + "\n"); }
        for (int i = 0; play.actions != null && i < play.actions.Length; i++)
        {
            var a = play.actions[i];
            if (playActed.Contains(i) || t < a.t) continue;
            playActed.Add(i);
            try
            {
                Type type = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) { type = asm.GetType(a.type); if (type != null) break; }
                var obj = type != null ? UnityEngine.Object.FindObjectOfType(type) : null;
                var m = type != null ? type.GetMethod(a.method, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) : null;
                if (m != null && obj != null) m.Invoke(obj, null); else playErrors.Add("action failed: " + a.type + "." + a.method);
            }
            catch (Exception e) { playErrors.Add("action exception: " + e); }
        }
        for (int i = 0; play.captures != null && i < play.captures.Length; i++)
        {
            if (playCaptured.Contains(i) || t < play.captures[i]) continue;
            playCaptured.Add(i);
            try { CapturePlay(play.output + "_" + i + ".png"); } catch (Exception e) { playErrors.Add("capture exception: " + e); }
        }
        if (t >= play.end) { File.WriteAllText(Dir + "/playtest_log.txt", "errors: " + playErrors.Count + "\n" + string.Join("\n---\n", playErrors.ToArray())); EditorApplication.isPlaying = false; }
    }

    static void CapturePlay(string path)
    {
        var cam = Camera.main;
        if (cam == null) { foreach (var c in UnityEngine.Object.FindObjectsOfType<Camera>()) if (c.enabled) { cam = c; break; } }
        if (cam == null) { playErrors.Add("no camera to capture"); return; }
        var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>().Where(c => c.renderMode == RenderMode.ScreenSpaceOverlay).ToList();
        foreach (var c in canvases) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = 1f; }
        int oldMask = cam.cullingMask; cam.cullingMask = -1;
        Canvas.ForceUpdateCanvases();
        Capture(cam, 1280, 720, path);
        cam.cullingMask = oldMask;
        foreach (var c in canvases) { c.renderMode = RenderMode.ScreenSpaceOverlay; }
    }

    static void EndPlayTest()
    {
        string original = null;
        try { original = JsonUtility.FromJson<PlayState>(File.ReadAllText(PlayFile)).original; } catch { }
        File.Delete(PlayFile);
        playHooked = false;
        if (!File.Exists(Dir + "/playtest_log.txt")) File.WriteAllText(Dir + "/playtest_log.txt", "the play test did not finish\n" + string.Join("\n---\n", playErrors.ToArray()));
        if (!string.IsNullOrEmpty(original) && File.Exists(original)) EditorApplication.delayCall += () => EditorSceneManager.OpenScene(original, OpenSceneMode.Single);
        File.WriteAllText(Dir + "/status.txt", "playtest done\n" + DateTime.Now);
    }

    [MenuItem("GAMECON/Render Key Art (run job)")]
    public static void RunNow() { Poll(true); }

    static void Poll() { Poll(false); }

    static void Poll(bool force)
    {
        if (busy || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        if (!force && EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(Job)) return;
        string json;
        try { json = File.ReadAllText(Job); } catch { return; }
        try { File.Copy(Job, Dir + "/job.running.json", true); File.Delete(Job); } catch { return; }
        busy = true;
        string status = "ok";
        try
        {
            var job = JsonUtility.FromJson<JobData>(json);
            Execute(job);
        }
        catch (Exception e) { status = "ERROR: " + e; Debug.LogError("[KeyArt] " + e); }
        if (!File.Exists(PlayFile)) File.WriteAllText(Dir + "/status.txt", status + "\n" + DateTime.Now);
        busy = false;
    }

    static Vector3 V3(float[] a, Vector3 d) { return a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : d; }

    static void Execute(JobData job)
    {
        if (job.mode == "exec") { Exec(job); return; }
        if (job.mode == "portrait") { PortraitJob(job); return; }
        if (job.mode == "playtest") { StartPlayTest(job); return; }

        // never lose the user's work: only switch scenes when nothing is unsaved
        string original = SceneManager.GetActiveScene().path;
        if (SceneManager.GetActiveScene().isDirty) throw new Exception("the open scene has unsaved changes - save it first");
        var scene = EditorSceneManager.OpenScene(job.scene, OpenSceneMode.Single);
        try
        {
            if (job.mode == "dump") { Dump(scene); return; }
            if (job.mode == "menu_preview") { MenuPreview(job, scene); return; }
            RenderJob(job, scene);
        }
        finally
        {
            if (!string.IsNullOrEmpty(original) && original != job.scene) EditorSceneManager.OpenScene(original, OpenSceneMode.Single);
        }
    }

    static void StartPlayTest(JobData job)
    {
        if (SceneManager.GetActiveScene().isDirty) throw new Exception("the open scene has unsaved changes - save it first");
        var st = new PlayState { original = SceneManager.GetActiveScene().path, output = job.output.Replace(".png", ""), captures = job.captures, actions = job.actions, end = job.end };
        if (File.Exists(Dir + "/playtest_log.txt")) File.Delete(Dir + "/playtest_log.txt");
        File.WriteAllText(PlayFile, JsonUtility.ToJson(st));
        EditorSceneManager.OpenScene(job.scene, OpenSceneMode.Single);
        File.WriteAllText(Dir + "/status.txt.pending", "started");
        EditorApplication.EnterPlaymode();
    }

    // a bust portrait of a rigged hero on a transparent background (for the dialogue box)
    static void PortraitJob(JobData job)
    {
        if (SceneManager.GetActiveScene().isDirty) throw new Exception("the open scene has unsaved changes - save it first");
        string original = SceneManager.GetActiveScene().path;
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null; RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.50f, 0.50f, 0.56f); RenderSettings.fog = false;
            var hj = job.heroes[0];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Rigged/" + hj.prefab + "_Rigged.prefab");
            if (prefab == null) throw new Exception("rigged prefab missing: " + hj.prefab);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.Euler(0f, hj.yaw, 0f);
            var hr = go.AddComponent<HeroRig>(); hr.Setup(go.GetComponent<CaptiveRig>()); hr.pose = HeroRig.Pose.Stand; hr.poseWeight = 1f; hr.walkWeight = 0f; hr.Apply();

            Func<float, float, float, Color, float, Light> L = (x, y, z, c, i) =>
            {
                var g = new GameObject("L"); var l = g.AddComponent<Light>(); l.type = LightType.Directional; l.color = c; l.intensity = i; l.shadows = LightShadows.Soft;
                g.transform.rotation = Quaternion.Euler(x, y, z); return l;
            };
            L(28f, 205f, 0f, new Color(1f, 0.93f, 0.84f), 1.25f);          // key: from the front left, warm
            L(10f, 140f, 0f, new Color(0.7f, 0.8f, 1f), 0.45f);            // fill: from the front right, cool
            L(25f, 20f, 0f, new Color(1f, 0.8f, 0.55f), 0.7f);             // rim: from behind

            float head = hr.rig.bones[3].position.y;                      // the head bone
            var camGo = new GameObject("PortraitCamera"); var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0f, 0f, 0f, 0f); cam.fieldOfView = job.fov; cam.allowHDR = false; cam.allowMSAA = true;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 50f;
            Vector3 target = new Vector3(0f, head - 0.10f, 0f);
            camGo.transform.position = target + Quaternion.Euler(0f, hj.yaw, 0f) * new Vector3(0f, 0.04f, job.camPos != null ? job.camPos[2] : 2.6f);
            camGo.transform.rotation = Quaternion.LookRotation(target - camGo.transform.position, Vector3.up);

            var rt = new RenderTexture(job.width, job.height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(job.width, job.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, job.width, job.height), 0, 0); tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null;
            Directory.CreateDirectory(Path.GetDirectoryName(job.output));
            File.WriteAllBytes(job.output, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
        }
        finally
        {
            if (!string.IsNullOrEmpty(original) && File.Exists(original)) EditorSceneManager.OpenScene(original, OpenSceneMode.Single);
        }
    }

    static void Exec(JobData job)
    {
        if (SceneManager.GetActiveScene().isDirty) throw new Exception("the open scene has unsaved changes - save it first");
        int dot = job.method.LastIndexOf('.');
        string typeName = job.method.Substring(0, dot), methodName = job.method.Substring(dot + 1);
        Type type = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) { type = asm.GetType(typeName); if (type != null) break; }
        if (type == null) throw new Exception("type not found: " + typeName);
        var m = type.GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (m == null) throw new Exception("method not found: " + job.method);
        object r = m.Invoke(null, null);
        File.WriteAllText(Dir + "/exec_result.txt", r != null ? r.ToString() : "(no result)");
    }

    static void MenuPreview(JobData job, Scene scene)
    {
        var d = UnityEngine.Object.FindObjectOfType<MainMenuDirector>();
        if (d == null) throw new Exception("no MainMenuDirector in " + job.scene);
        if (job.previewTime >= 0f) d.PreviewAt(job.previewTime); else d.PreviewFinalState();
        d.PreviewPanel(job.panel);
        d.ApplyFonts();
        var canvas = d.canvas; var cam = d.cam;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 1f;
        Canvas.ForceUpdateCanvases();
        Capture(cam, job.width, job.height, job.output);
    }

    static void Dump(Scene scene)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var r in scene.GetRootGameObjects())
        {
            var rs = r.GetComponentsInChildren<Renderer>(true);
            Bounds b = new Bounds(r.transform.position, Vector3.zero);
            foreach (var x in rs) b.Encapsulate(x.bounds);
            sb.AppendLine(r.name + "  active=" + r.activeSelf + "  pos=" + r.transform.position + "  boundsCenter=" + b.center + "  size=" + b.size + "  children=" + r.transform.childCount);
        }
        foreach (var c in UnityEngine.Object.FindObjectsOfType<Camera>(true)) sb.AppendLine("CAMERA " + c.name + " pos=" + c.transform.position + " euler=" + c.transform.eulerAngles + " fov=" + c.fieldOfView);
        foreach (var l in UnityEngine.Object.FindObjectsOfType<Light>(true)) sb.AppendLine("LIGHT " + l.name + " type=" + l.type + " pos=" + l.transform.position + " euler=" + l.transform.eulerAngles + " int=" + l.intensity + " col=" + l.color + " mode=" + l.lightmapBakeType);
        sb.AppendLine("SKYBOX " + (RenderSettings.skybox != null ? RenderSettings.skybox.name : "none") + " fog=" + RenderSettings.fog + " fogDensity=" + RenderSettings.fogDensity + " ambientMode=" + RenderSettings.ambientMode + " ambient=" + RenderSettings.ambientLight);
        sb.AppendLine("LIGHTMAPS " + LightmapSettings.lightmaps.Length);
        File.WriteAllText(Dir + "/dump.txt", sb.ToString());
    }

    static void RenderJob(JobData job, Scene scene)
    {
        // ---- scene look ----
        if (job.hide != null)
            foreach (var n in job.hide)
                foreach (var g in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Where(t => t.name == n).Select(t => t.gameObject).ToList())
                    g.SetActive(false);

        // the scene's own cameras must not render
        foreach (var c in UnityEngine.Object.FindObjectsOfType<Camera>()) c.enabled = false;

        Light sun = RenderSettings.sun;
        if (sun == null) sun = UnityEngine.Object.FindObjectsOfType<Light>().FirstOrDefault(l => l.type == LightType.Directional);
        if (sun != null)
        {
            if (job.sunEuler != null) sun.transform.rotation = Quaternion.Euler(V3(job.sunEuler, Vector3.zero));
            if (job.sunColor != null) sun.color = new Color(job.sunColor[0], job.sunColor[1], job.sunColor[2]);
            if (job.sunIntensity >= 0f) sun.intensity = job.sunIntensity;
            sun.shadows = LightShadows.Soft; sun.shadowStrength = 1f;
        }
        if (job.ambient >= 0f) { RenderSettings.ambientIntensity = job.ambient; }
        if (job.fogDensity >= 0f) { RenderSettings.fog = job.fogDensity > 0f; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = job.fogDensity; }
        if (job.fogColor != null) RenderSettings.fogColor = new Color(job.fogColor[0], job.fogColor[1], job.fogColor[2]);
        DynamicGI.UpdateEnvironment();

        if (job.hideRed)
            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r is SkinnedMeshRenderer || r.bounds.size.magnitude > 4f) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !m.HasProperty("_Color")) continue;
                    Color c = m.color;
                    if (c.r > 0.6f && c.g < 0.3f && c.b < 0.3f) { r.gameObject.SetActive(false); break; }
                }
            }

        // ---- heroes ----
        var weapons = new GameObject("KeyArtProps");
        foreach (var hj in job.heroes) BuildHero(hj, weapons.transform);

        foreach (var lj in job.lights)
        {
            var lg = new GameObject("KeyArtLight"); var l = lg.AddComponent<Light>();
            l.type = lj.type == "Spot" ? LightType.Spot : lj.type == "Point" ? LightType.Point : LightType.Directional;
            l.intensity = lj.intensity; l.range = lj.range; l.spotAngle = lj.angle; l.shadows = lj.shadows ? LightShadows.Soft : LightShadows.None;
            l.color = lj.color != null ? new Color(lj.color[0], lj.color[1], lj.color[2]) : Color.white;
            Vector3 lp = V3(lj.pos, Vector3.zero); if (job.snap && lj.pos != null) lp.y += GroundY(lp.x, lp.z);
            lg.transform.position = lp;
            if (lj.target != null)
            {
                Vector3 tg = V3(lj.target, Vector3.zero); if (job.snap) tg.y += GroundY(tg.x, tg.z);
                lg.transform.rotation = Quaternion.LookRotation(tg - lp);
            }
            else lg.transform.rotation = Quaternion.Euler(V3(lj.euler, new Vector3(50, 0, 0)));
        }

        // a cool rim light for separation
        if (job.rimIntensity > 0f)
        {
            var rg = new GameObject("RimLight"); var rl = rg.AddComponent<Light>();
            rl.type = LightType.Directional; rl.intensity = job.rimIntensity; rl.shadows = LightShadows.None;
            rl.color = job.rimColor != null ? new Color(job.rimColor[0], job.rimColor[1], job.rimColor[2]) : new Color(0.6f, 0.75f, 1f);
            Vector3 camF = Quaternion.Euler(V3(job.camEuler, Vector3.zero)) * Vector3.forward;
            rg.transform.rotation = Quaternion.LookRotation(Quaternion.Euler(0f, 160f, 0f) * new Vector3(camF.x, 0f, camF.z) + Vector3.down * 0.35f);
        }

        // ---- camera + render ----
        var camGo = new GameObject("KeyArtCamera");
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.1f; cam.farClipPlane = 1500f; cam.allowHDR = true; cam.allowMSAA = true;
        cam.clearFlags = CameraClearFlags.Skybox;

        var views = new List<ViewJob>(job.views);
        if (job.camPos != null) views.Add(new ViewJob { name = "", camPos = job.camPos, focus = job.focus, fov = job.fov });
        foreach (var v in views)
        {
            Vector3 cp = V3(v.camPos, new Vector3(0, 2, -10));
            if (job.snap) cp.y += GroundY(cp.x, cp.z);
            camGo.transform.position = cp;
            cam.fieldOfView = v.fov;
            if (v.focus != null)
            {
                Vector3 f = V3(v.focus, Vector3.zero); if (job.snap) f.y += GroundY(f.x, f.z);
                camGo.transform.rotation = Quaternion.LookRotation(f - cp, Vector3.up);
            }
            else camGo.transform.rotation = Quaternion.Euler(V3(job.camEuler, Vector3.zero));
            string outPath = job.output;
            if (!string.IsNullOrEmpty(v.name)) outPath = Path.Combine(Path.GetDirectoryName(job.output), Path.GetFileNameWithoutExtension(job.output) + "_" + v.name + ".png");
            Capture(cam, job.width, job.height, outPath);
        }
    }

    static void Capture(Camera cam, int w, int h, string outPath)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;
        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
        File.WriteAllBytes(outPath, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
    }

    static float GroundY(float x, float z)
    {
        RaycastHit hit;
        if (Physics.Raycast(new Vector3(x, 400f, z), Vector3.down, out hit, 800f, ~0, QueryTriggerInteraction.Ignore)) return hit.point.y;
        return 0f;
    }

    // ---------------------------------------------------------------------------------------------------- heroes
    static void BuildHero(HeroJob hj, Transform props)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Rigged/" + hj.prefab + "_Rigged.prefab");
        if (prefab == null) throw new Exception("rigged prefab missing: " + hj.prefab);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Vector3 hp = V3(hj.pos, Vector3.zero); hp.y += GroundY(hp.x, hp.z);
        go.transform.position = hp;
        go.transform.rotation = Quaternion.Euler(0f, hj.yaw, 0f);
        var cr = go.GetComponent<CaptiveRig>();
        var hr = go.AddComponent<HeroRig>();
        hr.Setup(cr);
        HeroRig.Pose p; if (!Enum.TryParse(hj.pose, out p)) p = HeroRig.Pose.Stand;
        hr.pose = p; hr.poseWeight = 1f; hr.walkWeight = 0f;
        hr.Apply();                                  // torso and legs; the arms follow the weapon below

        var T = go.transform;
        Func<float, float, float, Vector3> L = (x, y, z) => T.TransformPoint(new Vector3(x, y, z));
        Func<float, float, float, Vector3> D = (x, y, z) => T.TransformDirection(new Vector3(x, y, z)).normalized;
        switch (hj.weapon)
        {
            case "rifle_ready":     // two hands, the rifle across the body, muzzle up and forward
            {
                var rifle = NewRifle(props);
                Quaternion rot = Quaternion.LookRotation(D(-0.80f, 0.52f, 0.30f), D(0.35f, 0.85f, -0.20f));
                Place(rifle, new Vector3(0f, -0.10f, -0.07f), L(0.24f, 1.00f, 0.18f), rot);
                hr.SolveArm(true, rifle.TransformPoint(new Vector3(0f, -0.10f, -0.07f)), D(0.7f, -0.7f, -0.2f));
                hr.SolveArm(false, rifle.TransformPoint(new Vector3(0f, -0.045f, 0.20f)), D(-0.6f, -0.8f, -0.1f));
                break;
            }
            case "rifle_low":       // hanging from the right hand, muzzle down and forward; the left hand on the hip
            {
                var rifle = NewRifle(props);
                Quaternion rot = Quaternion.LookRotation(D(0.04f, -0.50f, 0.86f), D(0.10f, 0.86f, 0.50f));
                Place(rifle, new Vector3(0f, -0.10f, -0.07f), L(0.26f, 0.92f, 0.10f), rot);
                hr.SolveArm(true, rifle.TransformPoint(new Vector3(0f, -0.10f, -0.07f)), D(0.6f, -0.8f, -0.3f));
                hr.SolveArm(false, L(-0.23f, 0.98f, 0.0f), D(-0.8f, -0.4f, -0.4f), 0.04f);
                break;
            }
            case "sword_planted":   // the point in the ground, both hands on the pommel
            {
                var sword = NewSword(props);
                Quaternion rot = Quaternion.LookRotation(D(0f, 0f, 1f), D(0f, -1f, 0f));
                Place(sword, Vector3.zero, L(0.0f, 0.99f, 0.30f), rot);
                hr.SolveArm(true, sword.TransformPoint(new Vector3(0f, -0.045f, 0f)), D(0.7f, -0.7f, -0.2f));
                hr.SolveArm(false, sword.TransformPoint(new Vector3(0f, 0.025f, 0f)), D(-0.7f, -0.7f, -0.2f));
                break;
            }
            case "sword_low":       // in the right hand, the point down and out; the left hand on the hip
            {
                var sword = NewSword(props);
                Quaternion rot = Quaternion.LookRotation(D(0f, 0f, 1f), D(0.30f, -0.60f, 0.74f));
                Place(sword, Vector3.zero, L(0.30f, 0.94f, 0.10f), rot);
                hr.SolveArm(true, sword.TransformPoint(Vector3.zero), D(0.6f, -0.8f, -0.3f), 0.05f);
                hr.SolveArm(false, L(-0.23f, 0.98f, 0.0f), D(-0.8f, -0.4f, -0.4f), 0.04f);
                break;
            }
        }
        hr.Apply();
    }

    static void Place(Transform prop, Vector3 gripLocal, Vector3 gripWorld, Quaternion rot)
    {
        prop.rotation = rot;
        prop.position = gripWorld - rot * gripLocal;
    }

    static Transform NewRifle(Transform parent)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Rigged/Gun_DeathProp.prefab");
        if (prefab == null) throw new Exception("Gun_DeathProp.prefab missing (run GAMECON > Build Death Scene)");
        var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        g.transform.SetParent(parent, true);
        return g.transform;
    }

    // ---------------------------------------------------------------------------------------------------- a sword (procedural)
    static Material Mat(Color c, float metallic, float smooth)
    {
        var m = new Material(Shader.Find("Standard")) { color = c };
        m.SetFloat("_Metallic", metallic); m.SetFloat("_Glossiness", smooth);
        return m;
    }

    /// <summary>A longsword: origin at the middle of the grip, the blade along +Y (0.9 m), the flat sides facing +-Z.</summary>
    public static Transform NewSword(Transform parent)
    {
        var root = new GameObject("Sword").transform; root.SetParent(parent, false);
        var steel = Mat(new Color(0.78f, 0.80f, 0.83f), 1f, 0.88f);
        var gold = Mat(new Color(0.86f, 0.64f, 0.24f), 0.95f, 0.75f);
        var leather = Mat(new Color(0.16f, 0.09f, 0.05f), 0f, 0.35f);

        // blade: a tapered diamond section with a ridge
        const int N = 14; float y0 = 0.085f, y1 = 0.93f;
        var verts = new List<Vector3>(); var tris = new List<int>();
        for (int i = 0; i <= N; i++)
        {
            float u = i / (float)N, y = Mathf.Lerp(y0, y1, u);
            float tip = u > 0.85f ? Mathf.Clamp01((1f - u) / 0.15f) : 1f;
            float w = Mathf.Lerp(0.028f, 0.020f, Mathf.Min(1f, u / 0.8f)) * tip;
            float th = Mathf.Lerp(0.0075f, 0.004f, u) * tip;
            verts.Add(new Vector3(-w, y, 0)); verts.Add(new Vector3(0, y, th)); verts.Add(new Vector3(w, y, 0)); verts.Add(new Vector3(0, y, -th));
        }
        for (int i = 0; i < N; i++)
        {
            int a = i * 4, b = (i + 1) * 4;
            for (int k = 0; k < 4; k++)
            {
                int k2 = (k + 1) % 4;
                tris.Add(a + k); tris.Add(b + k); tris.Add(b + k2);
                tris.Add(a + k); tris.Add(b + k2); tris.Add(a + k2);
            }
        }
        var mesh = new Mesh(); mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var blade = new GameObject("Blade", typeof(MeshFilter), typeof(MeshRenderer)); blade.transform.SetParent(root, false);
        blade.GetComponent<MeshFilter>().sharedMesh = mesh; blade.GetComponent<MeshRenderer>().sharedMaterial = steel;

        Func<PrimitiveType, string, Vector3, Vector3, Material, Transform> Part = (type, name, pos, scale, mat) =>
        {
            var g = GameObject.CreatePrimitive(type); g.name = name;
            UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(root, false); g.transform.localPosition = pos; g.transform.localScale = scale;
            g.GetComponent<MeshRenderer>().sharedMaterial = mat; return g.transform;
        };
        Part(PrimitiveType.Cube, "Guard", new Vector3(0, 0.075f, 0), new Vector3(0.19f, 0.020f, 0.032f), gold);
        Part(PrimitiveType.Sphere, "GuardL", new Vector3(-0.095f, 0.075f, 0), new Vector3(0.030f, 0.030f, 0.030f), gold);
        Part(PrimitiveType.Sphere, "GuardR", new Vector3(0.095f, 0.075f, 0), new Vector3(0.030f, 0.030f, 0.030f), gold);
        Part(PrimitiveType.Cylinder, "Grip", new Vector3(0, -0.005f, 0), new Vector3(0.034f, 0.075f, 0.034f), leather);
        for (int i = -1; i <= 1; i++) Part(PrimitiveType.Cylinder, "Ring" + i, new Vector3(0, -0.005f + i * 0.045f, 0), new Vector3(0.038f, 0.004f, 0.038f), gold);
        Part(PrimitiveType.Sphere, "Pommel", new Vector3(0, -0.092f, 0), new Vector3(0.052f, 0.052f, 0.052f), gold);
        return root;
    }
}
