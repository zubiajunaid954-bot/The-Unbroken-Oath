using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using GAMECON.Weather;

/// <summary>
/// Replaces the simple rain in Assets/Scenes/SunTemple_Night.unity with a layered,
/// realistic rain: near streaks, a wide distant curtain, ground splashes (rain stops on
/// roofs/floors), and low ground mist. Everything follows the player so it rains everywhere.
/// </summary>
public static class RealisticRainBuilder
{
    const string NightScene = "Assets/Scenes/SunTemple_Night.unity";
    const string Folder = "Assets/Environment/Night";
    const float SpawnHeight = 14f;     // emitter height above the player
    const float CeilingHeight = 14f;

    [MenuItem("GAMECON/Upgrade to Realistic Rain (SunTemple_Night)")]
    public static void Build()
    {
        if (!File.Exists(NightScene))
        {
            EditorUtility.DisplayDialog("Error", "SunTemple_Night.unity not found. Run 'Build Night + Rain Scene' first.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.OpenScene(NightScene, OpenSceneMode.Single);

        var weather = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<StormController>(true)).FirstOrDefault();
        if (weather == null)
        {
            EditorUtility.DisplayDialog("Error", "WeatherSystem not found in SunTemple_Night.", "OK");
            return;
        }
        Transform root = weather.transform;

        // remove the old rain
        foreach (var old in root.Cast<Transform>().Where(c => c.name == "Rain" || c.name == "RainSystem").ToList())
            Object.DestroyImmediate(old.gameObject);

        Directory.CreateDirectory(Folder);
        Texture2D streak = MakeTexture("RainStreak", 16, 128, StreakPixel);
        Texture2D dot = MakeTexture("SoftDot", 64, 64, DotPixel);
        Material nearMat = MakeMat("Rain_Near", streak, new Color(0.85f, 0.9f, 1f, 1f));
        Material farMat = MakeMat("Rain_Far", streak, new Color(0.8f, 0.87f, 1f, 1f));
        Material splashMat = MakeMat("Rain_Splash", dot, new Color(0.85f, 0.9f, 1f, 1f));
        Material mistMat = MakeMat("Rain_Mist", dot, new Color(0.6f, 0.68f, 0.85f, 1f));

        var rain = new GameObject("RainSystem");
        rain.transform.SetParent(root, false);

        // Drops are NOT killed by colliders (invisible collision boxes made the rain vanish in patches).
        // Splashes are emitted on the floor under the player instead (StormController keeps them there).
        var near = BuildStreaks(rain.transform, "Rain_Near", nearMat,
            rate: 4200, box: 34f, sizeMin: 0.024f, sizeMax: 0.038f, speedMin: 20f, speedMax: 25f,
            alpha: 0.30f, velScale: 0.04f);
        var mid = BuildStreaks(rain.transform, "Rain_Mid", farMat,
            rate: 4500, box: 100f, sizeMin: 0.045f, sizeMax: 0.07f, speedMin: 20f, speedMax: 25f,
            alpha: 0.11f, velScale: 0.03f);

        var ground = new GameObject("Rain_Ground");
        ground.transform.SetParent(rain.transform, false);
        BuildSplash(ground.transform, splashMat);
        BuildMist(ground.transform, mistMat);

        weather.followOffset = new Vector3(0, SpawnHeight, 0);
        weather.groundFollower = ground.transform;
        weather.rainSystems = new[] { near, mid };
        var rainAudio = root.Find("RainSound");
        if (rainAudio != null) { weather.rainAudio = rainAudio.GetComponent<AudioSource>(); weather.rainAudioVolume = 0.55f; }

        RenderSettings.fogDensity = Mathf.Max(RenderSettings.fogDensity, 0.016f);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[RealisticRain] Upgraded rain in " + NightScene);
        EditorUtility.DisplayDialog("Realistic rain ready",
            "SunTemple_Night.unity now has fine dense rain all around the player, floor splashes and ground mist. Press Play to see it.", "OK");
    }

    // ---------- particle systems ----------
    static ParticleSystem BuildStreaks(Transform parent, string name, Material mat,
        float rate, float box, float sizeMin, float sizeMax, float speedMin, float speedMax, float alpha, float velScale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.Euler(87f, 12f, 0f); // down, slight wind slant
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = (SpawnHeight + 30f) / ((speedMin + speedMax) * 0.5f); // falls well below the player (slopes, stairs)
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.8f, 0.9f, alpha * 0.5f), new Color(0.85f, 0.9f, 1f, alpha));
        main.maxParticles = 30000;
        main.gravityModifier = 0f;

        var em = ps.emission; em.rateOverTime = rate;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = new Vector3(box, box, 0.1f);

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Stretch;
        rend.velocityScale = velScale;
        rend.lengthScale = 1f;
        rend.cameraVelocityScale = 0f;
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;
        ps.Play();
        return ps;
    }

    // Continuous splashes on the floor around the player (box lies flat, particles pop up).
    static ParticleSystem BuildSplash(Transform parent, Material mat)
    {
        var go = new GameObject("Rain_Splash");
        go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.26f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        main.startColor = new Color(0.8f, 0.86f, 0.95f, 0.35f);
        main.gravityModifier = 2.2f;
        main.maxParticles = 3000;

        var em = ps.emission; em.rateOverTime = 1100f;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = new Vector3(26f, 26f, 0.02f);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.5f));
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;
        ps.Play();
        return ps;
    }

    static void BuildMist(Transform parent, Material mat)
    {
        var go = new GameObject("Rain_GroundMist");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0, 0.5f, 0);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(5f, 9f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        main.startColor = new Color(0.5f, 0.56f, 0.7f, 0.05f);
        main.maxParticles = 140;

        var em = ps.emission; em.rateOverTime = 16f;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = new Vector3(50f, 1f, 50f);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode = ParticleSystemRenderMode.Billboard;
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;
        ps.Play();
    }

    // ---------- textures & materials ----------
    delegate float AlphaFn(float u, float v);

    // soft streak: bright thin core, fading toward both ends
    static float StreakPixel(float u, float v)
    {
        float x = (u - 0.5f) * 2f;
        float across = Mathf.Exp(-x * x * 7f);
        float along = Mathf.Pow(Mathf.Sin(Mathf.Clamp01(v) * Mathf.PI), 0.8f);
        return across * along;
    }

    static float DotPixel(float u, float v)
    {
        float dx = (u - 0.5f) * 2f, dy = (v - 0.5f) * 2f;
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        return Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d);
    }

    static Texture2D MakeTexture(string name, int w, int h, AlphaFn fn)
    {
        string path = Folder + "/" + name + ".png";
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, new Color(1, 1, 1, fn((x + 0.5f) / w, (y + 0.5f) / h)));
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Default;
        imp.alphaSource = TextureImporterAlphaSource.FromInput;
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = false;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material MakeMat(string name, Texture2D tex, Color tint)
    {
        string path = Folder + "/" + name + ".mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
        m.mainTexture = tex;
        m.SetColor("_TintColor", new Color(0.4f, 0.42f, 0.46f, 0.5f)); // neutral grey: no neon/bloom glow
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
