using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using scgFullBodyController;

/// <summary>
/// The player controller can vault over objects tagged "vaultObject" (press Space while touching them).
/// This tags every low, thin, visible obstacle (fences, low walls, rails, curbs) in the scene so they can be crossed.
/// Invisible boundary boxes, tall walls, buildings and terrain are left alone.
/// </summary>
public static class FenceVaultTagger
{
    const string NightScene = "Assets/Scenes/SunTemple_Night.unity";
    const string DayScene = "Assets/Scenes/SunTemple_Game.unity";

    [MenuItem("GAMECON/Make Fences Vaultable (Space)")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        int total = 0;
        foreach (string path in new[] { NightScene, DayScene })
        {
            if (!File.Exists(path)) continue;
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            int n = Tag(scene);
            total += n;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Vault] " + path + ": tagged " + n + " fences/low walls.");
        }
        // reopen the night scene
        if (File.Exists(NightScene)) EditorSceneManager.OpenScene(NightScene, OpenSceneMode.Single);
        EditorUtility.DisplayDialog("Fences vaultable",
            "Tagged " + total + " low obstacles as 'vaultObject'.\nWalk into a fence and press Space to vault over it.", "OK");
    }

    static int Tag(Scene scene)
    {
        int count = 0;
        var seen = new HashSet<GameObject>();
        foreach (var col in Object.FindObjectsOfType<Collider>(true))
        {
            if (col.gameObject.scene != scene || col.isTrigger || col is TerrainCollider) continue;
            if (col.GetComponentInParent<AiController>() != null || col.GetComponentInParent<ThirdPersonUserControl>() != null) continue;
            if (col.attachedRigidbody != null) continue;
            // must be something you can see (not an invisible boundary box)
            if (col.GetComponent<Renderer>() == null && col.GetComponentInChildren<Renderer>() == null) continue;

            Bounds b = col.bounds;
            float h = b.size.y, wx = b.size.x, wz = b.size.z;
            float thin = Mathf.Min(wx, wz), longSide = Mathf.Max(wx, wz);
            // low (0.35 - 1.7 m), thin (<= 1.6 m) and not just a small prop (longer than 1 m)
            if (h < 0.35f || h > 1.7f || thin > 1.6f || longSide < 1.0f) continue;
            if (col.gameObject.CompareTag("vaultObject") || !seen.Add(col.gameObject)) continue;

            col.gameObject.tag = "vaultObject";
            count++;
        }
        return count;
    }
}
