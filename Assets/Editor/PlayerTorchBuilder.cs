using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using GAMECON.PlayerTools;
using scgFullBodyController;

/// <summary>
/// Gives the player a blue-white torch (toggle with F) mounted at the left of the view while the
/// gun stays in the right hand, and removes the torches from the enemy soldiers.
/// </summary>
public static class PlayerTorchBuilder
{
    const string NightScene = "Assets/Scenes/SunTemple_Night.unity";
    const string Folder = "Assets/Environment/Night";
    const bool UseProp = false;   // the Sun Temple prop is a fire bowl, not a hand torch: light only
    static readonly Color BlueLight = new Color(0.62f, 0.78f, 1f);

    [MenuItem("GAMECON/Give Player a Torch (F key) + Remove Enemy Torches")]
    public static void Build()
    {
        if (!File.Exists(NightScene)) { EditorUtility.DisplayDialog("Error", "SunTemple_Night.unity not found.", "OK"); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.OpenScene(NightScene, OpenSceneMode.Single);

        // 1. enemies lose their torches
        int removed = 0;
        foreach (var ai in Object.FindObjectsOfType<AiController>(true))
            foreach (var t in ai.GetComponentsInChildren<Transform>(true).Where(x => x != null && x.name == "Torch").ToList())
            { if (t != null) { Object.DestroyImmediate(t.gameObject); removed++; } }

        // 2. find the player camera
        // the gameplay camera: "Main Camera" somewhere under the FullBodyController (not the disabled Sun Temple demo player)
        Camera cam = Object.FindObjectsOfType<Camera>(true)
            .Where(c => c.gameObject.scene == scene && c.name != "sniperCamera")
            .FirstOrDefault(c => c.GetComponentInParent<ThirdPersonUserControl>(true) != null && c.name == "Main Camera");
        if (cam == null)
            cam = Object.FindObjectsOfType<Camera>(true)
                .FirstOrDefault(c => c.gameObject.scene == scene && c.name != "sniperCamera" && c.GetComponentInParent<ThirdPersonUserControl>(true) != null);
        if (cam == null) { EditorUtility.DisplayDialog("Error", "Player camera not found (no camera under the FullBodyController).", "OK"); return; }

        var old = cam.transform.Find("PlayerTorch");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        Material flameMat = MakeFlameMat();

        // 3. build the torch upright in world space (so bounds are axis aligned), then pose it on the camera
        var holder = new GameObject("PlayerTorch");
        holder.transform.SetParent(cam.transform, true);
        holder.transform.position = cam.transform.position;
        holder.transform.rotation = Quaternion.identity;

        Vector3 flameWorld = holder.transform.position + Vector3.up * 0.1f;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Sun_Temple/Prefabs/Props/Prop_FireTorch_A.prefab");
        if (UseProp && prefab != null)
        {
            var prop = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            prop.name = "Prop_FireTorch_A";
            prop.transform.SetParent(holder.transform, false);
            foreach (var c in prop.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var r in prop.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(prop, 0);

            Bounds b = Bnds(prop);
            if (b.size.y < Mathf.Max(b.size.x, b.size.z) * 0.9f)
            {
                prop.transform.localRotation = b.size.x >= b.size.z ? Quaternion.Euler(0, 0, 90) : Quaternion.Euler(90, 0, 0);
                b = Bnds(prop);
            }
            prop.transform.localScale = Vector3.one * (0.42f / Mathf.Max(0.001f, b.size.y));
            b = Bnds(prop);
            prop.transform.position += new Vector3(holder.transform.position.x - b.center.x, holder.transform.position.y - b.min.y, holder.transform.position.z - b.center.z);
            b = Bnds(prop);
            flameWorld = new Vector3(b.center.x, b.max.y, b.center.z);
        }
        

        ParticleSystem flame = UseProp ? MakeParticles(holder.transform, "Flame", flameWorld, flameMat, false) : null;
        ParticleSystem halo = UseProp ? MakeParticles(holder.transform, "Halo", flameWorld + Vector3.up * 0.03f, flameMat, true) : null;

        var glowGo = new GameObject("TorchGlow");
        glowGo.transform.SetParent(holder.transform, true);
        glowGo.transform.position = flameWorld + Vector3.up * 0.08f;
        var glow = glowGo.AddComponent<Light>();
        glow.type = LightType.Point; glow.color = BlueLight; glow.range = 9f; glow.intensity = 1.4f;
        glow.shadows = LightShadows.None; glow.lightmapBakeType = LightmapBakeType.Realtime;

        var beamGo = new GameObject("TorchBeam");
        beamGo.transform.SetParent(holder.transform, true);
        var beam = beamGo.AddComponent<Light>();
        beam.type = LightType.Spot; beam.color = BlueLight; beam.range = 38f; beam.spotAngle = 62f; beam.innerSpotAngle = 25f;
        beam.intensity = 4.5f; beam.shadows = LightShadows.Soft; beam.shadowStrength = 0.85f; beam.lightmapBakeType = LightmapBakeType.Realtime;

        var audioGo = new GameObject("TorchClick");
        audioGo.transform.SetParent(holder.transform, false);
        var src = audioGo.AddComponent<AudioSource>();
        src.playOnAwake = false; src.spatialBlend = 0f;

        // pose the torch: lower-left of the view, tilted forward
        holder.transform.localPosition = new Vector3(-0.34f, -0.30f, 0.6f);
        holder.transform.localRotation = Quaternion.Euler(38f, 10f, 0f);
        // the beam must point along the camera view regardless of how the torch is tilted
        beamGo.transform.position = cam.transform.TransformPoint(new Vector3(-0.1f, -0.08f, 0.15f));
        beamGo.transform.rotation = cam.transform.rotation;

        var pt = holder.AddComponent<PlayerTorch>();
        pt.toggleKey = KeyCode.F;
        pt.startOn = true;
        pt.beam = beam; pt.glow = glow; pt.flame = flame; pt.halo = halo;
        pt.clickSource = src;
        pt.clickClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Torch_Click.wav");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PlayerTorch] Added torch to " + cam.name + "; removed " + removed + " enemy torches.");
        EditorUtility.DisplayDialog("Player torch ready",
            "Press F in game to switch the blue torch on/off.\nThe gun stays in your hand; the torch sits at the left of your view.\nRemoved " + removed + " enemy torches.", "OK");
    }

    static ParticleSystem MakeParticles(Transform parent, string name, Vector3 worldPos, Material mat, bool halo)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.position = worldPos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        if (halo)
        {
            main.startLifetime = 0.6f; main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.5f);
            main.startColor = new Color(0.35f, 0.6f, 1f, 0.25f);
            main.maxParticles = 6;
            var e = ps.emission; e.rateOverTime = 10f;
            var s0 = ps.shape; s0.enabled = false;
        }
        else
        {
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.38f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.12f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.35f, 0.65f, 1f, 0.9f), new Color(0.7f, 0.9f, 1f, 0.9f));
            main.maxParticles = 40;
            var e = ps.emission; e.rateOverTime = 45f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 6f; sh.radius = 0.01f; sh.rotation = new Vector3(-90, 0, 0);
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.85f, 0.95f, 1f), 0f), new GradientColorKey(new Color(0.15f, 0.4f, 1f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }
        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;
        ps.Play();
        return ps;
    }

    static Bounds Bnds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Material MakeFlameMat()
    {
        string path = Folder + "/PlayerTorch_Flame.mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
        m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/SoftDot.png");
        m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
