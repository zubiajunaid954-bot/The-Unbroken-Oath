using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GAMECON.Mission;

/// <summary>
/// Connects Samrah's game-over cut scene: gives the CutsceneDirector the rigged Samrah (Assets/Prefabs/Rigged/Samrah_Rigged.prefab,
/// made by GAMECON > Rig Teammates). Part of the master build, or GAMECON > Build Death Scene.
/// </summary>
public static class DeathCutsceneBuilder
{
    const string NightScene = "Assets/Scenes/SunTemple_Night.unity";
    const string RiggedSamrah = "Assets/Prefabs/Rigged/Samrah_Rigged.prefab";

    [MenuItem("GAMECON/Build Death Scene (Samrah game over)")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.OpenScene(NightScene, OpenSceneMode.Single);
        string msg = Apply(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorUtility.DisplayDialog("Death scene", msg, "OK");
    }

    public static string Apply(Scene scene)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RiggedSamrah);
        var mm = Object.FindObjectOfType<MissionManager>();
        if (mm == null) return "MissionManager not found. Run 'Build Minimap + Rescue Mission' first.";
        var cut = mm.GetComponent<CutsceneDirector>();
        if (cut == null) cut = mm.gameObject.AddComponent<CutsceneDirector>();
        cut.samrahPrefab = prefab;
        cut.gunPrefab = BuildGunProp();

        // the victory scene: the four rigged heroes (Samrah, Duaa, Zubia, Maha)
        var heroes = new GameObject[4];
        string[] who = { "Samrah", "Duaa", "Zubia", "Maha" };
        string missing = "";
        for (int i = 0; i < 4; i++)
        {
            heroes[i] = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Rigged/" + who[i] + "_Rigged.prefab");
            if (heroes[i] == null) missing += " " + who[i];
        }
        cut.heroPrefabs = heroes;
        if (missing != "") Debug.LogWarning("[Victory scene] not rigged yet:" + missing + " - run GAMECON > Rig Teammates first.");
        EditorUtility.SetDirty(cut);
        if (prefab == null)
        {
            Debug.LogWarning("[Death scene] Samrah is not rigged yet - run GAMECON > Rig Teammates first.");
            return "Samrah is not rigged yet.\nRun 'GAMECON > Rig Teammates (Duaa, Zubia, Maha) + Samrah' first, then this again.";
        }
        return "Samrah's death scene is connected.\nWhen your life reaches 0 the cut scene plays, then the MISSION FAILED screen.";
    }

    // ---------------------------------------------------------------- the rifle that falls from her hand
    const string RiflePath = "Assets/scgFullBodyController/Models/Rifle/Prefabs/rifle.prefab";
    const string GunPropPath = "Assets/Prefabs/Rigged/Gun_DeathProp.prefab";

    /// <summary>Makes a clean copy of the M4A1 with its barrel along +Z, its top along +Y, centred, 0.85 m long.</summary>
    static GameObject BuildGunProp()
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(RiflePath);
        if (src == null) { Debug.LogWarning("[Death scene] rifle prefab not found: " + RiflePath); return null; }

        foreach (var mf in src.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mf.sharedMesh)) as ModelImporter;
            if (imp != null && !imp.isReadable) { imp.isReadable = true; imp.SaveAndReimport(); }
        }
        src = AssetDatabase.LoadAssetAtPath<GameObject>(RiflePath);

        var inst = Object.Instantiate(src);
        inst.transform.position = Vector3.zero; inst.transform.rotation = Quaternion.identity;
        foreach (var a in inst.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
        foreach (var mb in inst.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);

        // world-space vertices of the rifle body (without the magazine) and the magazine's position
        var verts = new List<Vector3>();
        Vector3? clipCentre = null;
        foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var m = mf.transform.localToWorldMatrix;
            if (mf.name.ToLower().Contains("clip") || mf.name.ToLower().Contains("mag"))
            {
                var r = mf.GetComponent<Renderer>(); if (r != null) clipCentre = r.bounds.center;
                continue;
            }
            foreach (var v in mf.sharedMesh.vertices) verts.Add(m.MultiplyPoint3x4(v));
        }
        if (verts.Count == 0) { Object.DestroyImmediate(inst); return null; }

        Bounds b = new Bounds(verts[0], Vector3.zero);
        foreach (var v in verts) b.Encapsulate(v);
        Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
        int L = 0; for (int i = 1; i < 3; i++) if (b.size[i] > b.size[L]) L = i;

        // barrel end = the end with the smaller cross-section
        float lo = b.min[L], hi = b.max[L], len = hi - lo;
        Bounds sa = new Bounds(), sb = new Bounds(); bool ia = false, ib = false;
        foreach (var v in verts)
        {
            if (v[L] < lo + 0.15f * len) { if (!ia) { sa = new Bounds(v, Vector3.zero); ia = true; } else sa.Encapsulate(v); }
            else if (v[L] > hi - 0.15f * len) { if (!ib) { sb = new Bounds(v, Vector3.zero); ib = true; } else sb.Encapsulate(v); }
        }
        float ca = sa.size.x + sa.size.y + sa.size.z - sa.size[L], cb = sb.size.x + sb.size.y + sb.size.z - sb.size[L];
        Vector3 fwd = axes[L] * (cb < ca ? 1f : -1f);

        // up: the magazine hangs below the rifle
        int U = -1; float best = 0f; Vector3 up = Vector3.up;
        if (clipCentre.HasValue)
        {
            Vector3 off = clipCentre.Value - b.center;
            for (int i = 0; i < 3; i++) if (i != L && Mathf.Abs(off[i]) > best) { best = Mathf.Abs(off[i]); U = i; }
            if (U >= 0) up = axes[U] * (off[U] > 0f ? -1f : 1f);
        }
        if (U < 0) { int mid = 0; for (int i = 0; i < 3; i++) if (i != L && (mid == L || b.size[i] > b.size[mid])) mid = i; up = axes[mid]; }
        up = (up - fwd * Vector3.Dot(up, fwd)).normalized;

        Quaternion q = Quaternion.LookRotation(fwd, up);
        Quaternion inv = Quaternion.Inverse(q);
        float s = 0.85f / len;

        var root = new GameObject("Gun_DeathProp");
        inst.transform.SetParent(root.transform, false);
        inst.transform.localScale = inst.transform.localScale * s;
        inst.transform.localRotation = inv;
        inst.transform.localPosition = -s * (inv * b.center);

        Directory.CreateDirectory("Assets/Prefabs/Rigged");
        AssetDatabase.DeleteAsset(GunPropPath);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, GunPropPath);
        Object.DestroyImmediate(root);
        return prefab;
    }
}
