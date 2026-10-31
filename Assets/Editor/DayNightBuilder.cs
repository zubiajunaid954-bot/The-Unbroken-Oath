using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using GAMECON.Weather;

/// <summary>
/// Adds the morning / night cycle to SunTemple_Night: the mission starts in a clear morning (no rain), and after the first
/// rescue the game turns into the rainy night you already have. Builds the DayNightController and the "MORNING / ENTERING NIGHT"
/// title text. Runs as part of "Build Minimap + Rescue Mission", or on its own with GAMECON > Build Morning/Night Cycle.
/// </summary>
public static class DayNightBuilder
{
    const string NightScene = "Assets/Scenes/SunTemple_Night.unity";

    [MenuItem("GAMECON/Build Morning-Night Cycle")]
    public static void Build()
    {
        if (!File.Exists(NightScene)) { EditorUtility.DisplayDialog("Error", "SunTemple_Night.unity not found.", "OK"); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.OpenScene(NightScene, OpenSceneMode.Single);
        string msg = Apply(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorUtility.DisplayDialog("Morning / night cycle", msg, "OK");
    }

    /// <returns>a message for the user</returns>
    public static string Apply(Scene scene)
    {
        AssetDatabase.Refresh();
        var all = scene.GetRootGameObjects();

        var storm = all.SelectMany(r => r.GetComponentsInChildren<StormController>(true)).FirstOrDefault();
        if (storm == null) return "The rain / thunder system (WeatherSystem) was not found.\nRun 'Build Night + Rain Scene' first.";

        var canvas = all.SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).FirstOrDefault(c => c.name == "MissionCanvas");

        // the directional light that is the sun by day and the moon by night
        Light sun = RenderSettings.sun;
        if (sun == null) sun = all.SelectMany(r => r.GetComponentsInChildren<Light>(true)).FirstOrDefault(l => l.type == LightType.Directional && l.enabled);
        if (sun != null) { sun.name = "Sun-Moon Directional"; RenderSettings.sun = sun; }

        var nightSky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/Night/NightSky.mat");
        var music = storm.transform.Find("BackgroundMusic");
        var ambience = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Ambience/Day_Ambience.wav");

        // ---------- the title text ----------
        Text banner = null;
        if (canvas != null)
        {
            var old = canvas.transform.Find("TimeBanner");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("TimeBanner", typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(canvas.transform, false);
            banner = go.GetComponent<Text>();
            banner.font = font; banner.fontSize = 28; banner.color = new Color(1f, 0.93f, 0.7f, 0f); banner.alignment = TextAnchor.MiddleCenter;
            banner.supportRichText = true; banner.raycastTarget = false; banner.text = "";
            banner.horizontalOverflow = HorizontalWrapMode.Overflow; banner.verticalOverflow = VerticalWrapMode.Overflow;
            var o = go.GetComponent<Outline>(); o.effectColor = new Color(0, 0, 0, 0.95f); o.effectDistance = new Vector2(3f, -3f);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -140f); rt.sizeDelta = new Vector2(1400f, 200f);
            go.transform.SetSiblingIndex(Mathf.Min(canvas.transform.childCount - 1, 3));
        }

        // ---------- the controller ----------
        Transform parent = storm.transform.parent != null ? storm.transform.parent : storm.transform;
        var oldSys = parent.Find("DayNightSystem");
        if (oldSys != null) Object.DestroyImmediate(oldSys.gameObject);
        var sys = new GameObject("DayNightSystem");
        sys.transform.SetParent(parent, false);
        var dn = sys.AddComponent<DayNightController>();
        dn.day = DayNightController.DefaultDay();
        dn.night = DayNightController.DefaultNight();
        dn.sun = sun; dn.nightSkyMaterial = nightSky; dn.storm = storm;
        dn.music = music != null ? music.GetComponent<AudioSource>() : null;
        dn.dayAmbience = ambience; dn.timeBanner = banner;
        dn.transitionSeconds = 9f;

        string warn = "";
        if (nightSky == null) warn += "\n(NightSky material not found: the sky will not change.)";
        if (canvas == null) warn += "\n(MissionCanvas not found: the 'MORNING / ENTERING NIGHT' text is missing - run Build Minimap + Rescue Mission.)";
        return "The mission now starts in a clear MORNING (no rain). After the first teammate is rescued the game changes to the rainy NIGHT, with the text 'ENTERING NIGHT'." + warn;
    }
}
