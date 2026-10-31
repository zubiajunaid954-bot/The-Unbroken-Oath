using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using GAMECON.Combat;

/// <summary>Builds the ammo panel (bottom right, tactical amber / olive style). Part of the master build, or GAMECON > Build Ammo HUD.</summary>
public static class AmmoHudBuilder
{
    const string NightScene = "Assets/Scenes/SunTemple_Night.unity";

    [MenuItem("GAMECON/Build Ammo HUD")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.OpenScene(NightScene, OpenSceneMode.Single);
        bool ok = Apply(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorUtility.DisplayDialog("Ammo HUD", ok ? "The ammo panel is built (bottom right)." : "MissionCanvas not found. Run 'Build Minimap + Rescue Mission' first.", "OK");
    }

    public static bool Apply(Scene scene)
    {
        var canvas = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).FirstOrDefault(c => c.name == "MissionCanvas");
        if (canvas == null) return false;
        var old = canvas.transform.Find("AmmoPanel");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Sprite rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Environment/Blood/Icon_RoundedRect.png");
        Color amber = new Color(1f, 0.78f, 0.22f, 1f);
        Color panel = new Color(0.07f, 0.09f, 0.075f, 0.93f);

        var root = NewRect("AmmoPanel", canvas.transform);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(1f, 0f);
        root.anchoredPosition = new Vector2(-40f, 36f);
        root.sizeDelta = new Vector2(380f, 136f);

        var frame = NewImage("Frame", root, rounded, amber); Stretch(frame.rectTransform); frame.type = Image.Type.Sliced;
        var back = NewImage("Back", root, rounded, panel); Stretch(back.rectTransform); back.type = Image.Type.Sliced; Inset(back.rectTransform, 3f);
        var lowGlow = NewImage("LowGlow", root, rounded, new Color(0.95f, 0.25f, 0.15f, 0f)); Stretch(lowGlow.rectTransform); lowGlow.type = Image.Type.Sliced; Inset(lowGlow.rectTransform, 3f);
        var edge = NewImage("InnerEdge", root, rounded, new Color(1f, 0.78f, 0.22f, 0.18f)); Stretch(edge.rectTransform); edge.type = Image.Type.Sliced; Inset(edge.rectTransform, 8f);
        var edgeIn = NewImage("InnerEdgeFill", root, rounded, panel); Stretch(edgeIn.rectTransform); edgeIn.type = Image.Type.Sliced; Inset(edgeIn.rectTransform, 9f);

        // accent: short amber bars on two corners
        Accent(root, new Vector2(0f, 1f), new Vector2(30f, 3f), new Vector2(18f, -2f), amber);
        Accent(root, new Vector2(1f, 0f), new Vector2(30f, 3f), new Vector2(-18f, 2f), amber);

        // left: weapon icon, name, fire mode
        var icon = NewImage("Icon", root, null, amber);
        Place(icon.rectTransform, new Vector2(0f, 1f), new Vector2(22f, -14f), new Vector2(110f, 40f)); icon.preserveAspect = true;
        var wname = NewText("WeaponName", root, font, "RIFLE", 20, Color.white, TextAnchor.MiddleLeft);
        Place(wname.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -56f), new Vector2(100f, 22f));
        var mode = NewText("Mode", root, font, "AUTO", 14, new Color(1f, 0.78f, 0.22f), TextAnchor.MiddleLeft);
        Place(mode.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -78f), new Vector2(100f, 18f));

        // divider
        var div = NewImage("Divider", root, null, new Color(1f, 0.78f, 0.22f, 0.35f));
        Place(div.rectTransform, new Vector2(0f, 1f), new Vector2(140f, -14f), new Vector2(2f, 70f));

        // big magazine count + reserve
        var mag = NewText("Mag", root, font, "30", 76, Color.white, TextAnchor.MiddleRight);
        Place(mag.rectTransform, new Vector2(1f, 1f), new Vector2(-122f, -14f), new Vector2(120f, 74f));
        mag.rectTransform.pivot = new Vector2(1f, 1f);
        var res = NewText("Reserve", root, font, "/ 90", 30, new Color(1f, 0.85f, 0.45f, 0.85f), TextAnchor.MiddleLeft);
        Place(res.rectTransform, new Vector2(1f, 1f), new Vector2(-116f, -44f), new Vector2(104f, 36f));
        res.rectTransform.pivot = new Vector2(0f, 1f);
        res.rectTransform.anchoredPosition = new Vector2(-116f, -30f);

        // status (LOW AMMO / RELOAD / RELOADING)
        var status = NewText("Status", root, font, "", 17, amber, TextAnchor.MiddleRight);
        Place(status.rectTransform, new Vector2(1f, 1f), new Vector2(-20f, -80f), new Vector2(200f, 18f));
        status.rectTransform.pivot = new Vector2(1f, 1f);

        // reload bar (thin, under the status)
        var rbBack = NewImage("ReloadBack", root, null, new Color(1f, 1f, 1f, 0.1f));
        Place(rbBack.rectTransform, new Vector2(0f, 0f), new Vector2(24f, 36f), new Vector2(332f, 3f));
        var rb = NewImage("ReloadFill", rbBack.transform, null, amber); Stretch(rb.rectTransform);
        rb.type = Image.Type.Filled; rb.fillMethod = Image.FillMethod.Horizontal; rb.fillOrigin = 0; rb.fillAmount = 0f;

        // the cartridge row
        var row = NewRect("Cartridges", root);
        row.anchorMin = row.anchorMax = new Vector2(0f, 0f); row.pivot = new Vector2(0f, 0.5f);
        row.anchoredPosition = new Vector2(24f, 20f); row.sizeDelta = new Vector2(332f, 18f);
        var pills = new Image[30];
        for (int i = 0; i < pills.Length; i++)
        {
            var p = NewImage("Round" + (i + 1), row, null, amber);
            p.rectTransform.anchorMin = p.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            p.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            pills[i] = p;
        }

        var hud = root.gameObject.AddComponent<AmmoHud>();
        hud.root = root; hud.frame = frame; hud.icon = icon; hud.reloadFill = rb; hud.lowGlow = lowGlow;
        hud.weaponName = wname; hud.mode = mode; hud.mag = mag; hud.reserve = res; hud.status = status; hud.pills = pills;
        hud.amber = amber;
        return true;
    }

    static void Accent(Transform parent, Vector2 anchor, Vector2 size, Vector2 pos, Color c)
    {
        var a = NewImage("Accent", parent, null, c);
        a.rectTransform.anchorMin = a.rectTransform.anchorMax = a.rectTransform.pivot = anchor;
        a.rectTransform.anchoredPosition = pos; a.rectTransform.sizeDelta = size;
    }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor; rt.anchoredPosition = pos; rt.sizeDelta = size;
    }

    static void Inset(RectTransform rt, float v) { rt.offsetMin = new Vector2(v, v); rt.offsetMax = new Vector2(-v, -v); }

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
        t.font = font; t.text = text; t.fontSize = size; t.color = color; t.alignment = anchor; t.raycastTarget = false; t.fontStyle = FontStyle.Bold;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        var o = rt.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, 0.9f); o.effectDistance = new Vector2(1.5f, -1.5f);
        return t;
    }

    static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }
}
