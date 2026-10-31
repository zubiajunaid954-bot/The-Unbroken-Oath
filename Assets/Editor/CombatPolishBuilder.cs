using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using GAMECON.FX;
using GAMECON.Mission;
using GAMECON.PlayerTools;

/// <summary>
/// Builds the combat polish used by SunTemple_Night:
///  - realistic blood assets (spray + mist + screen/world splatters + pools), stored in Resources/FX
///  - the player life bar, red damage vignette, on-screen blood splatters and the "MISSION FAILED" panel
/// Everything is added to the existing MissionCanvas; nothing else in the scene is touched.
/// </summary>
public static class CombatPolishBuilder
{
    const string NightScene = "Assets/Scenes/SunTemple_Night.unity";
    const string BloodFolder = "Assets/Environment/Blood";
    const string FxResources = "Assets/Resources/FX";

    [MenuItem("GAMECON/Build Combat Polish (blood, life bar, fail screen)")]
    public static void Build()
    {
        if (!File.Exists(NightScene)) { EditorUtility.DisplayDialog("Error", "SunTemple_Night.unity not found.", "OK"); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.OpenScene(NightScene, OpenSceneMode.Single);
        bool ok = Apply(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorUtility.DisplayDialog("Combat polish",
            ok ? "Blood effects, the player life bar, damage flash and the 'Mission failed' screen are ready.\n\nPress Play."
               : "Blood assets were built, but MissionCanvas was not found. Run 'Build Minimap + Rescue Mission' first, then this again.", "OK");
    }

    /// <summary>Builds the assets and (re)creates the UI in the scene. Returns false when there is no MissionCanvas yet.</summary>
    public static bool Apply(Scene scene)
    {
        Directory.CreateDirectory(BloodFolder);
        Directory.CreateDirectory(FxResources);

        // ---------- textures ----------
        var splatSprites = new List<Sprite>();
        for (int i = 0; i < 4; i++) splatSprites.Add(MakeTexture("BloodSplat_" + i, 256, null, 100 + i * 17, true));
        var splatTex = splatSprites.Select(s => s.texture).ToArray();
        var poolSprites = new List<Sprite>();
        for (int i = 0; i < 2; i++) poolSprites.Add(MakeTexture("BloodPool_" + i, 256, null, 300 + i * 29, false, pool: true));
        var poolTex = poolSprites.Select(s => s.texture).ToArray();
        Sprite dot = MakeTexture("BloodDot", 64, null, 7, false, dot: true);
        Sprite streak = MakeTexture("BloodStreak", 16, null, 9, false, streak: true);
        Sprite vignette = MakeTexture("Vignette_Red", 256, null, 5, false, vignette: true);

        // ---------- materials ----------
        Material decalMat = MakeMat("BloodDecal", new Color(0.5f, 0.5f, 0.5f, 0.5f), null);
        Material dropMat = MakeMat("BloodDrop", new Color(0.42f, 0.04f, 0.04f, 0.5f), streak.texture);
        Material chunkMat = MakeMat("BloodChunk", new Color(0.35f, 0.02f, 0.02f, 0.5f), dot.texture);
        Material mistMat = MakeMat("BloodMist", new Color(0.42f, 0.03f, 0.03f, 0.5f), dot.texture);

        // ---------- prefabs (Resources/FX) ----------
        BuildHitPrefab(decalMat, dropMat, chunkMat, mistMat, splatTex);
        BuildPoolPrefab(decalMat, poolTex);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // ---------- UI ----------
        var canvasGo = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).FirstOrDefault(c => c.name == "MissionCanvas");
        if (canvasGo == null) return false;
        BuildUi(canvasGo.transform, vignette, splatSprites.ToArray());
        return true;
    }

    // =============================== blood prefabs ===============================
    static void BuildHitPrefab(Material decalMat, Material dropMat, Material chunkMat, Material mistMat, Texture2D[] splats)
    {
        var root = new GameObject("BloodHit");
        var fx = root.AddComponent<BloodHitFX>();
        fx.splatTextures = splats; fx.decalMaterial = decalMat; fx.floorDecals = 2; fx.reach = 3.2f;

        // fine droplets: fast, stretched, gravity pulls them down
        var spray = NewSystem(root.transform, "Spray", dropMat, 0.55f);
        var m = spray.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 7f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
        m.gravityModifier = 1.6f;
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.32f, 0f, 0f, 0.95f), new Color(0.72f, 0.03f, 0.03f, 0.95f));
        var e = spray.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 22, 32) });
        var sh = spray.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 24f; sh.radius = 0.012f;
        var r = spray.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.07f; r.lengthScale = 1.6f;

        // heavier chunks: slower, fall quickly
        var chunks = NewSystem(root.transform, "Chunks", chunkMat, 0.55f);
        m = chunks.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.1f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.8f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.085f);
        m.gravityModifier = 2.6f;
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.22f, 0f, 0f, 0.95f), new Color(0.5f, 0.02f, 0.02f, 0.95f));
        e = chunks.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 8, 12) });
        sh = chunks.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 42f; sh.radius = 0.02f;

        // soft red mist cloud
        var mist = NewSystem(root.transform, "Mist", mistMat, 0.7f);
        m = mist.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.3f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.4f);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        m.gravityModifier = 0.15f;
        m.startColor = new Color(0.5f, 0.03f, 0.03f, 0.38f);
        e = mist.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 5, 8) });
        sh = mist.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 38f; sh.radius = 0.03f;
        var sol = mist.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.45f, 1f, 1.7f));
        var col = mist.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        SavePrefab(root, FxResources + "/BloodHit.prefab");
    }

    static void BuildPoolPrefab(Material decalMat, Texture2D[] pools)
    {
        var root = new GameObject("BloodPool");
        var fx = root.AddComponent<BloodPoolFX>();
        fx.poolTextures = pools; fx.decalMaterial = decalMat; fx.size = 1.5f;
        SavePrefab(root, FxResources + "/BloodPool.prefab");
    }

    static void SavePrefab(GameObject go, string path)
    {
        AssetDatabase.DeleteAsset(path);
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
    }

    static ParticleSystem NewSystem(Transform parent, string name, Material mat, float duration)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = duration; main.loop = false; main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 100;
        var em = ps.emission; em.rateOverTime = 0f;
        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = ShadowCastingMode.Off; rend.receiveShadows = false;
        return ps;
    }

    // =============================== UI ===============================
    static void BuildUi(Transform canvas, Sprite vignette, Sprite[] splats)
    {
        foreach (string n in new[] { "DamageVignette", "BloodSplatters", "HealthBar", "FailPanel", "PlayerHealthUI" })
        {
            var old = canvas.Find(n);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Sprite square = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Minimap/Icon_Square.png");

        // the original health number ("350 +") is replaced by the life bar: hide it in the scene too
        var oldHud = Object.FindObjectOfType<scgFullBodyController.hudController>(true);
        if (oldHud != null && oldHud.uiHealth != null) oldHud.uiHealth.gameObject.SetActive(false);

        // red vignette (behind everything else)
        var vig = NewImage("DamageVignette", canvas, vignette, new Color(0.8f, 0f, 0f, 0f));
        Stretch(vig.rectTransform); vig.transform.SetAsFirstSibling();

        // blood splatters on the "lens"
        var splatRoot = NewRect("BloodSplatters", canvas); Stretch(splatRoot);
        splatRoot.SetSiblingIndex(1);

        // life bar, top-left - same look as the dialogue box and the weapon boxes: amber frame, dark olive panel,
        // amber medical cross, segmented olive-green gauge (signal red at 50% or less)
        var bar = NewRect("HealthBar", canvas);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0f, 1f);          // top-left corner
        bar.anchoredPosition = new Vector2(40f, -60f);
        bar.sizeDelta = new Vector2(480f, 46f);

        Color amber = new Color(1f, 0.78f, 0.22f, 1f);
        Color panel = new Color(0.07f, 0.09f, 0.075f, 0.95f);          // = the dialogue box
        Sprite rounded = RoundedSprite();
        var frame = NewImage("Frame", bar, rounded, amber); Stretch(frame.rectTransform); frame.type = Image.Type.Sliced;
        var backImg = NewImage("Back", bar, rounded, panel); Stretch(backImg.rectTransform); backImg.type = Image.Type.Sliced;
        backImg.rectTransform.offsetMin = new Vector2(3, 3); backImg.rectTransform.offsetMax = new Vector2(-3, -3);
        var edge = NewImage("InnerEdge", bar, rounded, new Color(1f, 0.78f, 0.22f, 0.20f)); Stretch(edge.rectTransform); edge.type = Image.Type.Sliced;
        edge.rectTransform.offsetMin = new Vector2(7, 7); edge.rectTransform.offsetMax = new Vector2(-7, -7);
        var edgeIn = NewImage("InnerEdgeFill", bar, rounded, panel); Stretch(edgeIn.rectTransform); edgeIn.type = Image.Type.Sliced;
        edgeIn.rectTransform.offsetMin = new Vector2(8, 8); edgeIn.rectTransform.offsetMax = new Vector2(-8, -8);

        // medical cross (amber) at the left end
        Sprite cross = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Minimap/Icon_HealthCross.png");
        var crossImg = NewImage("Cross", bar, cross != null ? cross : square, amber);
        crossImg.rectTransform.anchorMin = crossImg.rectTransform.anchorMax = crossImg.rectTransform.pivot = new Vector2(0f, 0.5f);
        crossImg.rectTransform.anchoredPosition = new Vector2(12f, 0f); crossImg.rectTransform.sizeDelta = new Vector2(30f, 30f);

        // the gauge: rounded mask holding track, trail, fill and the segment ticks
        var inner = NewRect("Inner", bar); inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(52, 11); inner.offsetMax = new Vector2(-12, -11);
        var maskImg = inner.gameObject.AddComponent<Image>(); maskImg.sprite = rounded; maskImg.type = Image.Type.Sliced; maskImg.raycastTarget = false;
        var mask = inner.gameObject.AddComponent<Mask>(); mask.showMaskGraphic = false;
        var track = NewImage("Track", inner, square, new Color(1f, 0.78f, 0.22f, 0.08f)); Stretch(track.rectTransform);
        var trail = NewImage("Trail", inner, square, new Color(1f, 0.9f, 0.55f, 0.6f)); Stretch(trail.rectTransform); MakeFilled(trail);
        var fill = NewImage("Fill", inner, square, new Color(0.62f, 0.82f, 0.28f, 1f)); Stretch(fill.rectTransform); MakeFilled(fill);
        var shine = NewImage("Shine", inner, square, new Color(1f, 1f, 1f, 0.13f));        // glossy top half
        shine.rectTransform.anchorMin = new Vector2(0f, 0.55f); shine.rectTransform.anchorMax = Vector2.one; shine.rectTransform.offsetMin = shine.rectTransform.offsetMax = Vector2.zero;
        for (int k = 1; k < 10; k++)
        {
            var tick = NewImage("Tick" + k, inner, square, new Color(0.07f, 0.09f, 0.075f, 0.9f));
            tick.rectTransform.anchorMin = new Vector2(k / 10f, 0f); tick.rectTransform.anchorMax = new Vector2(k / 10f, 1f);
            tick.rectTransform.pivot = new Vector2(0.5f, 0.5f); tick.rectTransform.sizeDelta = new Vector2(2.5f, 0f); tick.rectTransform.anchoredPosition = Vector2.zero;
        }

        // mission failed panel
        var fail = NewRect("FailPanel", canvas); Stretch(fail);
        var fimg = fail.gameObject.AddComponent<Image>(); fimg.color = new Color(0.25f, 0f, 0f, 0.82f); fimg.raycastTarget = false;
        var ftxt = NewText("FailText", fail, font, "", 56, new Color(1f, 0.85f, 0.85f), TextAnchor.MiddleCenter);
        Stretch(ftxt.rectTransform);
        fail.gameObject.SetActive(false);

        // runtime driver
        var go = new GameObject("PlayerHealthUI");
        go.transform.SetParent(canvas, false);
        var ui = go.AddComponent<PlayerHealthUI>();
        ui.fill = fill; ui.trail = trail; ui.label = null; ui.vignette = vig; ui.splatterRoot = splatRoot; ui.splatSprites = splats;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(new Color(0.9f, 0.1f, 0.1f), 0f), new GradientColorKey(new Color(0.95f, 0.75f, 0.1f), 0.45f), new GradientColorKey(new Color(0.25f, 0.9f, 0.3f), 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        ui.colour = grad;

        // wire the fail panel into the mission manager
        var mm = Object.FindObjectOfType<MissionManager>();
        if (mm != null) { mm.failPanel = fail.gameObject; mm.failText = ftxt; }
        else Debug.LogWarning("[CombatPolish] MissionManager not found - run 'Build Minimap + Rescue Mission' first.");
    }

    static Sprite RoundedSprite()
    {
        const string path = BloodFolder + "/Icon_RoundedRect.png";
        const int w = 128, h = 64, r = 30;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // distance to the inner rectangle (corners are circles of radius r)
                float cx = Mathf.Clamp(x + 0.5f, r, w - r), cy = Mathf.Clamp(y + 0.5f, r, h - r);
                float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r + 0.5f - d)));
            }
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
        imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.wrapMode = TextureWrapMode.Clamp;
        imp.spriteBorder = new Vector4(r, r, r, r);
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static void MakeFilled(Image img)
    {
        img.type = Image.Type.Filled; img.fillMethod = Image.FillMethod.Horizontal; img.fillOrigin = 0; img.fillAmount = 1f;
    }

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
        t.font = font; t.text = text; t.fontSize = size; t.color = color; t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false;
        var o = rt.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0, 0, 0, 0.9f); o.effectDistance = new Vector2(1.5f, -1.5f);
        return t;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    // =============================== procedural textures ===============================
    delegate float F(float u, float v);

    static Sprite MakeTexture(string name, int size, F unused, int seed, bool splat, bool pool = false, bool dot = false, bool streak = false, bool vignette = false)
    {
        string path = BloodFolder + "/" + name + ".png";
        int w = streak ? 16 : size, h = streak ? 64 : size;
        var rnd = new System.Random(seed);
        var px = new Color[w * h];

        // random shape parameters
        float[] amp = new float[6], ph = new float[6];
        for (int i = 0; i < 6; i++) { amp[i] = (float)(rnd.NextDouble() * 0.09 / (1 + i * 0.5)); ph[i] = (float)(rnd.NextDouble() * 6.283); }
        var sat = new List<Vector3>();   // satellite droplets x,y,r
        if (splat)
            for (int i = 0; i < 16; i++)
            {
                float a = (float)(rnd.NextDouble() * 6.283), d = 0.3f + (float)rnd.NextDouble() * 0.17f, r = 0.008f + (float)rnd.NextDouble() * 0.035f;
                sat.Add(new Vector3(0.5f + Mathf.Cos(a) * d, 0.5f + Mathf.Sin(a) * d, r));
            }
        // a few drips running down from the blob
        var drips = new List<Vector3>();   // x, length, width
        if (splat)
            for (int i = 0; i < 3; i++) drips.Add(new Vector3(0.38f + (float)rnd.NextDouble() * 0.24f, 0.1f + (float)rnd.NextDouble() * 0.2f, 0.012f + (float)rnd.NextDouble() * 0.012f));
        float baseR = pool ? 0.33f : 0.2f + (float)rnd.NextDouble() * 0.06f;

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                float a = 0f; Color c = new Color(0.45f, 0.0f, 0.02f);
                if (dot) { float d = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f; a = Mathf.Clamp01(1f - d); a *= a; c = Color.white; }
                else if (streak)
                {
                    float xx = (u - 0.5f) * 2f; a = Mathf.Exp(-xx * xx * 6f) * Mathf.Pow(Mathf.Sin(v * Mathf.PI), 0.7f); c = Color.white;
                }
                else if (vignette)
                {
                    float dx = (u - 0.5f) * 2f, dy = (v - 0.5f) * 2f; float d = Mathf.Sqrt(dx * dx + dy * dy);
                    a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.25f, d)); c = Color.white;
                }
                else
                {
                    float dx = u - 0.5f, dy = v - 0.5f; float d = Mathf.Sqrt(dx * dx + dy * dy); float th = Mathf.Atan2(dy, dx);
                    float R = baseR; for (int i = 0; i < 6; i++) R += amp[i] * Mathf.Cos(th * (i + 2) + ph[i]);
                    float edge = pool ? 0.045f : 0.02f;
                    a = Mathf.Clamp01((R - d) / edge);
                    if (splat)
                    {
                        foreach (var s in sat) { float dd = Mathf.Sqrt((u - s.x) * (u - s.x) + (v - s.y) * (v - s.y)); a = Mathf.Max(a, Mathf.Clamp01((s.z - dd) / 0.008f)); }
                        foreach (var dr in drips)
                            if (v < 0.5f && v > 0.5f - dr.y && Mathf.Abs(u - dr.x) < dr.z) a = Mathf.Max(a, 1f);
                    }
                    float shade = Mathf.Clamp01(d / Mathf.Max(0.01f, R));
                    c = Color.Lerp(pool ? new Color(0.30f, 0.0f, 0.01f) : new Color(0.55f, 0.01f, 0.03f), pool ? new Color(0.42f, 0.02f, 0.03f) : new Color(0.32f, 0.0f, 0.02f), shade);
                    a *= pool ? 0.92f : 0.97f;
                }
                px[y * w + x] = new Color(c.r, c.g, c.b, a);
            }
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels(px); tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
        imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.wrapMode = TextureWrapMode.Clamp;
        imp.filterMode = FilterMode.Bilinear;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static Material MakeMat(string name, Color tint, Texture2D tex)
    {
        string path = BloodFolder + "/" + name + ".mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
        m.SetColor("_TintColor", tint);
        if (tex != null) m.mainTexture = tex;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
