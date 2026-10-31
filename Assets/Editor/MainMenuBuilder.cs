using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;
using GAMECON.Menu;

/// <summary>
/// Builds the MAIN MENU scene (the Sun Temple gate plaza with the four warriors walking in, the title and the buttons) and the STORY scene
/// (with SKIP INTRO), and puts MainMenu, StoryScene and SunTemple_Night into the Build Settings in that order.
/// Run it with GAMECON > Build Main Menu + Story Scene.
/// </summary>
public static class MainMenuBuilder
{
    const string MenuScene = "Assets/Scenes/MainMenu.unity";
    const string StoryScene = "Assets/Scenes/StoryScene.unity";
    const string GameScene = "Assets/Scenes/SunTemple_Night.unity";
    const string BaseScene = "Assets/Scenes/SunTemple_Game.unity";          // the day-time Sun Temple the menu is made from
    const string Folder = "Assets/UI/MainMenu";
    const string Gen = "Assets/UI/MainMenu/Generated";

    static readonly Color Amber = new Color(1f, 0.78f, 0.22f, 1f);

    [MenuItem("GAMECON/Build Main Menu + Story Scene")]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string r = BuildAll();
        EditorUtility.DisplayDialog("Main menu", r, "OK");
    }

    public static string BuildAll()
    {
        string original = SceneManager.GetActiveScene().path;
        Directory.CreateDirectory(Gen);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ImportLogo();
        var report = new System.Text.StringBuilder();
        try
        {
            var sprites = MakeSprites();
            var swordPrefab = MakeSwordPrefab();
            BuildMenuScene(sprites, swordPrefab, report);
            BuildStoryScene(sprites, report);
            SetBuildSettings();
        }
        finally
        {
            if (!string.IsNullOrEmpty(original) && File.Exists(original)) EditorSceneManager.OpenScene(original, OpenSceneMode.Single);
        }
        report.AppendLine("\nBuild Settings: 0 MainMenu, 1 StoryScene, 2 SunTemple_Night.\nPress Play with MainMenu open (or build the game) to see it.");
        return report.ToString();
    }

    // =========================================================================================================== textures
    class Sprites
    {
        public Sprite logo, logoGlow, shine, chevron, radial, vignette, gradV, panelFill, panelFrame, panelFillLarge, panelFrameLarge, wash, lineFade;
        public Dictionary<string, Sprite[]> buttons = new Dictionary<string, Sprite[]>();     // "WxH" -> fill, frame, glow
    }

    static Sprite LoadSprite(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null && (imp.textureType != TextureImporterType.Sprite || imp.mipmapEnabled))
        {
            imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Bilinear; imp.textureCompression = TextureImporterCompression.Uncompressed; imp.maxTextureSize = 4096;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static void ImportLogo()
    {
        foreach (var n in new[] { "TitleLogo.png", "TitleGlow.png" })
        {
            string p = Folder + "/" + n;
            if (File.Exists(p)) { AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceSynchronousImport); LoadSprite(p); }
            else Debug.LogWarning("[MainMenu] missing " + p);
        }
    }

    static Sprite Save(string name, int w, int h, Func<int, int, Color32> px)
    {
        string path = Gen + "/" + name + ".png";
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var data = new Color32[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) data[y * w + x] = px(x, y);
        tex.SetPixels32(data); tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
        imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.wrapMode = TextureWrapMode.Clamp;
        imp.filterMode = FilterMode.Bilinear; imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static Color32 White(float a) { return new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f)); }

    // a rectangle with two cut corners (top-left and bottom-right); returns the signed distance (> 0 inside) in pixels
    static float ChamferDist(float x, float y, float w, float h, float c)
    {
        // polygon (y up): (0,c) (c,0)... we cut the bottom-left and top-right in texture space (y = 0 is the bottom), which reads as top-left / bottom-right cuts in the UI
        Vector2[] poly = { new Vector2(c, 0), new Vector2(w, 0), new Vector2(w, h - c), new Vector2(w - c, h), new Vector2(0, h), new Vector2(0, c) };
        Vector2 p = new Vector2(x, y);
        float best = float.MaxValue; bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            Vector2 a = poly[j], b = poly[i], ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            best = Mathf.Min(best, (p - (a + ab * t)).magnitude);
            if (((poly[i].y > p.y) != (poly[j].y > p.y)) && (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)) inside = !inside;
        }
        return inside ? best : -best;
    }

    static Sprite[] MakeButton(int w, int h)
    {
        float c = Mathf.Min(h * 0.30f, 22f);
        string n = "Btn_" + w + "x" + h;
        var fill = Save(n + "_fill", w, h, (x, y) => White(ChamferDist(x + 0.5f, y + 0.5f, w, h, c) + 0.5f));
        var frame = Save(n + "_frame", w, h, (x, y) =>
        {
            float d = ChamferDist(x + 0.5f, y + 0.5f, w, h, c);
            float line = Mathf.Clamp01(1.6f - Mathf.Abs(d - 1.4f));                    // a 2 px line just inside the edge
            float inner = 0.28f * Mathf.Clamp01(1.2f - Mathf.Abs(d - 6.5f));           // a faint second line
            // a brighter short accent on the cut corners
            return White(Mathf.Max(line, inner));
        });
        var glow = Save(n + "_glow", w, h, (x, y) =>
        {
            float d = ChamferDist(x + 0.5f, y + 0.5f, w, h, c);
            if (d <= 0) return White(0);
            float gx = Mathf.Clamp01(1f - x / (w * 0.95f));
            float gy = 1f - Mathf.Abs(y - h * 0.5f) / (h * 0.5f) * 0.35f;
            return White(gx * gx * 0.9f * gy);
        });
        return new[] { fill, frame, glow };
    }

    static Sprite[] MakePanel(string name, int w, int h, float c)
    {
        var fill = Save(name + "_fill", w, h, (x, y) => White(ChamferDist(x + 0.5f, y + 0.5f, w, h, c) + 0.5f));
        var frame = Save(name + "_frame", w, h, (x, y) =>
        {
            float d = ChamferDist(x + 0.5f, y + 0.5f, w, h, c);
            float line = Mathf.Clamp01(1.8f - Mathf.Abs(d - 1.5f));
            float inner = 0.35f * Mathf.Clamp01(1.3f - Mathf.Abs(d - 9f));
            return White(Mathf.Max(line, inner));
        });
        return new[] { fill, frame };
    }

    static Sprites MakeSprites()
    {
        var s = new Sprites();
        s.logo = LoadSprite(Folder + "/TitleLogo.png");
        s.logoGlow = LoadSprite(Folder + "/TitleGlow.png");
        // the text-menu buttons: a warm wash that fades out to the right, and a thin line that fades out as well
        s.wash = Save("Wash", 256, 64, (x, y) =>
        {
            float gx = Mathf.Pow(1f - x / 255f, 1.6f);
            float gy = Mathf.Pow(Mathf.Sin(Mathf.PI * (y + 0.5f) / 64f), 0.5f);
            return White(gx * gy);
        });
        s.lineFade = Save("LineFade", 256, 4, (x, y) =>
        {
            float gx = Mathf.Pow(1f - x / 255f, 1.4f);
            float gy = (y == 1 || y == 2) ? 1f : 0.35f;
            return White(gx * gy);
        });
        var p1 = MakePanel("Panel_860x640", 860, 640, 30); s.panelFill = p1[0]; s.panelFrame = p1[1];
        var p2 = MakePanel("Panel_1040x700", 1040, 700, 30); s.panelFillLarge = p2[0]; s.panelFrameLarge = p2[1];

        // a diagonal soft bar for the light sweeps
        s.shine = Save("Shine", 96, 128, (x, y) =>
        {
            float u = x / 96f - 0.30f * (y / 128f - 0.5f);
            float a = Mathf.Clamp01(1f - Mathf.Abs(u - 0.5f) * 2.2f);
            return White(a * a);
        });
        s.chevron = Save("Chevron", 28, 40, (x, y) =>
        {
            // a ">" made of two strokes
            float px = x + 0.5f, py = y + 0.5f;
            float d1 = DistToSeg(px, py, 6f, 6f, 20f, 20f), d2 = DistToSeg(px, py, 20f, 20f, 6f, 34f);
            return White(Mathf.Clamp01(2.2f - Mathf.Min(d1, d2)));
        });
        s.radial = Save("Radial", 256, 256, (x, y) =>
        {
            float r = Mathf.Sqrt((x - 127.5f) * (x - 127.5f) + (y - 127.5f) * (y - 127.5f)) / 128f;
            float a = Mathf.Clamp01(1f - r); return White(a * a);
        });
        s.vignette = Save("Vignette", 256, 256, (x, y) =>
        {
            float dx = (x - 127.5f) / 128f, dy = (y - 127.5f) / 128f;
            float r = Mathf.Sqrt(dx * dx * 0.8f + dy * dy * 1.15f);
            return White(Mathf.Clamp01((r - 0.45f) / 0.75f) * 0.95f);
        });
        s.gradV = Save("GradV", 4, 256, (x, y) => White(Mathf.Pow(y / 255f, 1.6f)));          // opaque at the top, clear at the bottom
        return s;
    }

    static float DistToSeg(float px, float py, float ax, float ay, float bx, float by)
    {
        float abx = bx - ax, aby = by - ay;
        float t = Mathf.Clamp01(((px - ax) * abx + (py - ay) * aby) / (abx * abx + aby * aby));
        float cx = ax + abx * t, cy = ay + aby * t;
        return Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
    }

    // =========================================================================================================== the sword prefab
    static GameObject MakeSwordPrefab()
    {
        const string path = "Assets/Prefabs/Rigged/Sword_Prop.prefab";
        var holder = new GameObject("tmp");
        var sword = KeyArtRenderer.NewSword(holder.transform);
        sword.name = "Sword_Prop";
        // procedural meshes / materials must be assets to live in a prefab
        string dir = "Assets/Prefabs/Rigged/Sword"; Directory.CreateDirectory(dir);
        int i = 0;
        foreach (var mf in sword.GetComponentsInChildren<MeshFilter>(true))
        {
            string mp = dir + "/Mesh_" + (i++) + ".asset";
            var mesh = mf.sharedMesh;
            if (mesh != null && !AssetDatabase.Contains(mesh)) { AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(UnityEngine.Object.Instantiate(mesh), mp); mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(mp); }
        }
        var done = new Dictionary<Material, Material>(); int mi = 0;
        foreach (var r in sword.GetComponentsInChildren<Renderer>(true))
        {
            var m = r.sharedMaterial; if (m == null) continue;
            if (!done.ContainsKey(m))
            {
                string mp = dir + "/Mat_" + (mi++) + ".mat";
                AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(UnityEngine.Object.Instantiate(m), mp);
                done[m] = AssetDatabase.LoadAssetAtPath<Material>(mp);
            }
            r.sharedMaterial = done[m];
        }
        sword.SetParent(null, false);
        AssetDatabase.DeleteAsset(path);
        var prefab = PrefabUtility.SaveAsPrefabAsset(sword.gameObject, path);
        UnityEngine.Object.DestroyImmediate(sword.gameObject);
        UnityEngine.Object.DestroyImmediate(holder);
        return prefab;
    }

    // =========================================================================================================== UI helpers
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

    static Text NewText(string name, Transform parent, string text, int size, Color color, TextAnchor anchor, FontStyle style = FontStyle.Bold)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = text; t.fontSize = size; t.color = color; t.alignment = anchor; t.fontStyle = style; t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.supportRichText = true;
        var o = rt.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, 0.85f); o.effectDistance = new Vector2(1.5f, -1.5f);
        return t;
    }

    static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
    {
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
    }

    static Canvas NewCanvas(string name, int order)
    {
        var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = order;
        var sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;           // the whole 1920x1080 layout always fits, on any screen shape
        return c;
    }

    /// <summary>
    /// A menu entry set like the game's title: serif capitals on the dark scene, a thin gold line under it, and on hover a warm wash from the left,
    /// a small diamond, gold text that slides in. No boxes. (withChevron = false centres the text, for the panels.)
    /// </summary>
    static MenuButton MakeButton(Transform parent, string name, string label, int w, int h, Sprites sp, bool primary, Vector2 anchor, Vector2 pos, int fontSize, bool leftAligned = true)
    {
        var rt = NewRect(name, parent);
        Place(rt, anchor, pos, new Vector2(w, h));
        var group = rt.gameObject.AddComponent<CanvasGroup>();
        var hit = rt.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, 0); hit.raycastTarget = true;           // the clickable area

        var wash = NewImage("Wash", rt, sp.wash, new Color(0.98f, 0.74f, 0.30f, 0f)); Stretch(wash.rectTransform);
        var flash = NewImage("Flash", rt, sp.wash, new Color(1, 1, 1, 0f)); Stretch(flash.rectTransform);
        var line = NewImage("Line", rt, sp.lineFade, new Color(Amber.r, Amber.g, Amber.b, 0.3f));
        line.rectTransform.anchorMin = new Vector2(0f, 0f); line.rectTransform.anchorMax = new Vector2(1f, 0f); line.rectTransform.pivot = new Vector2(0f, 0f);
        line.rectTransform.sizeDelta = new Vector2(0f, 4f); line.rectTransform.anchoredPosition = Vector2.zero;

        var diamond = NewImage("Diamond", rt, null, Amber);
        Place(diamond.rectTransform, new Vector2(0f, 0.5f), new Vector2(16f, 2f), new Vector2(10f, 10f)); diamond.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);

        var text = NewText("Label", rt, label, fontSize, new Color(0.93f, 0.91f, 0.84f), leftAligned ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
        if (leftAligned) Place(text.rectTransform, new Vector2(0f, 0.5f), new Vector2(46f, 2f), new Vector2(w - 60f, h), new Vector2(0f, 0.5f));
        else Place(text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 2f), new Vector2(w, h));

        text.GetComponent<Outline>().effectDistance = new Vector2(2.2f, -2.2f);
        var b = rt.gameObject.AddComponent<MenuButton>();
        b.root = rt; b.group = group; b.fill = wash; b.frame = line; b.flash = flash; b.diamond = diamond; b.label = text; b.primary = primary;
        b.minimal = true; b.centered = !leftAligned;
        return b;
    }

    // =========================================================================================================== the main menu scene
    static void BuildMenuScene(Sprites sp, GameObject swordPrefab, System.Text.StringBuilder report)
    {
        AssetDatabase.DeleteAsset(MenuScene);
        if (!AssetDatabase.CopyAsset(BaseScene, MenuScene)) throw new Exception("could not copy " + BaseScene);
        var scene = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Single);

        // ---- strip the gameplay: the player, the enemies, the demo HUD and the game logic ----
        foreach (var root in scene.GetRootGameObjects())
            if (root.name.Contains("PLAYER") || root.name.Contains("ENEMIES") || root.name.Contains("UI") || root.name.Contains("GAME LOGIC"))
                UnityEngine.Object.DestroyImmediate(root);
        foreach (var mb in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)).ToList())
        {
            if (mb == null) continue;
            string tn = mb.GetType().Name;
            if (tn == "Door" || tn == "DistanceHide" || tn == "CullDistanceVolume" || tn == "ExposureArea" || tn == "ExposureManager" || tn == "TriggerArea" ||
                tn == "CursorLock" || tn == "CursorManager" || tn == "CullDistances" || tn == "FPSDisplay" || tn == "sceneController" || tn == "hudController" ||
                tn == "CharController_Motor" || tn == "PostProcessingBehaviour")
                UnityEngine.Object.DestroyImmediate(mb);
        }
        foreach (var cam in UnityEngine.Object.FindObjectsOfType<Camera>(true)) UnityEngine.Object.DestroyImmediate(cam.gameObject);
        foreach (var l in UnityEngine.Object.FindObjectsOfType<AudioListener>(true)) UnityEngine.Object.DestroyImmediate(l);

        // the small red gameplay markers of the demo scene are hidden
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

        // ---- the look (the same as the key art): golden hour, a warm sun behind the camera's left, a cool rim light ----
        Light sun = RenderSettings.sun;
        if (sun == null) sun = UnityEngine.Object.FindObjectsOfType<Light>().FirstOrDefault(l => l.type == LightType.Directional);
        if (sun != null)
        {
            sun.transform.rotation = EulerFor(0.6f, -0.5f, -0.62f); sun.color = new Color(1f, 0.84f, 0.62f); sun.intensity = 1.8f;
            sun.shadows = LightShadows.Soft; sun.shadowStrength = 1f; RenderSettings.sun = sun;
        }
        RenderSettings.ambientIntensity = 1.5f;
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.002f; RenderSettings.fogColor = new Color(0.78f, 0.82f, 0.92f);
        DynamicGI.UpdateEnvironment();

        var menuRoot = new GameObject("====== MAIN MENU ======");

        var rim = new GameObject("Rim Light (menu)", typeof(Light)); rim.transform.SetParent(menuRoot.transform, false);
        var rl = rim.GetComponent<Light>(); rl.type = LightType.Directional; rl.color = new Color(0.62f, 0.78f, 1f); rl.intensity = 1.3f; rl.shadows = LightShadows.None;
        rim.transform.rotation = EulerFor(-0.7f, -0.22f, 0.25f);

        // ---- the camera ----
        var camGo = new GameObject("Menu Camera", typeof(Camera), typeof(AudioListener));
        camGo.transform.SetParent(menuRoot.transform, false); camGo.tag = "MainCamera";
        var cam2 = camGo.GetComponent<Camera>();
        cam2.fieldOfView = 42f; cam2.nearClipPlane = 0.1f; cam2.farClipPlane = 1500f; cam2.allowHDR = true; cam2.allowMSAA = true; cam2.clearFlags = CameraClearFlags.Skybox;
        camGo.transform.position = new Vector3(75.5f, 2f, -150.5f);

        // ---- audio ----
        var audio = new GameObject("Audio"); audio.transform.SetParent(menuRoot.transform, false);
        AudioSource Src(string n, bool loop) { var g = new GameObject(n, typeof(AudioSource)); g.transform.SetParent(audio.transform, false); var s = g.GetComponent<AudioSource>(); s.playOnAwake = false; s.loop = loop; s.spatialBlend = 0f; return s; }
        var music = Src("Music", true); music.clip = Clip("Menu_Theme");
        var sfx = Src("SFX", false); var steps = Src("Footsteps", false);

        // ---- floating dust in the light ----
        var dust = BuildDust(menuRoot.transform, sp.radial);

        // ---- the UI ----
        var canvas = NewCanvas("MenuCanvas", 10);
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)); es.transform.SetParent(menuRoot.transform, false);

        var vig = NewImage("Vignette", canvas.transform, sp.vignette, new Color(0, 0, 0, 0.55f)); Stretch(vig.rectTransform);
        var top = NewImage("TopShade", canvas.transform, sp.gradV, new Color(0.02f, 0.02f, 0.02f, 0.78f));
        top.rectTransform.anchorMin = new Vector2(0, 1); top.rectTransform.anchorMax = new Vector2(1, 1); top.rectTransform.pivot = new Vector2(0.5f, 1f); top.rectTransform.sizeDelta = new Vector2(0, 600); top.rectTransform.anchoredPosition = Vector2.zero;
        var bot = NewImage("BottomShade", canvas.transform, sp.gradV, new Color(0.02f, 0.02f, 0.02f, 0.80f));
        bot.rectTransform.anchorMin = new Vector2(0, 0); bot.rectTransform.anchorMax = new Vector2(1, 0); bot.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        bot.rectTransform.sizeDelta = new Vector2(0, 360); bot.rectTransform.anchoredPosition = new Vector2(0, 180);
        bot.rectTransform.localScale = new Vector3(1, -1, 1);          // the same gradient flipped: opaque at the bottom, clear towards the top

        // a soft dark column behind the menu entries (they stand on a pale wall)
        var menuShade = NewImage("MenuShade", canvas.transform, sp.wash, new Color(0.02f, 0.02f, 0.02f, 0.66f));
        menuShade.rectTransform.anchorMin = new Vector2(1f, 0f); menuShade.rectTransform.anchorMax = new Vector2(1f, 1f); menuShade.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        menuShade.rectTransform.sizeDelta = new Vector2(900f, 0f); menuShade.rectTransform.anchoredPosition = new Vector2(-450f, 0f); menuShade.rectTransform.localScale = new Vector3(-1f, 1f, 1f);

        // the logo: a glow plate, the gold face (with a moving light sweep inside it)
        var logo = NewRect("Logo", canvas.transform);
        Place(logo, new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(1518f, 442f), new Vector2(0.5f, 1f));
        var logoGroup = logo.gameObject.AddComponent<CanvasGroup>();
        var glowImg = NewImage("Glow", logo, sp.logoGlow, new Color(Amber.r, Amber.g, Amber.b, 0.4f)); Stretch(glowImg.rectTransform);
        var face = NewImage("Face", logo, sp.logo, Color.white); Stretch(face.rectTransform);
        face.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var shine = NewImage("Shine", face.transform, sp.shine, new Color(1f, 0.97f, 0.85f, 0f));
        shine.rectTransform.anchorMin = new Vector2(0.5f, 0f); shine.rectTransform.anchorMax = new Vector2(0.5f, 1f); shine.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        shine.rectTransform.sizeDelta = new Vector2(260f, 0f);

        // the buttons, on the right
        var play = MakeButton(canvas.transform, "PlayButton", "PLAY", 470, 88, sp, true, new Vector2(1f, 1f), new Vector2(-270f, -580f), 50);
        var controls = MakeButton(canvas.transform, "ControlsButton", "CONTROLS", 430, 70, sp, false, new Vector2(1f, 1f), new Vector2(-270f, -680f), 38);
        var settings = MakeButton(canvas.transform, "SettingsButton", "SETTINGS", 430, 70, sp, false, new Vector2(1f, 1f), new Vector2(-270f, -760f), 38);
        var quit = MakeButton(canvas.transform, "QuitButton", "QUIT", 430, 70, sp, false, new Vector2(1f, 1f), new Vector2(-270f, -840f), 38);

        // (no on-screen key hints)

        // panels
        var settingsPanel = BuildSettingsPanel(canvas.transform, sp, out var masterSlider, out var musicSlider, out var masterVal, out var musicVal, out var masterLbl, out var musicLbl, out var fsButton, out var fsLabel, out var settingsBack);
        var controlsPanel = BuildControlsPanel(canvas.transform, sp, out var controlsBack);

        // the fade (on top of everything)
        var fadeImg = NewImage("Fade", canvas.transform, null, Color.black); Stretch(fadeImg.rectTransform);

        // ---- the director ----
        var dirGo = new GameObject("MenuDirector"); dirGo.transform.SetParent(menuRoot.transform, false);
        var d = dirGo.AddComponent<MainMenuDirector>();
        d.cam = cam2;
        d.heroPrefabs = new[] { Load("Samrah"), Load("Duaa"), Load("Zubia"), Load("Maha") };
        d.riflePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Rigged/Gun_DeathProp.prefab");
        d.swordPrefab = swordPrefab;
        if (d.riflePrefab == null) throw new Exception("Gun_DeathProp.prefab missing - run GAMECON > Build Death Scene first");
        foreach (var hp in d.heroPrefabs) if (hp == null) throw new Exception("a rigged hero prefab is missing - run GAMECON > Rig Teammates first");
        d.dust = dust;
        d.music = music; d.sfx = sfx; d.steps = steps;
        d.hoverClip = Clip("UI_Hover"); d.clickClip = Clip("UI_Click"); d.backClip = Clip("UI_Back"); d.whooshClip = Clip("UI_Whoosh"); d.stepClip = Clip("Step_Stone");
        d.canvas = canvas; d.fade = fadeImg; d.logoGroup = logoGroup; d.logoRect = logo; d.logoGlow = glowImg; d.logoShine = shine;
        d.playButton = play; d.controlsButton = controls; d.settingsButton = settings; d.quitButton = quit;
        d.settingsPanel = settingsPanel; d.controlsPanel = controlsPanel; d.settingsBack = settingsBack; d.controlsBack = controlsBack; d.fullscreenButton = fsButton;
        d.masterSlider = masterSlider; d.musicSlider = musicSlider; d.masterValue = masterVal; d.musicValue = musicVal; d.fullscreenLabel = fsLabel;
        d.masterLabel = masterLbl; d.musicLabel = musicLbl;
        d.nextScene = "StoryScene";

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        report.AppendLine("MainMenu scene built: " + MenuScene);
    }

    static GameObject Load(string who) { return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Rigged/" + who + "_Rigged.prefab"); }

    static AudioClip Clip(string n)
    {
        string p = "Assets/Resources/Audio/Menu/" + n + ".wav";
        AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceSynchronousImport);
        var c = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
        if (c == null) Debug.LogWarning("[MainMenu] audio clip missing: " + p);
        return c;
    }

    static Quaternion EulerFor(float x, float y, float z) { return Quaternion.LookRotation(new Vector3(x, y, z).normalized); }

    static ParticleSystem BuildDust(Transform parent, Sprite radial)
    {
        var go = new GameObject("Dust", typeof(ParticleSystem)); go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(78f, 2.2f, -150f);
        var ps = go.GetComponent<ParticleSystem>();
        var main = ps.main; main.loop = true; main.playOnAwake = true; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 12f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.07f); main.maxParticles = 260;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.92f, 0.75f, 0.35f), new Color(1f, 0.8f, 0.5f, 0.7f));
        var em = ps.emission; em.rateOverTime = 26f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(14f, 5f, 12f);
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 0.25f; noise.scrollSpeed = 0.1f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        var rend = go.GetComponent<ParticleSystemRenderer>();
        var shader = Shader.Find("Legacy Shaders/Particles/Additive"); if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        string mp = Gen + "/Dust.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, mp); }
        mat.shader = shader; mat.mainTexture = radial != null ? radial.texture : null;
        EditorUtility.SetDirty(mat);
        rend.sharedMaterial = mat; rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rend.receiveShadows = false;
        return ps;
    }

    // ---------------------------------------------------------------------------------------------------- the settings panel
    static MenuPanel BuildSettingsPanel(Transform canvas, Sprites sp, out Slider master, out Slider music, out Text masterVal, out Text musicVal, out Text masterLbl, out Text musicLbl,
                                        out MenuButton fsButton, out Text fsLabel, out MenuButton back)
    {
        var root = NewRect("SettingsPanel", canvas); Stretch(root);
        var group = root.gameObject.AddComponent<CanvasGroup>();
        var dim = NewImage("Dim", root, null, new Color(0.01f, 0.012f, 0.01f, 0.72f)); Stretch(dim.rectTransform); dim.raycastTarget = true;
        var win = NewRect("Window", root); Place(win, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860f, 640f));
        NewImage("Fill", win, sp.panelFill, new Color(0.07f, 0.09f, 0.075f, 0.97f)).rectTransform.sizeDelta = new Vector2(860f, 640f);
        NewImage("Frame", win, sp.panelFrame, Amber).rectTransform.sizeDelta = new Vector2(860f, 640f);
        var title = NewText("Title", win, "SETTINGS", 52, Amber, TextAnchor.MiddleCenter); Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(700f, 70f));
        var sep = NewImage("Sep", win, null, new Color(1f, 0.78f, 0.22f, 0.45f)); Place(sep.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 205f), new Vector2(740f, 2f));
        var dmd = NewImage("Diamond", win, null, Amber); Place(dmd.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 205f), new Vector2(14f, 14f)); dmd.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);

        masterLbl = NewText("MasterLabel", win, "MASTER VOLUME", 30, new Color(0.93f, 0.94f, 0.87f), TextAnchor.MiddleLeft);
        Place(masterLbl.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-380f, 115f), new Vector2(320f, 50f), new Vector2(0f, 0.5f));
        master = MakeSlider(win, new Vector2(70f, 115f), sp);
        masterVal = NewText("MasterValue", win, "100%", 30, Amber, TextAnchor.MiddleRight); Place(masterVal.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(380f, 115f), new Vector2(110f, 50f), new Vector2(1f, 0.5f));

        musicLbl = NewText("MusicLabel", win, "MUSIC VOLUME", 30, new Color(0.93f, 0.94f, 0.87f), TextAnchor.MiddleLeft);
        Place(musicLbl.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-380f, 15f), new Vector2(320f, 50f), new Vector2(0f, 0.5f));
        music = MakeSlider(win, new Vector2(70f, 15f), sp);
        musicVal = NewText("MusicValue", win, "85%", 30, Amber, TextAnchor.MiddleRight); Place(musicVal.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(380f, 15f), new Vector2(110f, 50f), new Vector2(1f, 0.5f));

        fsButton = MakeButton(win, "FullscreenButton", "FULLSCREEN:  OFF", 520, 64, sp, false, new Vector2(0.5f, 0.5f), new Vector2(0f, -95f), 32, false);
        fsButton.slideFrom = new Vector2(0f, -30f);
        fsLabel = fsButton.label;
        back = MakeButton(win, "BackButton", "BACK", 300, 64, sp, true, new Vector2(0.5f, 0.5f), new Vector2(0f, -215f), 36, false);
        back.slideFrom = new Vector2(0f, -30f);

        var panel = root.gameObject.AddComponent<MenuPanel>(); panel.group = group; panel.window = win;
        root.gameObject.SetActive(false);
        return panel;
    }

    static Slider MakeSlider(Transform parent, Vector2 pos, Sprites sp)
    {
        var rt = NewRect("Slider", parent);
        Place(rt, new Vector2(0.5f, 0.5f), pos, new Vector2(360f, 34f));
        var bg = NewImage("Background", rt, null, new Color(1f, 1f, 1f, 0.12f)); bg.raycastTarget = true;
        bg.rectTransform.anchorMin = new Vector2(0f, 0.5f); bg.rectTransform.anchorMax = new Vector2(1f, 0.5f); bg.rectTransform.sizeDelta = new Vector2(0f, 8f);
        var fa = NewRect("Fill Area", rt); fa.anchorMin = new Vector2(0f, 0.5f); fa.anchorMax = new Vector2(1f, 0.5f); fa.sizeDelta = new Vector2(-14f, 8f); fa.anchoredPosition = Vector2.zero;
        var fill = NewImage("Fill", fa, null, Amber); fill.rectTransform.anchorMin = new Vector2(0, 0); fill.rectTransform.anchorMax = new Vector2(0, 1); fill.rectTransform.sizeDelta = new Vector2(10f, 0f);
        var ha = NewRect("Handle Slide Area", rt); ha.anchorMin = Vector2.zero; ha.anchorMax = Vector2.one; ha.offsetMin = new Vector2(10f, 0f); ha.offsetMax = new Vector2(-10f, 0f);
        var handle = NewImage("Handle", ha, null, new Color(1f, 0.9f, 0.5f)); handle.raycastTarget = true;
        handle.rectTransform.sizeDelta = new Vector2(22f, 22f); handle.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
        var s = rt.gameObject.AddComponent<Slider>();
        s.fillRect = fill.rectTransform; s.handleRect = handle.rectTransform; s.targetGraphic = handle; s.direction = Slider.Direction.LeftToRight;
        s.minValue = 0f; s.maxValue = 1f; s.value = 1f; s.transition = Selectable.Transition.None;
        var nav = new Navigation { mode = Navigation.Mode.None }; s.navigation = nav;
        return s;
    }

    // ---------------------------------------------------------------------------------------------------- the controls panel
    static MenuPanel BuildControlsPanel(Transform canvas, Sprites sp, out MenuButton back)
    {
        var root = NewRect("ControlsPanel", canvas); Stretch(root);
        var group = root.gameObject.AddComponent<CanvasGroup>();
        var dim = NewImage("Dim", root, null, new Color(0.01f, 0.012f, 0.01f, 0.72f)); Stretch(dim.rectTransform); dim.raycastTarget = true;
        var win = NewRect("Window", root); Place(win, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1040f, 700f));
        NewImage("Fill", win, sp.panelFillLarge, new Color(0.07f, 0.09f, 0.075f, 0.97f)).rectTransform.sizeDelta = new Vector2(1040f, 700f);
        NewImage("Frame", win, sp.panelFrameLarge, Amber).rectTransform.sizeDelta = new Vector2(1040f, 700f);
        var title = NewText("Title", win, "CONTROLS", 52, Amber, TextAnchor.MiddleCenter); Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 290f), new Vector2(700f, 70f));
        var sep = NewImage("Sep", win, null, new Color(1f, 0.78f, 0.22f, 0.45f)); Place(sep.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 245f), new Vector2(900f, 2f));
        var dmd = NewImage("Diamond", win, null, Amber); Place(dmd.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 245f), new Vector2(14f, 14f)); dmd.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);

        string[,] left = { { "W A S D", "MOVE" }, { "MOUSE", "LOOK / AIM" }, { "LEFT CLICK", "FIRE" }, { "R", "RELOAD" }, { "1   2   3", "CHOOSE WEAPON" } };
        string[,] right = { { "G", "GRENADE" }, { "F", "TORCH" }, { "E", "INTERACT / FREE" }, { "M", "FULL MAP" }, { "SPACE", "JUMP" } };
        for (int i = 0; i < 5; i++)
        {
            float y = 165f - i * 72f;
            var k1 = NewText("Key" + i, win, left[i, 0], 30, Amber, TextAnchor.MiddleLeft); Place(k1.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-470f, y), new Vector2(190f, 50f), new Vector2(0f, 0.5f));
            var d1 = NewText("Desc" + i, win, left[i, 1], 28, new Color(0.93f, 0.94f, 0.87f), TextAnchor.MiddleLeft, FontStyle.Normal); Place(d1.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-270f, y), new Vector2(270f, 50f), new Vector2(0f, 0.5f));
            var k2 = NewText("KeyR" + i, win, right[i, 0], 30, Amber, TextAnchor.MiddleLeft); Place(k2.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(40f, y), new Vector2(150f, 50f), new Vector2(0f, 0.5f));
            var d2 = NewText("DescR" + i, win, right[i, 1], 28, new Color(0.93f, 0.94f, 0.87f), TextAnchor.MiddleLeft, FontStyle.Normal); Place(d2.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(190f, y), new Vector2(330f, 50f), new Vector2(0f, 0.5f));
        }
        var tip = NewText("Tip", win, "THE OPERATOR WILL GUIDE YOU.  KEEP AN EYE ON THE MAP.", 24, new Color(1f, 0.86f, 0.42f, 0.9f), TextAnchor.MiddleCenter, FontStyle.Normal);
        Place(tip.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -215f), new Vector2(900f, 40f));
        back = MakeButton(win, "BackButton", "BACK", 300, 64, sp, true, new Vector2(0.5f, 0.5f), new Vector2(0f, -285f), 36, false);
        back.slideFrom = new Vector2(0f, -30f);

        var panel = root.gameObject.AddComponent<MenuPanel>(); panel.group = group; panel.window = win;
        root.gameObject.SetActive(false);
        return panel;
    }

    // =========================================================================================================== the story scene
    static void BuildStoryScene(Sprites sp, System.Text.StringBuilder report)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Camera", typeof(Camera), typeof(AudioListener)); camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.cullingMask = 0;
        RenderSettings.skybox = null; RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = Color.black;

        var canvas = NewCanvas("StoryCanvas", 10);
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        // the decoration of the text version (hidden while the intro video plays)
        var decor = NewRect("Decor", canvas.transform); Stretch(decor);
        var glow = NewImage("Glow", decor, sp.radial, new Color(1f, 0.7f, 0.3f, 0.16f));
        Place(glow.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1700f, 900f));
        var vig = NewImage("Vignette", decor, sp.vignette, new Color(0, 0, 0, 0.7f)); Stretch(vig.rectTransform);

        // thin amber lines + diamonds above and below the text
        foreach (float y in new[] { 150f, -150f })
        {
            var ln = NewImage("Line", decor, null, new Color(1f, 0.78f, 0.22f, 0.45f)); Place(ln.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(900f, 2f));
            var dm = NewImage("Diamond", decor, null, Amber); Place(dm.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(14f, 14f)); dm.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
        }

        // the intro video (GAME.mp4 in StreamingAssets), fitted into the screen
        var vidGo = new GameObject("IntroVideo", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        vidGo.transform.SetParent(canvas.transform, false);
        var vidImg = vidGo.GetComponent<RawImage>(); vidImg.color = new Color(1f, 1f, 1f, 0f); vidImg.raycastTarget = false;
        var vrt = vidGo.GetComponent<RectTransform>(); Stretch(vrt);
        var fitter = vidGo.GetComponent<AspectRatioFitter>(); fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent; fitter.aspectRatio = 848f / 480f;

        var body = NewRect("Body", canvas.transform); Place(body, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 260f));
        var bodyGroup = body.gameObject.AddComponent<CanvasGroup>();
        var txt = NewText("Text", body, "", 54, new Color(0.95f, 0.95f, 0.9f), TextAnchor.MiddleCenter, FontStyle.Bold);
        Stretch(txt.rectTransform); txt.horizontalOverflow = HorizontalWrapMode.Wrap; txt.verticalOverflow = VerticalWrapMode.Overflow;
        txt.gameObject.GetComponent<Outline>().effectDistance = new Vector2(2f, -2f);

        // the skip button (bottom right)
        var skip = MakeButton(canvas.transform, "SkipButton", "SKIP INTRO", 330, 62, sp, false, new Vector2(1f, 0f), new Vector2(-230f, 80f), 32, true);
        skip.slideFrom = new Vector2(80f, 0f);

        // (no "press space" hint: only the SKIP INTRO button is shown)

        var fade = NewImage("Fade", canvas.transform, null, Color.black); Stretch(fade.rectTransform);

        var audio = new GameObject("Audio");
        AudioSource Src(string n, bool loop) { var g = new GameObject(n, typeof(AudioSource)); g.transform.SetParent(audio.transform, false); var s = g.GetComponent<AudioSource>(); s.playOnAwake = false; s.loop = loop; s.spatialBlend = 0f; return s; }
        var music = Src("Music", true); music.clip = Clip("Story_Ambience");
        var sfx = Src("SFX", false);

        var vaGo = new GameObject("VideoAudio", typeof(AudioSource)); vaGo.transform.SetParent(audio.transform, false);
        var va = vaGo.GetComponent<AudioSource>(); va.playOnAwake = false; va.spatialBlend = 0f;
        var go = new GameObject("StoryDirector");
        var vp = go.AddComponent<VideoPlayer>(); vp.playOnAwake = false;
        var si = go.AddComponent<StoryIntro>();
        si.video = vp; si.videoImage = vidImg; si.videoAudio = va; si.decor = decor.gameObject; si.videoFile = "GAME.mp4";
        si.bodyText = txt; si.bodyGroup = bodyGroup; si.skipButton = skip; si.glow = glow; si.fade = fade; si.music = music; si.sfx = sfx;
        si.hoverClip = Clip("UI_Hover"); si.clickClip = Clip("UI_Click"); si.nextScene = "SunTemple_Night";

        AssetDatabase.DeleteAsset(StoryScene);
        EditorSceneManager.SaveScene(scene, StoryScene);
        report.AppendLine("StoryScene built: " + StoryScene);
    }

    // =========================================================================================================== build settings
    static void SetBuildSettings()
    {
        var list = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(MenuScene, true),
            new EditorBuildSettingsScene(StoryScene, true),
            new EditorBuildSettingsScene(GameScene, true),
        };
        foreach (var s in EditorBuildSettings.scenes)
            if (s.path != MenuScene && s.path != StoryScene && s.path != GameScene && File.Exists(s.path)) list.Add(s);
        EditorBuildSettings.scenes = list.ToArray();
    }
}
