using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using GAMECON.Mission;
using GAMECON.Voice;

/// <summary>
/// The operator's dialogue box: a cream speech bubble at the bottom of the screen with the typed text "Operator - ..."
/// and the operator's portrait (Assets/UI/Operator/Operator.png) beside it. Runs as part of
/// "Build Minimap + Rescue Mission", or on its own with GAMECON > Build Operator Dialogue Box.
/// </summary>
public static class DialogueBuilder
{
    const string NightScene = "Assets/Scenes/SunTemple_Night.unity";
    const string PortraitPath = "Assets/UI/Operator/Operator.png";
    const string SamrahPortraitPath = "Assets/UI/Operator/Samrah.png";

    [MenuItem("GAMECON/Build Operator Dialogue Box")]
    public static void Build()
    {
        if (!File.Exists(NightScene)) { EditorUtility.DisplayDialog("Error", "SunTemple_Night.unity not found.", "OK"); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.OpenScene(NightScene, OpenSceneMode.Single);
        bool ok = Apply(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorUtility.DisplayDialog("Dialogue box", ok ? "The operator's dialogue box is ready." : "MissionCanvas was not found. Run 'Build Minimap + Rescue Mission' first.", "OK");
    }

    /// <summary>(Automation) rebuilds the dialogue box in the game scene without any dialog.</summary>
    public static string ApplyToNight()
    {
        Scene scene = EditorSceneManager.OpenScene(NightScene, OpenSceneMode.Single);
        bool ok = Apply(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return ok ? "dialogue box rebuilt" : "MissionCanvas not found";
    }

    public static bool Apply(Scene scene)
    {
        var canvas = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).FirstOrDefault(c => c.name == "MissionCanvas");
        if (canvas == null) return false;

        // portrait sprite
        AssetDatabase.ImportAsset(PortraitPath, ImportAssetOptions.ForceUpdate);
        var imp = AssetImporter.GetAtPath(PortraitPath) as TextureImporter;
        if (imp != null)
        {
            imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.filterMode = FilterMode.Bilinear; imp.maxTextureSize = 1024;
            imp.SaveAndReimport();
        }
        Sprite portraitSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PortraitPath);
        Sprite rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Environment/Blood/Icon_RoundedRect.png");
        Sprite square = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Minimap/Icon_Square.png");
        if (portraitSprite == null) Debug.LogWarning("[Dialogue] Operator portrait not found at " + PortraitPath);

        var old = canvas.transform.Find("OperatorDialogue");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // ---------- root ----------
        var rootGo = new GameObject("OperatorDialogue", typeof(RectTransform), typeof(CanvasGroup));
        rootGo.transform.SetParent(canvas.transform, false);
        var root = rootGo.GetComponent<RectTransform>();
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0f);
        root.anchoredPosition = new Vector2(-190f, 28f);        // shifted left so it never covers the ammo panel (bottom right)
        root.sizeDelta = new Vector2(1240f, 330f);
        var group = rootGo.GetComponent<CanvasGroup>(); group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;

        Color dark = new Color(1f, 0.78f, 0.22f, 1f);                 // amber border (the HUD accent colour)
        Color cream = new Color(0.07f, 0.09f, 0.075f, 0.95f);         // dark olive / charcoal panel (night-mission theme)
        const float bubbleX = 225f, bubbleY = 8f, bubbleW = 1000f, bubbleH = 150f;

        // speech-bubble tail pointing at the portrait (two rotated squares: outline + fill)
        var tailOut = Img("TailOutline", root, square, dark, new Vector2(bubbleX - 2f, bubbleY + bubbleH * 0.5f), new Vector2(46f, 46f));
        tailOut.rectTransform.localRotation = Quaternion.Euler(0, 0, 45f);
        var tailFill = Img("TailFill", root, square, cream, new Vector2(bubbleX - 1f, bubbleY + bubbleH * 0.5f), new Vector2(36f, 36f));
        tailFill.rectTransform.localRotation = Quaternion.Euler(0, 0, 45f);

        // bubble: dark outline + cream fill
        var bOut = Img("BubbleOutline", root, rounded != null ? rounded : square, dark, new Vector2(bubbleX, bubbleY), new Vector2(bubbleW, bubbleH));
        var bFill = Img("BubbleFill", root, rounded != null ? rounded : square, cream, new Vector2(bubbleX + 4f, bubbleY + 4f), new Vector2(bubbleW - 8f, bubbleH - 8f));
        if (rounded != null) { bOut.type = Image.Type.Sliced; bFill.type = Image.Type.Sliced; }

        // text
        var tgo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        tgo.transform.SetParent(root, false);
        var text = tgo.GetComponent<Text>();
        text.font = font; text.fontSize = 33; text.color = new Color(0.93f, 0.94f, 0.87f); text.alignment = TextAnchor.MiddleLeft;
        text.supportRichText = true; text.raycastTarget = false; text.text = "";
        text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
        var trt = tgo.GetComponent<RectTransform>();
        trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0f, 0f);
        trt.anchoredPosition = new Vector2(bubbleX + 78f, bubbleY + 10f); trt.sizeDelta = new Vector2(bubbleW - 78f - 34f, bubbleH - 20f);

        // operator portrait (on top, overlapping the left end of the bubble and rising above it)
        var port = Img("OperatorPortrait", root, portraitSprite, Color.white, new Vector2(0f, 0f), new Vector2(290f, 317f));
        port.preserveAspect = true;

        // wire it up
        var box = rootGo.AddComponent<DialogueBox>();
        box.root = root; box.group = group; box.portrait = port.rectTransform; box.body = text;
        box.operatorPortrait = portraitSprite;
        AssetDatabase.ImportAsset(SamrahPortraitPath, ImportAssetOptions.ForceUpdate);
        var simp = AssetImporter.GetAtPath(SamrahPortraitPath) as TextureImporter;
        if (simp != null)
        {
            simp.textureType = TextureImporterType.Sprite; simp.spriteImportMode = SpriteImportMode.Single;
            simp.alphaIsTransparency = true; simp.mipmapEnabled = false; simp.filterMode = FilterMode.Bilinear; simp.maxTextureSize = 1024;
            simp.SaveAndReimport();
        }
        box.samrahPortrait = AssetDatabase.LoadAssetAtPath<Sprite>(SamrahPortraitPath);
        if (box.samrahPortrait == null) Debug.LogWarning("[Dialogue] Samrah's portrait not found at " + SamrahPortraitPath);

        var mm = Object.FindObjectOfType<MissionManager>();
        if (mm != null)
        {
            mm.dialogue = box;
            if (mm.voiceText != null) mm.voiceText.gameObject.SetActive(false);       // the old plain subtitle is replaced
        }
        else Debug.LogWarning("[Dialogue] MissionManager not found - run 'Build Minimap + Rescue Mission' first.");
        return true;
    }

    static Image Img(string name, Transform parent, Sprite sprite, Color color, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite; img.color = color; img.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return img;
    }
}
