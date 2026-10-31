using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using GAMECON.Weather;

/// <summary>
/// Turns the Sun Temple game scene into a rainy night: moonlight, dark sky/fog,
/// torch + lamp lights, rain, thunder + lightning, music, and extra enemies.
/// Output: Assets/Scenes/SunTemple_Night.unity (the day scene stays untouched).
/// </summary>
public static class NightStormBuilder
{
    const string DayScene = "Assets/Scenes/SunTemple_Game.unity";
    const string NightScene = "Assets/Scenes/SunTemple_Night.unity";
    const string EnvFolder = "Assets/Environment/Night";
    const int ExtraEnemies = 10;

    [MenuItem("GAMECON/Build Night + Rain Scene")]
    public static void Build()
    {
        if (!File.Exists(DayScene))
        {
            EditorUtility.DisplayDialog("Error", "Run 'Build Sun Temple Game Scene' first (SunTemple_Game.unity not found).", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        ConfigureAudioImports();

        if (File.Exists(NightScene)) AssetDatabase.DeleteAsset(NightScene);
        AssetDatabase.CopyAsset(DayScene, NightScene);
        AssetDatabase.Refresh();
        Scene scene = EditorSceneManager.OpenScene(NightScene, OpenSceneMode.Single);

        Directory.CreateDirectory(EnvFolder);

        Light moon = SetupLighting();
        Vector3 playerPos = FindPlayerPos();
        AddFireLights(scene, playerPos);
        Transform weather = BuildWeather(scene, moon);
        AddToGroup(scene, weather, "====== WEATHER & AUDIO ======", 7);

        TryBakeNavMesh();
        int added = SpawnExtraEnemies(scene, playerPos);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, NightScene);
        AddToBuildSettings(NightScene);

        Debug.Log("[NightStorm] Done. +" + added + " enemies. Scene: " + NightScene);
        EditorUtility.DisplayDialog("Night + Rain scene ready",
            "Created " + NightScene + "\n\n- Moonlit night, dark sky and fog\n- Torches, lamps and bonfires lit\n- Rain, thunder + lightning\n- Music and rain audio\n- +" + added + " enemies\n\nSunTemple_Game (day) is untouched. Press Play!", "OK");
    }

    // ---------------- Audio import ----------------
    static void ConfigureAudioImports()
    {
        string[] streams = { "Assets/Audio/Music/Night_Siege_Theme.wav", "Assets/Audio/Ambience/Rain_Loop.wav" };
        foreach (var p in streams) SetImport(p, AudioClipLoadType.Streaming, true);
        foreach (var p in Directory.GetFiles("Assets/Audio/SFX", "*.wav")) SetImport(p.Replace('\\', '/'), AudioClipLoadType.CompressedInMemory, false);
        AssetDatabase.Refresh();
    }

    static void SetImport(string path, AudioClipLoadType load, bool forceMono)
    {
        var imp = AssetImporter.GetAtPath(path) as AudioImporter;
        if (imp == null) return;
        var s = imp.defaultSampleSettings;
        s.loadType = load;
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = 0.6f;
        imp.defaultSampleSettings = s;
        imp.SaveAndReimport();
    }

    // ---------------- Lighting ----------------
    static Light SetupLighting()
    {
        Lightmapping.Clear(); // day-time baked lightmaps would make everything look lit
        Lightmapping.lightingDataAsset = null;

        Light moon = null;
        foreach (Light l in UnityEngine.Object.FindObjectsOfType<Light>(true))
        {
            if (l.type == LightType.Directional)
            {
                if (moon == null)
                {
                    moon = l;
                    l.name = "Moon Directional";
                    l.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
                    l.color = new Color(0.42f, 0.52f, 0.85f);
                    l.intensity = 0.45f;
                    l.shadows = LightShadows.Soft;
                    l.shadowStrength = 0.85f;
                }
                else l.enabled = false;
                l.lightmapBakeType = LightmapBakeType.Realtime;
            }
            else
            {
                // old day-time bounce/fill lights are not wanted at night
                l.lightmapBakeType = LightmapBakeType.Realtime;
                if (l.name.StartsWith("Sun") || l.name.StartsWith("Point light")) l.enabled = false;
            }
        }
        if (moon == null) // scene had no directional light
        {
            var go = new GameObject("Moon Directional");
            moon = go.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.42f, 0.52f, 0.85f);
            moon.intensity = 0.45f;
            go.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
        }

        // Baked daytime reflection probes would reflect a bright sky.
        foreach (var rp in UnityEngine.Object.FindObjectsOfType<ReflectionProbe>(true)) rp.enabled = false;

        // Night sky
        var sky = new Material(Shader.Find("Skybox/Procedural"));
        sky.SetFloat("_SunDisk", 0);
        sky.EnableKeyword("_SUNDISK_NONE");
        sky.DisableKeyword("_SUNDISK_SIMPLE");
        sky.DisableKeyword("_SUNDISK_HIGH_QUALITY");
        sky.SetFloat("_AtmosphereThickness", 0.35f);
        sky.SetColor("_SkyTint", new Color(0.12f, 0.15f, 0.30f));
        sky.SetColor("_GroundColor", new Color(0.02f, 0.025f, 0.05f));
        sky.SetFloat("_Exposure", 0.35f);
        AssetDatabase.DeleteAsset(EnvFolder + "/NightSky.mat");
        AssetDatabase.CreateAsset(sky, EnvFolder + "/NightSky.mat");

        RenderSettings.skybox = sky;
        RenderSettings.sun = moon;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.05f, 0.065f, 0.11f);
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = 0.25f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.035f, 0.045f, 0.075f);
        RenderSettings.fogDensity = 0.012f;
        return moon;
    }

    // ---------------- Torches, lamps, bonfires ----------------
    static Vector3 FindPlayerPos()
    {
        var uc = UnityEngine.Object.FindObjectOfType<scgFullBodyController.ThirdPersonUserControl>();
        if (uc != null) return uc.transform.position;
        var p = GameObject.FindGameObjectWithTag("Player");
        return p != null ? p.transform.position : Vector3.zero;
    }

    static void AddFireLights(Scene scene, Vector3 center)
    {
        // 1. every lamp / torch / candle / bonfire already placed by Sun Temple
        var kinds = new (string key, Color col, float range, float intensity)[]
        {
            ("Bonfire",   new Color(1f, 0.55f, 0.22f), 14f, 2.6f),
            ("FireTorch", new Color(1f, 0.58f, 0.25f), 10f, 2.0f),
            ("Lamp",      new Color(1f, 0.75f, 0.45f), 9f, 1.6f),
            ("Candles",   new Color(1f, 0.70f, 0.35f), 4f, 0.9f),
        };
        int count = 0;
        foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>())
        {
            if (t.gameObject.scene != scene || t.name == "NightLight") continue;
            foreach (var k in kinds)
            {
                if (!t.name.Contains(k.key) || t.name.Contains("DrainPipe")) continue;
                // only the prop root (skip children whose parent already matched)
                if (t.parent != null && t.parent.name.Contains(k.key)) break;
                AddLight(t, k.col, k.range, k.intensity, k.key != "Candles");
                count++;
                break;
            }
        }

        // 2. extra bonfires around the player so the area reads clearly at night
        var bonfire = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Sun_Temple/Prefabs/FX/FX_Bonfire_A.prefab");
        if (bonfire != null)
        {
            var rng = new System.Random(11);
            for (int i = 0; i < 5; i++)
            {
                float ang = (i * 72f + 20f) * Mathf.Deg2Rad;
                float dist = 14f + (float)rng.NextDouble() * 12f;
                Vector3 p = center + new Vector3(Mathf.Cos(ang) * dist, 0, Mathf.Sin(ang) * dist);
                if (!Physics.Raycast(p + Vector3.up * 60f, Vector3.down, out RaycastHit hit, 200f)) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(bonfire, scene);
                go.name = "Bonfire_Night_" + (i + 1);
                go.transform.position = hit.point;
                AddLight(go.transform, kinds[0].col, kinds[0].range, kinds[0].intensity, true);
                AddToGroup(scene, go.transform, "====== LIGHTING ======", 1, "Night Fires");
                count++;
            }
        }
        Debug.Log("[NightStorm] Added " + count + " fire/lamp lights.");
    }

    static void AddLight(Transform parent, Color col, float range, float intensity, bool flicker)
    {
        var go = new GameObject("NightLight");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0, 1.2f, 0);
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = col;
        l.range = range;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
        l.lightmapBakeType = LightmapBakeType.Realtime;
        if (flicker) go.AddComponent<FlickerLight>();
    }

    // ---------------- Rain, thunder, music ----------------
    static Transform BuildWeather(Scene scene, Light moon)
    {
        var root = new GameObject("WeatherSystem");
        SceneManager.MoveGameObjectToScene(root, scene);

        // Rain particles (follow the player via StormController)
        var rainGo = new GameObject("Rain");
        rainGo.transform.SetParent(root.transform, false);
        rainGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // emit downward
        var ps = rainGo.AddComponent<ParticleSystem>();
        ps.Stop();
        var main = ps.main;
        main.loop = true;
        main.startLifetime = 1.05f;
        main.startSpeed = 28f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 9000;
        main.gravityModifier = 0.2f;
        main.startColor = new Color(0.75f, 0.85f, 1f, 0.45f);
        var em = ps.emission; em.rateOverTime = 4500f;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = new Vector3(48f, 48f, 1f);
        var rend = rainGo.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Stretch;
        rend.velocityScale = 0.04f;
        rend.lengthScale = 2.5f;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;
        var rainMat = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
        rainMat.SetColor("_TintColor", new Color(0.8f, 0.88f, 1f, 0.5f));
        AssetDatabase.DeleteAsset(EnvFolder + "/RainDrop.mat");
        AssetDatabase.CreateAsset(rainMat, EnvFolder + "/RainDrop.mat");
        rend.sharedMaterial = rainMat;
        ps.Play();

        // Audio
        var rainSrc = MakeSource(root.transform, "RainSound", "Assets/Audio/Ambience/Rain_Loop.wav", 0.55f, true);
        var musicSrc = MakeSource(root.transform, "BackgroundMusic", "Assets/Audio/Music/Night_Siege_Theme.wav", 0.4f, true);
        var thunderSrc = MakeSource(root.transform, "ThunderSound", null, 1f, false);

        var storm = root.AddComponent<StormController>();
        storm.moonLight = moon;
        storm.thunderSource = thunderSrc;
        storm.thunderNear = new[] { Load("Assets/Audio/SFX/Thunder_Near.wav"), Load("Assets/Audio/SFX/Thunder_Mid.wav") };
        storm.thunderFar = new[] { Load("Assets/Audio/SFX/Thunder_Mid.wav"), Load("Assets/Audio/SFX/Thunder_Far.wav") };
        storm.followOffset = new Vector3(0, 22, 0);
        return root.transform;
    }

    static AudioClip Load(string p) { return AssetDatabase.LoadAssetAtPath<AudioClip>(p); }

    static AudioSource MakeSource(Transform parent, string name, string clipPath, float vol, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var s = go.AddComponent<AudioSource>();
        s.clip = clipPath != null ? Load(clipPath) : null;
        s.volume = vol;
        s.loop = loop;
        s.playOnAwake = loop;
        s.spatialBlend = 0f; // 2D
        return s;
    }

    // ---------------- Enemies ----------------
    static int SpawnExtraEnemies(Scene scene, Vector3 center)
    {
        var hard = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/scgFullBodyController/Prefabs/AI/Bot(Ai) Hard.prefab");
        var med = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/scgFullBodyController/Prefabs/AI/Bot(Ai) Medium.prefab");
        if (hard == null && med == null) { Debug.LogError("[NightStorm] Bot prefabs not found."); return 0; }

        Transform group = FindOrCreateGroup(scene, "====== ENEMIES ======", 4);
        int existing = UnityEngine.Object.FindObjectsOfType<scgFullBodyController.AiController>().Length;
        int added = 0;
        var rng = new System.Random(23);
        for (int i = 0; i < ExtraEnemies; i++)
        {
            Vector3 p = center;
            bool ok = false;
            for (int tries = 0; tries < 12 && !ok; tries++)
            {
                float ang = (float)(rng.NextDouble() * Math.PI * 2);
                float dist = 18f + (float)rng.NextDouble() * 30f; // 18-48 m: out of sight at first
                Vector3 c = center + new Vector3(Mathf.Cos(ang) * dist, 0, Mathf.Sin(ang) * dist);
                if (Physics.Raycast(c + Vector3.up * 60f, Vector3.down, out RaycastHit hit, 200f)) c.y = hit.point.y;
                if (NavMesh.SamplePosition(c, out NavMeshHit nh, 6f, NavMesh.AllAreas)) { p = nh.position; ok = true; }
                else if (tries == 11) { p = c; ok = true; }
            }
            var prefab = (i % 2 == 0 && hard != null) ? hard : (med != null ? med : hard);
            var bot = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            bot.name = "Enemy_Soldier_" + (existing + i + 1);
            bot.transform.SetParent(group, true);
            bot.transform.position = p;
            bot.transform.LookAt(new Vector3(center.x, p.y, center.z));
            var ai = bot.GetComponent<scgFullBodyController.AiController>();
            if (ai != null)
            {
                ai.sightRange = Mathf.Max(ai.sightRange, 45f);
                ai.attackRange = Mathf.Max(ai.attackRange, 30f);
            }
            added++;
        }
        return added;
    }

    // ---------------- Hierarchy helpers ----------------
    static Transform FindOrCreateGroup(Scene scene, string name, int siblingIndex)
    {
        var g = scene.GetRootGameObjects().FirstOrDefault(r => r.name == name);
        if (g == null)
        {
            g = new GameObject(name);
            SceneManager.MoveGameObjectToScene(g, scene);
            g.transform.SetSiblingIndex(Mathf.Min(siblingIndex, scene.rootCount - 1));
        }
        return g.transform;
    }

    static void AddToGroup(Scene scene, Transform t, string groupName, int siblingIndex, string subGroup = null)
    {
        Transform g = FindOrCreateGroup(scene, groupName, siblingIndex);
        if (subGroup != null)
        {
            var sub = g.Find(subGroup);
            if (sub == null)
            {
                var s = new GameObject(subGroup);
                s.transform.SetParent(g, false);
                sub = s.transform;
            }
            g = sub;
        }
        t.SetParent(g, true);
    }

    // ---------------- NavMesh + build settings ----------------
    static void TryBakeNavMesh()
    {
        try
        {
#pragma warning disable CS0618
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
#pragma warning restore CS0618
        }
        catch (Exception e)
        {
            Debug.LogWarning("[NightStorm] NavMesh bake failed - bake manually via Window > AI > Navigation. " + e.Message);
        }
    }

    static void AddToBuildSettings(string path)
    {
        var list = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
        list.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = list.ToArray();
    }
}
