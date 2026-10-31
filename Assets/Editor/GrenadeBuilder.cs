using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using GAMECON.Combat;
using GAMECON.FX;

/// <summary>
/// Builds the grenade feature: grenade + explosion prefabs (Resources/FX), the little grenade box on the HUD
/// (count, G key, cook bar) and the GrenadeThrower in SunTemple_Night. Run as part of "Build Minimap + Rescue Mission",
/// or on its own with GAMECON > Build Grenades.
/// </summary>
public static class GrenadeBuilder
{
    const string NightScene = "Assets/Scenes/SunTemple_Night.unity";
    const string Folder = "Assets/Environment/Grenade";
    const string FxResources = "Assets/Resources/FX";

    [MenuItem("GAMECON/Build Grenades (G key)")]
    public static void Build()
    {
        if (!File.Exists(NightScene)) { EditorUtility.DisplayDialog("Error", "SunTemple_Night.unity not found.", "OK"); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.OpenScene(NightScene, OpenSceneMode.Single);
        bool ok = Apply(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorUtility.DisplayDialog("Grenades",
            ok ? "Grenades are ready.\n\nHold G to pull the pin (the rifle is lowered), release to throw.\nThe box at the bottom right shows how many you have."
               : "The prefabs were built, but MissionCanvas was not found. Run 'Build Minimap + Rescue Mission' first.", "OK");
    }

    public static bool Apply(Scene scene)
    {
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(FxResources);
        AssetDatabase.Refresh();       // make sure the new sound files are imported

        // ---------- textures ----------
        Texture2D dot = TexOrMake("Assets/Environment/Blood/BloodDot.png", "SoftDot", 64, (u, v) => { float d = Mathf.Sqrt((u - .5f) * (u - .5f) + (v - .5f) * (v - .5f)) * 2f; float a = Mathf.Clamp01(1f - d); return new Color(1, 1, 1, a * a); });
        Texture2D streak = TexOrMake("Assets/Environment/Blood/BloodStreak.png", "Streak", 16, (u, v) => { float x = (u - .5f) * 2f; return new Color(1, 1, 1, Mathf.Exp(-x * x * 6f) * Mathf.Pow(Mathf.Sin(Mathf.Clamp01(v) * Mathf.PI), .7f)); }, 64);
        Texture2D ringTex = TexOrMake("Assets/UI/Minimap/Icon_Ring.png", "Ring", 128, (u, v) => { float d = Mathf.Sqrt((u - .5f) * (u - .5f) + (v - .5f) * (v - .5f)) * 2f; float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.8f) / 0.18f); return new Color(1, 1, 1, a); });
        var scorch = new[] { MakeScorch("Scorch_0", 11), MakeScorch("Scorch_1", 23) };
        Sprite icon = MakeIcon();

        // ---------- materials ----------
        Material fire = Mat("Explosion_Fire", "Legacy Shaders/Particles/Additive", dot, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        Material smoke = Mat("Explosion_Smoke", "Legacy Shaders/Particles/Alpha Blended", dot, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        Material spark = Mat("Explosion_Spark", "Legacy Shaders/Particles/Additive", streak, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        Material debris = Mat("Explosion_Debris", "Legacy Shaders/Particles/Alpha Blended", dot, new Color(0.3f, 0.22f, 0.16f, 0.5f));
        Material ringMat = Mat("Explosion_Ring", "Legacy Shaders/Particles/Additive", ringTex, new Color(0.5f, 0.42f, 0.32f, 0.5f));
        Material scorchMat = Mat("Explosion_Scorch", "Legacy Shaders/Particles/Alpha Blended", null, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        Material arcMat = Mat("Grenade_Arc", "Legacy Shaders/Particles/Additive", null, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        var bodyMat = new Material(Shader.Find("Standard")) { color = new Color(0.22f, 0.29f, 0.15f) }; bodyMat.SetFloat("_Glossiness", 0.35f);
        var metalMat = new Material(Shader.Find("Standard")) { color = new Color(0.32f, 0.32f, 0.34f) }; metalMat.SetFloat("_Metallic", 0.8f); metalMat.SetFloat("_Glossiness", 0.55f);
        AssetDatabase.DeleteAsset(Folder + "/Grenade_Body.mat"); AssetDatabase.CreateAsset(bodyMat, Folder + "/Grenade_Body.mat");
        AssetDatabase.DeleteAsset(Folder + "/Grenade_Metal.mat"); AssetDatabase.CreateAsset(metalMat, Folder + "/Grenade_Metal.mat");

        // ---------- audio ----------
        AudioClip Clip(string n) { return AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Resources/Audio/Grenade/" + n + ".wav"); }

        // ---------- prefabs ----------
        GameObject explosion = BuildExplosion(fire, smoke, spark, debris, ringMat, scorchMat, scorch, Clip("Grenade_Explosion"));
        GameObject grenade = BuildGrenade(bodyMat, metalMat, explosion, Clip("Grenade_Bounce_0"), Clip("Grenade_Beep"));
        AssetDatabase.SaveAssets();

        // ---------- HUD + thrower ----------
        var canvas = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).FirstOrDefault(c => c.name == "MissionCanvas");
        if (canvas == null) return false;
        BuildHud(canvas.transform, icon, grenade, Clip("Grenade_Pin"), Clip("Grenade_Throw"), Clip("Grenade_Ringing"), arcMat, scene);
        return true;
    }

    // ================================================================== prefabs
    static GameObject BuildExplosion(Material fire, Material smoke, Material spark, Material debris, Material ringMat, Material scorchMat, Texture2D[] scorch, AudioClip sound)
    {
        var root = new GameObject("GrenadeExplosion");
        var fx = root.AddComponent<ExplosionFX>();
        fx.sound = sound; fx.scorchMaterial = scorchMat; fx.scorchTextures = scorch; fx.pushForce = 26f; fx.pushRadius = 13f;

        var lg = new GameObject("Flash"); lg.transform.SetParent(root.transform, false);
        lg.transform.localPosition = Vector3.up * 0.8f;
        var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.68f, 0.32f); l.range = 24f; l.intensity = 8f; l.shadows = LightShadows.None;
        fx.flash = l;

        // fireball
        var fb = Sys(root.transform, "Fireball", fire, 0.2f);
        var m = fb.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.95f); m.startSpeed = new ParticleSystem.MinMaxCurve(2f, 8f);
        m.startSize = new ParticleSystem.MinMaxCurve(1.0f, 2.4f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.45f, 0.9f), new Color(1f, 0.45f, 0.1f, 0.9f)); m.gravityModifier = -0.15f;
        Burst(fb, 20, 26); Sphere(fb, 0.45f);
        var sol = fb.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.55f, 1f, 1.7f));
        Fade(fb, new Color(1f, 0.9f, 0.5f), new Color(0.8f, 0.2f, 0.05f));

        // very short white-hot core flash
        var core = Sys(root.transform, "Core", fire, 0.1f);
        m = core.main; m.startLifetime = 0.14f; m.startSpeed = 0f; m.startSize = 7f; m.startColor = new Color(1f, 0.95f, 0.8f, 1f);
        Burst(core, 1, 1);
        var cs = core.shape; cs.enabled = false;

        // smoke column
        var sm = Sys(root.transform, "Smoke", smoke, 0.3f);
        m = sm.main; m.startLifetime = new ParticleSystem.MinMaxCurve(3.4f, 4.6f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 3.5f);
        m.startSize = new ParticleSystem.MinMaxCurve(1.6f, 3.2f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.10f, 0.09f, 0.085f, 0.7f), new Color(0.22f, 0.2f, 0.19f, 0.6f)); m.gravityModifier = -0.04f;
        Burst(sm, 22, 28); Sphere(sm, 0.6f);
        sol = sm.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.1f));
        var vel = sm.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.y = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
        Fade(sm, Color.white, new Color(0.7f, 0.7f, 0.7f), 0.35f);

        // glowing embers and small flames that linger on the ground and slowly die out
        var em2 = Sys(root.transform, "Embers", fire, 0.3f);
        m = em2.main; m.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 4.4f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.4f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.5f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f); m.gravityModifier = -0.06f;
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.15f, 0.8f), new Color(1f, 0.3f, 0.05f, 0.8f));
        Burst(em2, 12, 18); Sphere(em2, 1.8f);
        sol = em2.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.25f));
        Fade(em2, new Color(1f, 0.8f, 0.4f), new Color(0.6f, 0.1f, 0.02f), 0.1f);

        // sparks
        var sp = Sys(root.transform, "Sparks", spark, 0.2f);
        m = sp.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f); m.startSpeed = new ParticleSystem.MinMaxCurve(10f, 26f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f); m.gravityModifier = 1.4f;
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.5f, 1f), new Color(1f, 0.5f, 0.15f, 1f));
        Burst(sp, 40, 55); Sphere(sp, 0.2f);
        var rs = sp.GetComponent<ParticleSystemRenderer>(); rs.renderMode = ParticleSystemRenderMode.Stretch; rs.velocityScale = 0.06f; rs.lengthScale = 1.6f;
        Fade(sp, Color.white, new Color(1f, 0.4f, 0.1f));

        // debris thrown up
        var db = Sys(root.transform, "Debris", debris, 0.2f);
        m = db.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.7f); m.startSpeed = new ParticleSystem.MinMaxCurve(6f, 16f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f); m.gravityModifier = 2.6f; m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.22f, 0.16f, 0.1f, 1f), new Color(0.4f, 0.32f, 0.22f, 1f));
        Burst(db, 26, 34);
        var dsh = db.shape; dsh.shapeType = ParticleSystemShapeType.Cone; dsh.angle = 55f; dsh.radius = 0.3f; dsh.rotation = new Vector3(-90f, 0f, 0f);
        Fade(db, Color.white, Color.white, 0.5f);

        // flat shock wave running along the ground
        var sw = Sys(root.transform, "Shockwave", ringMat, 0.1f);
        m = sw.main; m.startLifetime = 0.45f; m.startSpeed = 0f; m.startSize = 20f; m.startColor = new Color(1f, 0.85f, 0.65f, 0.9f);
        Burst(sw, 1, 1);
        var sws = sw.shape; sws.enabled = false;
        var swr = sw.GetComponent<ParticleSystemRenderer>(); swr.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        sol = sw.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.08f, 1f, 1f));
        Fade(sw, Color.white, Color.white, 0.15f);

        return Save(root, FxResources + "/GrenadeExplosion.prefab");
    }

    static GameObject BuildGrenade(Material body, Material metal, GameObject explosion, AudioClip bounce, AudioClip beep)
    {
        var root = new GameObject("Grenade");
        Part(root.transform, PrimitiveType.Sphere, "Body", Vector3.zero, new Vector3(0.085f, 0.11f, 0.085f), body);
        Part(root.transform, PrimitiveType.Cylinder, "Fuze", new Vector3(0, 0.066f, 0), new Vector3(0.034f, 0.018f, 0.034f), metal);
        Part(root.transform, PrimitiveType.Cube, "Lever", new Vector3(0.03f, 0.04f, 0), new Vector3(0.008f, 0.075f, 0.022f), metal);
        Part(root.transform, PrimitiveType.Cylinder, "Ring", new Vector3(-0.022f, 0.07f, 0), new Vector3(0.026f, 0.002f, 0.026f), metal);

        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 0.45f; rb.drag = 0.05f; rb.angularDrag = 0.6f; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var sc = root.AddComponent<SphereCollider>(); sc.radius = 0.058f;
        string pmPath = Folder + "/Grenade.physicMaterial";
        AssetDatabase.DeleteAsset(pmPath);
        var pm = new PhysicMaterial("Grenade") { bounciness = 0.38f, dynamicFriction = 0.55f, staticFriction = 0.6f, bounceCombine = PhysicMaterialCombine.Maximum, frictionCombine = PhysicMaterialCombine.Average };
        AssetDatabase.CreateAsset(pm, pmPath);
        sc.sharedMaterial = pm;

        var src = root.AddComponent<AudioSource>(); src.spatialBlend = 1f; src.minDistance = 2f; src.maxDistance = 45f; src.playOnAwake = false;
        var g = root.AddComponent<Grenade>();
        g.explosionPrefab = explosion; g.bounceClip = bounce; g.beepClip = beep;
        g.lethalRadius = 6.5f; g.blastRadius = 10f;
        return Save(root, FxResources + "/Grenade.prefab");
    }

    static GameObject Save(GameObject go, string path)
    {
        AssetDatabase.DeleteAsset(path);
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    static void Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos; go.transform.localScale = scale;
        var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off;
    }

    // ---------- particle helpers ----------
    static ParticleSystem Sys(Transform parent, string name, Material mat, float duration)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = duration; main.loop = false; main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 200;
        var em = ps.emission; em.rateOverTime = 0f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return ps;
    }

    static void Burst(ParticleSystem ps, short min, short max)
    {
        var e = ps.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, min, max) });
    }

    static void Sphere(ParticleSystem ps, float radius)
    {
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = radius;
    }

    static void Fade(ParticleSystem ps, Color from, Color to, float fadeInEnd = 0.05f)
    {
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeInEnd), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
    }

    // ================================================================== HUD
    static void BuildHud(Transform canvas, Sprite icon, GameObject grenadePrefab, AudioClip pin, AudioClip thr, AudioClip ringing, Material arcMat, Scene scene)
    {
        foreach (string n in new[] { "GrenadeBox", "GrenadeFlash", "WeaponBar" })
        {
            var old = canvas.Find(n);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Sprite rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Environment/Blood/Icon_RoundedRect.png");
        Sprite square = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Minimap/Icon_Square.png");

        var box = NewRect("GrenadeBox", canvas);
        box.anchorMin = box.anchorMax = box.pivot = new Vector2(1f, 0f);
        box.anchoredPosition = new Vector2(-40f, 175f);
        box.sizeDelta = new Vector2(190f, 84f);

        var frame = NewImage("Frame", box, rounded != null ? rounded : square, new Color(0f, 0f, 0f, 0.6f));
        Stretch(frame.rectTransform); if (rounded != null) frame.type = Image.Type.Sliced;

        var ic = NewImage("Icon", box, icon, new Color(0.62f, 0.78f, 0.4f, 1f));
        ic.rectTransform.anchorMin = ic.rectTransform.anchorMax = ic.rectTransform.pivot = new Vector2(0f, 0.5f);
        ic.rectTransform.anchoredPosition = new Vector2(12f, 0f); ic.rectTransform.sizeDelta = new Vector2(58f, 58f);

        var count = NewText("Count", box, font, "x 4", 38, Color.white, TextAnchor.MiddleRight);
        count.rectTransform.anchorMin = count.rectTransform.anchorMax = count.rectTransform.pivot = new Vector2(1f, 0.5f);
        count.rectTransform.anchoredPosition = new Vector2(-14f, -2f); count.rectTransform.sizeDelta = new Vector2(100f, 50f);

        var key = NewText("Key", box, font, "G", 20, new Color(1f, 0.85f, 0.3f), TextAnchor.MiddleCenter);
        key.rectTransform.anchorMin = key.rectTransform.anchorMax = key.rectTransform.pivot = new Vector2(1f, 1f);
        key.rectTransform.anchoredPosition = new Vector2(-12f, -4f); key.rectTransform.sizeDelta = new Vector2(24f, 24f);

        // cook bar: fills while the pin is pulled
        var barBg = NewImage("CookBarBack", box, square, new Color(0f, 0f, 0f, 0.55f));
        barBg.rectTransform.anchorMin = new Vector2(0f, 0f); barBg.rectTransform.anchorMax = new Vector2(1f, 0f);
        barBg.rectTransform.pivot = new Vector2(0.5f, 1f); barBg.rectTransform.anchoredPosition = new Vector2(0f, -4f); barBg.rectTransform.sizeDelta = new Vector2(-8f, 7f);
        var bar = NewImage("CookBar", barBg.transform, square, new Color(1f, 0.35f, 0.1f, 1f));
        Stretch(bar.rectTransform);
        bar.type = Image.Type.Filled; bar.fillMethod = Image.FillMethod.Horizontal; bar.fillOrigin = 0; bar.fillAmount = 0f;

        BuildWeaponBar(canvas, rounded != null ? rounded : square, square, font);

        // white flash for nearby explosions (over everything)
        var flash = NewImage("GrenadeFlash", canvas, square, new Color(1f, 1f, 1f, 0f));
        Stretch(flash.rectTransform);

        // the thrower
        Transform mission = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "====== MISSION ======")?.transform;
        if (mission == null) { var mg = new GameObject("====== MISSION ======"); SceneManager.MoveGameObjectToScene(mg, scene); mission = mg.transform; }
        var oldSys = mission.Find("GrenadeSystem");
        if (oldSys != null) Object.DestroyImmediate(oldSys.gameObject);
        var go = new GameObject("GrenadeSystem");
        go.transform.SetParent(mission, false);
        var gt = go.AddComponent<GrenadeThrower>();
        gt.grenades = 4; gt.maxGrenades = 6;
        gt.grenadePrefab = grenadePrefab; gt.pinClip = pin; gt.throwClip = thr; gt.ringingClip = ringing; gt.arcMaterial = arcMat;
        gt.countText = count; gt.boxFrame = frame; gt.cookBar = bar; gt.flash = flash;
    }

    // ================================================================== weapon bar
    static void BuildWeaponBar(Transform canvas, Sprite frameSprite, Sprite square, Font font)
    {
        var bar = NewRect("WeaponBar", canvas);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(1f, 0f);
        bar.anchoredPosition = new Vector2(-40f, 270f);
        const float w = 112f, h = 74f, gap = 8f;
        bar.sizeDelta = new Vector2(w * 3 + gap * 2, h);

        var hud = bar.gameObject.AddComponent<WeaponHud>();
        hud.rifleIcon = WeaponIcon("Icon_Weapon_Rifle", 0);
        hud.pistolIcon = WeaponIcon("Icon_Weapon_Pistol", 1);
        hud.sniperIcon = WeaponIcon("Icon_Weapon_Sniper", 2);
        hud.slots = new WeaponHud.Slot[3];

        for (int i = 0; i < 3; i++)
        {
            var root = NewRect("Slot" + (i + 1), bar);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0f, 0.5f);
            root.anchoredPosition = new Vector2(i * (w + gap), 0f); root.sizeDelta = new Vector2(w, h);

            var frame = NewImage("Frame", root, frameSprite, new Color(0f, 0f, 0f, 0.55f));
            Stretch(frame.rectTransform); if (frameSprite != square) frame.type = Image.Type.Sliced;

            var icon = NewImage("Icon", root, null, Color.white);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            icon.rectTransform.anchoredPosition = new Vector2(2f, 8f); icon.rectTransform.sizeDelta = new Vector2(92f, 38f);
            icon.preserveAspect = true;

            var key = NewText("Key", root, font, (i + 1).ToString(), 20, new Color(1f, 0.85f, 0.3f), TextAnchor.MiddleCenter);
            key.rectTransform.anchorMin = key.rectTransform.anchorMax = key.rectTransform.pivot = new Vector2(0f, 1f);
            key.rectTransform.anchoredPosition = new Vector2(8f, -4f); key.rectTransform.sizeDelta = new Vector2(20f, 22f);

            var label = NewText("Label", root, font, "", 15, Color.white, TextAnchor.MiddleLeft);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(0f, 0f);
            label.rectTransform.anchoredPosition = new Vector2(10f, 5f); label.rectTransform.sizeDelta = new Vector2(60f, 20f);

            var ammo = NewText("Ammo", root, font, "", 16, new Color(0.85f, 0.95f, 1f), TextAnchor.MiddleRight);
            ammo.rectTransform.anchorMin = ammo.rectTransform.anchorMax = ammo.rectTransform.pivot = new Vector2(1f, 0f);
            ammo.rectTransform.anchoredPosition = new Vector2(-8f, 5f); ammo.rectTransform.sizeDelta = new Vector2(70f, 20f);

            hud.slots[i] = new WeaponHud.Slot { root = root, frame = frame, icon = icon, key = key, label = label, ammo = ammo };
        }
    }

    // simple weapon silhouettes (white, with a dark outline) drawn from rectangles
    static Sprite WeaponIcon(string name, int kind)
    {
        string path = "Assets/UI/" + name + ".png";
        Directory.CreateDirectory("Assets/UI");
        const int W = 128, H = 52;
        var rects = new System.Collections.Generic.List<Rect>();      // in 0..1 space (u, v, w, h)
        if (kind == 0)        // assault rifle: long barrel, receiver, stock, magazine, sight
        {
            rects.Add(new Rect(0.04f, 0.50f, 0.34f, 0.10f));   // barrel
            rects.Add(new Rect(0.30f, 0.38f, 0.40f, 0.26f));   // receiver / handguard
            rects.Add(new Rect(0.68f, 0.40f, 0.26f, 0.20f));   // stock
            rects.Add(new Rect(0.50f, 0.10f, 0.09f, 0.30f));   // magazine
            rects.Add(new Rect(0.56f, 0.20f, 0.08f, 0.20f));   // grip
            rects.Add(new Rect(0.42f, 0.64f, 0.10f, 0.10f));   // front/rear sight
        }
        else if (kind == 1)   // pistol
        {
            rects.Add(new Rect(0.18f, 0.52f, 0.62f, 0.22f));   // slide
            rects.Add(new Rect(0.56f, 0.12f, 0.20f, 0.44f));   // grip
            rects.Add(new Rect(0.40f, 0.34f, 0.16f, 0.20f));   // trigger guard area
        }
        else                  // sniper: very long barrel + scope
        {
            rects.Add(new Rect(0.02f, 0.46f, 0.50f, 0.07f));   // barrel
            rects.Add(new Rect(0.46f, 0.38f, 0.30f, 0.22f));   // receiver
            rects.Add(new Rect(0.74f, 0.34f, 0.24f, 0.24f));   // stock
            rects.Add(new Rect(0.40f, 0.64f, 0.30f, 0.12f));   // scope
            rects.Add(new Rect(0.46f, 0.30f, 0.07f, 0.14f));   // grip
            rects.Add(new Rect(0.18f, 0.30f, 0.014f, 0.17f));  // bipod leg
        }

        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + .5f) / W, v = (y + .5f) / H;
                bool fill = false, outline = false;
                foreach (var r in rects)
                {
                    if (u >= r.x && u <= r.x + r.width && v >= r.y && v <= r.y + r.height) fill = true;
                    float pu = 2.2f / W, pv = 2.2f / H;
                    if (u >= r.x - pu && u <= r.x + r.width + pu && v >= r.y - pv && v <= r.y + r.height + pv) outline = true;
                }
                tex.SetPixel(x, y, fill ? Color.white : (outline ? new Color(0, 0, 0, 0.9f) : Color.clear));
            }
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
        imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.filterMode = FilterMode.Bilinear;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ================================================================== small helpers
    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var rt = NewRect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite; img.color = color; img.raycastTarget = false;
        return img;
    }

    static Text NewText(string name, Transform parent, Font font, string text, int size, Color color, TextAnchor anchor)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = font; t.text = text; t.fontSize = size; t.color = color; t.alignment = anchor; t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        var o = rt.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, 0.9f); o.effectDistance = new Vector2(1.5f, -1.5f);
        return t;
    }

    static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }

    delegate Color Px(float u, float v);

    static Texture2D TexOrMake(string existing, string name, int size, Px fn, int h = 0)
    {
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(existing);
        if (t != null) return t;
        return MakeTexture(name, size, h > 0 ? h : size, fn, false);
    }

    static Texture2D MakeTexture(string name, int w, int h, Px fn, bool sprite)
    {
        string path = Folder + "/" + name + ".png";
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) tex.SetPixel(x, y, fn((x + .5f) / w, (y + .5f) / h));
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
        imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.wrapMode = TextureWrapMode.Clamp;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Texture2D MakeScorch(string name, int seed)
    {
        var rnd = new System.Random(seed);
        float[] amp = new float[6], ph = new float[6];
        for (int i = 0; i < 6; i++) { amp[i] = (float)(rnd.NextDouble() * 0.07 / (1 + i * 0.5)); ph[i] = (float)(rnd.NextDouble() * 6.283); }
        return MakeTexture(name, 256, 256, (u, v) =>
        {
            float dx = u - .5f, dy = v - .5f, d = Mathf.Sqrt(dx * dx + dy * dy), th = Mathf.Atan2(dy, dx);
            float R = 0.34f; for (int i = 0; i < 6; i++) R += amp[i] * Mathf.Cos(th * (i + 2) + ph[i]);
            float a = Mathf.Clamp01((R - d) / 0.16f);                       // soft burnt edge
            float core = Mathf.Clamp01(1f - d / 0.22f);
            float g = Mathf.Lerp(0.05f, 0.015f, core);
            return new Color(g, g, g, a * Mathf.Lerp(0.55f, 0.9f, core));
        }, false);
    }

    static Sprite MakeIcon()
    {
        const string path = "Assets/UI/Icon_Grenade.png";
        Directory.CreateDirectory("Assets/UI");
        var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float u = (x + .5f) / 64f, v = (y + .5f) / 64f;
                // body (ellipse), fuze (small rectangle on top), lever (thin bar on the right side)
                float ex = (u - .46f) / .27f, ey = (v - .42f) / .33f;
                bool body = ex * ex + ey * ey <= 1f;
                bool bodyOut = (ex * ex + ey * ey) <= 1.25f;
                bool fuze = u > .40f && u < .55f && v > .72f && v < .84f;
                bool lever = u > .66f && u < .74f && v > .36f && v < .88f;
                bool ring = (u - .30f) * (u - .30f) + (v - .84f) * (v - .84f) <= 0.012f && (u - .30f) * (u - .30f) + (v - .84f) * (v - .84f) >= 0.004f;
                Color c = Color.clear;
                if (bodyOut || fuze || lever || ring) c = new Color(0, 0, 0, 0.95f);
                if (body || fuze || lever || ring)
                {
                    float shade = body ? Mathf.Clamp01(0.75f + 0.25f * (-ex * 0.6f + ey * 0.4f)) : 0.9f;
                    c = new Color(shade, shade, shade, 1f);
                    // a few grooves on the body
                    if (body && (Mathf.Abs(Mathf.Repeat(v * 9f, 1f) - 0.5f) < 0.06f || Mathf.Abs(Mathf.Repeat(u * 9f, 1f) - 0.5f) < 0.06f)) c *= 0.7f;
                    c.a = 1f;
                }
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
        imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.filterMode = FilterMode.Bilinear;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static Material Mat(string name, string shader, Texture2D tex, Color tint)
    {
        string path = Folder + "/" + name + ".mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(Shader.Find(shader));
        m.SetColor("_TintColor", tint);
        if (tex != null) m.mainTexture = tex;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
