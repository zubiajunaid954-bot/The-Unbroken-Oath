using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds Assets/Scenes/SunTemple_Game.unity: the Sun Temple environment with the
/// FullBodyController player, HUD, enemies and scene controller carried over from
/// Combined_Scene (the Flooded Grounds city). Combined_Scene itself is never modified.
/// </summary>
public static class SunTempleSceneBuilder
{
    const string SunTempleScenePath = "Assets/Sun_Temple/Scenes/DemoScene.unity";
    const string SunTempleProfilePath = "Assets/Sun_Temple/Scenes/DemoScene_PostProcessProfile.asset";
    const string OldGameScenePath = "Assets/Scenes/Combined_Scene.unity";
    const string NewScenePath = "Assets/Scenes/SunTemple_Game.unity";

    [MenuItem("GAMECON/Build Sun Temple Game Scene")]
    public static void Build()
    {
        if (!File.Exists(SunTempleScenePath) || !File.Exists(OldGameScenePath))
        {
            EditorUtility.DisplayDialog("Error", "Sun Temple DemoScene or Combined_Scene not found.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // 1. New scene = copy of the Sun Temple demo scene (original stays untouched).
        if (File.Exists(NewScenePath)) AssetDatabase.DeleteAsset(NewScenePath);
        AssetDatabase.CopyAsset(SunTempleScenePath, NewScenePath);
        AssetDatabase.Refresh();
        Scene scene = EditorSceneManager.OpenScene(NewScenePath, OpenSceneMode.Single);

        // 2. Pull the gameplay objects out of the old game scene.
        Scene old = EditorSceneManager.OpenScene(OldGameScenePath, OpenSceneMode.Additive);

        // Sun Temple's own demo player: remember where it stands, then disable it.
        Vector3 spawn = new Vector3(0, 2, 0);
        Quaternion spawnRot = Quaternion.identity;
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name == "FpsController")
            {
                spawn = go.transform.position + Vector3.up * 0.2f;
                spawnRot = Quaternion.Euler(0, go.transform.eulerAngles.y, 0);
                go.SetActive(false);
                go.name = "FpsController (Sun Temple demo - disabled)";
            }
            else if (go.name == "Canvas" || go.name == "EventSystem")
            {
                // Demo UI/input; the game brings its own HUD and EventSystem.
                go.SetActive(false);
                go.name = go.name + " (Sun Temple demo - disabled)";
            }
        }

        var moved = new List<GameObject>();
        bool movedEventSystem = false;
        foreach (GameObject go in old.GetRootGameObjects())
        {
            if (!ShouldCarryOver(go, ref movedEventSystem)) continue;
            SceneManager.MoveGameObjectToScene(go, scene);
            moved.Add(go);
        }
        EditorSceneManager.CloseScene(old, true);

        // 3. Player placement + wiring required by the Sun Temple scripts.
        GameObject player = moved.FirstOrDefault(g => g.name.Contains("FullBodyController"));
        if (player != null)
        {
            player.transform.SetPositionAndRotation(spawn, spawnRot);
            SetupPlayerCamera(player);
            Physics.SyncTransforms();
        }
        else Debug.LogWarning("[SunTempleBuilder] FullBodyController not found in Combined_Scene.");

        // 4. Enemies: re-place around the player on the Sun Temple ground.
        var ais = moved.SelectMany(g => g.GetComponentsInChildren<scgFullBodyController.AiController>(true)).ToList();
        PlaceEnemies(ais, spawn);

        // 5. Organize hierarchy.
        Organize(scene, moved, player);

        // 6. NavMesh so enemies can walk, then save + build settings.
        TryBakeNavMesh();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, NewScenePath);
        AddToBuildSettings(NewScenePath);

        Debug.Log("[SunTempleBuilder] Done: " + NewScenePath + " (player at " + spawn + ", " + ais.Count + " enemies)");
        EditorUtility.DisplayDialog("Sun Temple scene ready",
            "Created " + NewScenePath + "\n\nPlayer, HUD, enemies and scene controller moved in.\nCombined_Scene (Flooded Grounds) is untouched as a backup.\n\nPress Play to test.", "OK");
    }

    static bool ShouldCarryOver(GameObject go, ref bool movedEventSystem)
    {
        string src = "";
        if (PrefabUtility.IsAnyPrefabInstanceRoot(go))
            src = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(go));
        if (src.Contains("scgFullBodyController")) return true;
        if (src.Contains("Flooded_Grounds")) return false;

        switch (go.name)
        {
            case "sceneController":
            case "FPSDisplay":
            case "Enemy_Soldiers_Squad":
                return true;
            case "EventSystem":
                if (movedEventSystem) return false;
                movedEventSystem = true;
                return true;
            case "Canvas":
                // Only the game HUD canvas (has the health/bullet text), not Flooded Grounds' own.
                return go.GetComponentsInChildren<Transform>(true).Any(t => t.name == "uiHealth" || t.name == "uiBullets" || t.name == "hud")
                       || go.GetComponentInChildren<scgFullBodyController.hudController>(true) != null;
        }
        if (go.GetComponent<scgFullBodyController.hudController>() != null) return true;
        return false;
    }

    static void SetupPlayerCamera(GameObject player)
    {
        if (player.tag == "Untagged") player.tag = "Player"; // Sun Temple exposure/culling/doors look for "Player"

        Camera cam = player.GetComponentInChildren<Camera>(true);
        if (cam == null) { Debug.LogWarning("[SunTempleBuilder] No camera under the player."); return; }
        cam.gameObject.tag = "MainCamera";
        cam.renderingPath = RenderingPath.DeferredShading; // recommended by the Sun Temple readme
        cam.allowHDR = true;

        // Same post-processing look as the Sun Temple demo camera.
        Type ppType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("UnityEngine.PostProcessing.PostProcessingBehaviour"))
            .FirstOrDefault(t => t != null);
        var profile = AssetDatabase.LoadAssetAtPath<ScriptableObject>(SunTempleProfilePath);
        if (ppType != null && profile != null)
        {
            var pp = cam.gameObject.GetComponent(ppType) ?? cam.gameObject.AddComponent(ppType);
            var so = new SerializedObject(pp);
            var prop = so.FindProperty("profile");
            if (prop != null) { prop.objectReferenceValue = profile; so.ApplyModifiedPropertiesWithoutUndo(); }
            else Debug.LogWarning("[SunTempleBuilder] Could not find profile field on PostProcessingBehaviour.");
        }
    }

    static void PlaceEnemies(List<scgFullBodyController.AiController> ais, Vector3 center)
    {
        // Tactical ring 12-30 m around the player; snapped to ground and NavMesh when available.
        for (int i = 0; i < ais.Count; i++)
        {
            float ang = i * (360f / Mathf.Max(1, ais.Count)) * Mathf.Deg2Rad;
            float dist = 12f + (i % 3) * 8f;
            Vector3 p = center + new Vector3(Mathf.Cos(ang) * dist, 0, Mathf.Sin(ang) * dist);
            if (Physics.Raycast(p + Vector3.up * 60f, Vector3.down, out RaycastHit hit, 200f))
                p.y = hit.point.y + 0.1f;
            var t = ais[i].transform;
            t.position = p;
            t.LookAt(new Vector3(center.x, p.y, center.z));
            ais[i].sightRange = Mathf.Max(ais[i].sightRange, 50f);
            ais[i].attackRange = Mathf.Max(ais[i].attackRange, 35f);
        }
    }

    // ---------- Hierarchy organisation ----------
    static void Organize(Scene scene, List<GameObject> moved, GameObject player)
    {
        Transform env = Group("====== ENVIRONMENT ======");
        Transform terrain = Group("Terrain & Nature", env);
        Transform arch = Group("Architecture", env);
        Transform props = Group("Props & Decals", env);
        Transform collision = Group("Collision", env);
        Transform light = Group("====== LIGHTING ======");
        Transform sys = Group("====== SYSTEMS (Exposure / Culling / Occlusion) ======");
        Transform playerG = Group("====== PLAYER ======");
        Transform enemies = Group("====== ENEMIES ======");
        Transform ui = Group("====== UI ======");
        Transform game = Group("====== GAME LOGIC ======");

        var movedSet = new HashSet<GameObject>(moved);
        foreach (GameObject go in scene.GetRootGameObjects().ToArray())
        {
            if (go.transform.parent != null) continue;
            string n = go.name;
            if (n.StartsWith("======")) continue;

            Transform target;
            if (movedSet.Contains(go))
            {
                if (go == player) target = playerG;
                else if (go.GetComponentInChildren<scgFullBodyController.AiController>(true) != null) target = enemies;
                else if (go.GetComponent<Canvas>() != null || n == "EventSystem" || n == "FPSDisplay" || go.GetComponent<scgFullBodyController.hudController>() != null) target = ui;
                else target = game;
            }
            else if (n.Contains("(Sun Temple demo - disabled)")) target = game;
            else if (n == "Terrain" || n == "WindZone" || n.StartsWith("Natural") || n.StartsWith("Nat_")) target = terrain;
            else if (n.StartsWith("Sun") || n.StartsWith("Point light") || n.StartsWith("Reflection Probe") || n == "bouncebleed" || n == "Light Probes") target = light;
            else if (n.StartsWith("CollisionBox")) target = collision;
            else if (n.StartsWith("ExposureArea") || n.StartsWith("InteriorOcclusionArea") || n.StartsWith("DetailArea") || n == "Occlusion Area") target = sys;
            else if (n.StartsWith("Dec_") || n.StartsWith("Prop_") || n.StartsWith("Props")) target = props;
            else if (n.StartsWith("Bld_") || n.StartsWith("Struct_") || n.StartsWith("Cube") || n == "Elparento_dummy") target = arch;
            else target = env;

            go.transform.SetParent(target, true);
        }

        // Tidy sort order of the top-level groups (leave other roots after).
        string[] order = { "====== ENVIRONMENT ======", "====== LIGHTING ======", "====== SYSTEMS (Exposure / Culling / Occlusion) ======",
                           "====== PLAYER ======", "====== ENEMIES ======", "====== UI ======", "====== GAME LOGIC ======" };
        for (int i = 0; i < order.Length; i++)
        {
            var g = scene.GetRootGameObjects().FirstOrDefault(r => r.name == order[i]);
            if (g != null) g.transform.SetSiblingIndex(i);
        }
        // Remove empty groups
        foreach (var g in new[] { terrain, arch, props, collision, light, sys, playerG, enemies, ui, game })
            if (g != null && g.childCount == 0) UnityEngine.Object.DestroyImmediate(g.gameObject);
    }

    static Transform Group(string name, Transform parent = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero; // identity so children keep world positions
        return go.transform;
    }

    // ---------- NavMesh + build settings ----------
    static void TryBakeNavMesh()
    {
        try
        {
#pragma warning disable CS0618
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
#pragma warning restore CS0618
            Debug.Log("[SunTempleBuilder] NavMesh baked.");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[SunTempleBuilder] NavMesh bake failed - bake manually via Window > AI > Navigation. " + e.Message);
        }
    }

    static void AddToBuildSettings(string path)
    {
        var list = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
        list.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = list.ToArray();
    }
}
