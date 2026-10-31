using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using GAMECON.Mission;

/// <summary>
/// The teammate models (Duaa, Zubia, Maha) are static meshes in a T-pose. This tool RIGS them:
///  1. reads the mesh, finds the body landmarks (shoulders, elbows, hips, knees, ankles ...) from the geometry
///  2. builds a 19-bone humanoid skeleton and skins the mesh to it (distance based bone weights)
///  3. saves a SkinnedMesh prefab  Assets/Prefabs/Rigged/&lt;Name&gt;_Rigged.prefab  with a CaptiveRig component
///     that animates it (kneeling with hands tied behind the back, standing up, talking).
/// </summary>
public static class TeammateRigBuilder
{
    const string OutFolder = "Assets/Prefabs/Rigged";
    static readonly string[] Names = { "Duaa", "Zubia", "Maha", "Samrah" };
    const float TargetHeight = 1.7f;

    [MenuItem("GAMECON/Rig Teammates (Duaa, Zubia, Maha) + Samrah")]
    public static void RigAll()
    {
        Directory.CreateDirectory(OutFolder);
        Directory.CreateDirectory(OutFolder + "/Meshes");
        int ok = 0;
        string report = "";
        foreach (var n in Names)
        {
            try
            {
                string r;
                if (RigOne(n, out r)) ok++;
                report += "\n" + r;
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Rig] " + n + " failed: " + e);
                report += "\n" + n + ": FAILED (" + e.Message + ")";
            }
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Rig teammates", ok + " of " + Names.Length + " teammates rigged." + report + "\n\nNow run: GAMECON > Build Minimap + Rescue Mission", "OK");
    }

    // =================================================================================================
    class Part { public Mesh mesh; public Matrix4x4 m; public Material[] mats; }

    static bool RigOne(string who, out string report)
    {
        report = who + ": ";
        var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + who + ".prefab");
        if (src == null) { report += "prefab not found"; return false; }

        // meshes must be readable
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
        try
        {
            inst.transform.position = Vector3.zero;                  // keep the prefab's own rotation (-90 deg import axis fix)
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                string p = AssetDatabase.GetAssetPath(mf.sharedMesh);
                var imp = AssetImporter.GetAtPath(p) as ModelImporter;
                if (imp != null && !imp.isReadable) { imp.isReadable = true; imp.SaveAndReimport(); }
            }
            inst.SetActive(false); inst.SetActive(true);

            var parts = new List<Part>();
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null) continue;
                parts.Add(new Part { mesh = mf.sharedMesh, m = mf.transform.localToWorldMatrix, mats = mr.sharedMaterials });
            }
            // (skinned renderers could not exist: the models are static)
            if (parts.Count == 0) { report += "no mesh found"; return false; }

            // ---------- 1. gather all vertices in world space ----------
            int total = parts.Sum(p => p.mesh.vertexCount);
            var wv = new Vector3[total];
            var wn = new Vector3[total];
            var wt = new Vector4[total];
            var uv = new Vector2[total];
            var colors = new Color[total];
            bool anyColors = false;
            int o = 0;
            foreach (var p in parts)
            {
                var v = p.mesh.vertices; var n = p.mesh.normals; var t = p.mesh.tangents; var u = p.mesh.uv; var c = p.mesh.colors;
                Matrix4x4 nm = p.m.inverse.transpose;
                for (int i = 0; i < v.Length; i++)
                {
                    wv[o + i] = p.m.MultiplyPoint3x4(v[i]);
                    wn[o + i] = n.Length > i ? nm.MultiplyVector(n[i]).normalized : Vector3.up;
                    if (t.Length > i) { var tw = p.m.MultiplyVector(new Vector3(t[i].x, t[i].y, t[i].z)).normalized; wt[o + i] = new Vector4(tw.x, tw.y, tw.z, t[i].w); }
                    else wt[o + i] = new Vector4(1, 0, 0, 1);
                    uv[o + i] = u.Length > i ? u[i] : Vector2.zero;
                    if (c.Length > i) { colors[o + i] = c[i]; anyColors = true; } else colors[o + i] = Color.white;
                }
                o += v.Length;
            }

            // ---------- 2. normalise: feet at y = 0, centred, 1.7 m tall, arms along X, facing +Z ----------
            var b = new Bounds(wv[0], Vector3.zero);
            foreach (var v in wv) b.Encapsulate(v);
            float H0 = b.size.y;
            if (H0 < b.size.x * 0.6f && H0 < b.size.z * 0.6f)
                Debug.LogWarning("[Rig] " + who + ": the model does not look upright (height " + H0 + " vs width " + b.size.x + "/" + b.size.z + ")");
            float sc = TargetHeight / H0;
            Vector3 origin = new Vector3(b.center.x, b.min.y, b.center.z);
            for (int i = 0; i < total; i++) wv[i] = (wv[i] - origin) * sc;
            float H = TargetHeight;

            // arms along Z instead of X? rotate 90 degrees about Y
            float extX = 0f, extZ = 0f;
            for (int i = 0; i < total; i++)
                if (wv[i].y > 0.70f * H && wv[i].y < 0.92f * H) { extX = Mathf.Max(extX, Mathf.Abs(wv[i].x)); extZ = Mathf.Max(extZ, Mathf.Abs(wv[i].z)); }
            Quaternion fix = Quaternion.identity;
            if (extZ > extX * 1.25f) fix = Quaternion.Euler(0, 90f, 0);

            // which way is she facing? toes point forward: the soles are in front of the shins
            Quaternion q = fix;
            Vector3 soleSum = Vector3.zero, shinSum = Vector3.zero; int soleN = 0, shinN = 0;
            for (int i = 0; i < total; i++)
            {
                Vector3 r = q * wv[i];
                if (r.y < 0.05f * H) { soleSum += r; soleN++; }
                else if (r.y > 0.12f * H && r.y < 0.30f * H) { shinSum += r; shinN++; }
            }
            if (soleN > 0 && shinN > 0 && (soleSum / soleN).z < (shinSum / shinN).z) fix = Quaternion.Euler(0, 180f, 0) * fix;
            for (int i = 0; i < total; i++) { wv[i] = fix * wv[i]; wn[i] = fix * wn[i]; var tt = fix * new Vector3(wt[i].x, wt[i].y, wt[i].z); wt[i] = new Vector4(tt.x, tt.y, tt.z, wt[i].w); }

            // re-centre horizontally on the ARM SPAN (a cape / bag makes the torso mass lopsided) and make both arms equally long
            float spanMax = 0f, spanMin = 0f;
            for (int i = 0; i < total; i++)
                if (wv[i].y > 0.70f * H && wv[i].y < 0.95f * H) { spanMax = Mathf.Max(spanMax, wv[i].x); spanMin = Mathf.Min(spanMin, wv[i].x); }
            float spanCx = (spanMax + spanMin) * 0.5f;
            for (int i = 0; i < total; i++) wv[i].x -= spanCx;
            float halfSpan = (spanMax - spanMin) * 0.5f;

            // ---------- 3. landmarks ----------
            float armY = 0.80f * H; { float s = 0f; int n = 0; for (int i = 0; i < total; i++) if (Mathf.Abs(wv[i].x) > 0.34f * H && wv[i].y > 0.60f * H) { s += wv[i].y; n++; } if (n > 50) armY = s / n; }
            float tipP = 0.46f * H, tipM = 0.46f * H;
            { float mp = 0, mm = 0; for (int i = 0; i < total; i++) if (Mathf.Abs(wv[i].y - armY) < 0.10f * H) { if (wv[i].x > mp) mp = wv[i].x; if (-wv[i].x > mm) mm = -wv[i].x; } if (mp > 0.25f * H) tipP = mp; if (mm > 0.25f * H) tipM = mm; }
            if (halfSpan > 0.25f * H) { tipP = halfSpan; tipM = halfSpan; }       // symmetric arms
            float torsoHalf = 0.17f * H;
            { float m = 0; for (int i = 0; i < total; i++) if (wv[i].y > 0.62f * H && wv[i].y < 0.74f * H && Mathf.Abs(wv[i].x) < 0.30f * H) m = Mathf.Max(m, Mathf.Abs(wv[i].x)); if (m > 0.08f * H) torsoHalf = m; }
            float legX = 0.065f * H;
            { float s = 0f; int n = 0; for (int i = 0; i < total; i++) if (wv[i].y > 0.14f * H && wv[i].y < 0.26f * H) { s += Mathf.Abs(wv[i].x); n++; } if (n > 50) legX = Mathf.Clamp(s / n, 0.04f * H, 0.14f * H); }
            float toeZ = 0.08f * H, heelZ = -0.04f * H;
            { float mx = -1e9f, mn = 1e9f; for (int i = 0; i < total; i++) if (wv[i].y < 0.05f * H) { mx = Mathf.Max(mx, wv[i].z); mn = Mathf.Min(mn, wv[i].z); } if (mx > mn) { toeZ = mx; heelZ = mn; } }
            float ankleZ = heelZ + (toeZ - heelZ) * 0.22f;
            // the body mass is centred on z = 0 for the torso; legs / feet are offset by ankleZ
            float torsoCz = 0f; { int n = 0; float s = 0; for (int i = 0; i < total; i++) if (wv[i].y > 0.55f * H && wv[i].y < 0.75f * H && Mathf.Abs(wv[i].x) < 0.2f * H) { s += wv[i].z; n++; } if (n > 0) torsoCz = s / n; }

            float hipsY = 0.53f * H, spineY = 0.60f * H, chestY = 0.70f * H, neckY = 0.83f * H, headY = 0.89f * H, topY = 1.0f * H;
            float kneeY = 0.285f * H, ankleY = 0.045f * H;
            float shoulderX = Mathf.Min(torsoHalf * 0.82f, 0.18f * H);
            float hipX = Mathf.Min(legX * 0.95f, 0.10f * H);
            float handLen = 0.10f * H;
            float wristP = tipP - handLen, wristM = tipM - handLen;

            // ---------- 4. skeleton ----------
            var root = new GameObject(who + "_Rigged");
            var armature = new GameObject("Armature"); armature.transform.SetParent(root.transform, false);
            var T = new Dictionary<string, Transform>();
            var pos = new Dictionary<string, Vector3>();   // bind world position (root space)
            Transform Mk(string name, string parent, Vector3 p)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent == null ? armature.transform : T[parent], false);
                go.transform.position = p;           // root is at the origin with identity rotation, so world == root space
                T[name] = go.transform; pos[name] = p;
                return go.transform;
            }
            Mk("Hips", null, new Vector3(0, hipsY, torsoCz));
            Mk("Spine", "Hips", new Vector3(0, spineY, torsoCz));
            Mk("Chest", "Spine", new Vector3(0, chestY, torsoCz));
            Mk("Neck", "Chest", new Vector3(0, neckY, torsoCz));
            Mk("Head", "Neck", new Vector3(0, headY, torsoCz));
            Vector3 headEnd = new Vector3(0, topY, torsoCz);
            foreach (int sgn in new[] { 1, -1 })
            {
                string sd = sgn > 0 ? "P" : "M";
                float wrist = sgn > 0 ? wristP : wristM, tip = sgn > 0 ? tipP : tipM;
                float elbow = shoulderX + (wrist - shoulderX) * 0.5f;
                Mk("Shoulder" + sd, "Chest", new Vector3(sgn * 0.05f * H, armY, torsoCz));
                Mk("UpperArm" + sd, "Shoulder" + sd, new Vector3(sgn * shoulderX, armY, torsoCz));
                Mk("LowerArm" + sd, "UpperArm" + sd, new Vector3(sgn * elbow, armY, torsoCz));
                Mk("Hand" + sd, "LowerArm" + sd, new Vector3(sgn * wrist, armY, torsoCz));
                Mk("UpperLeg" + sd, "Hips", new Vector3(sgn * hipX, hipsY - 0.02f * H, torsoCz * 0.5f));
                Mk("LowerLeg" + sd, "UpperLeg" + sd, new Vector3(sgn * legX, kneeY, (torsoCz * 0.5f + ankleZ) * 0.5f));
                Mk("Foot" + sd, "LowerLeg" + sd, new Vector3(sgn * legX, ankleY, ankleZ));
            }
            // end points (for the last bone of each chain)
            var end = new Dictionary<string, Vector3>
            {
                { "Head", headEnd },
                { "HandP", new Vector3(tipP, armY, torsoCz) }, { "HandM", new Vector3(-tipM, armY, torsoCz) },
                { "FootP", new Vector3(legX, ankleY * 0.5f, toeZ) }, { "FootM", new Vector3(-legX, ankleY * 0.5f, toeZ) },
            };

            // ---------- 5. skin weights (distance to the bone segments) ----------
            string[] boneOrder = { "Hips", "Spine", "Chest", "Neck", "Head", "ShoulderP", "ShoulderM", "UpperArmP", "UpperArmM", "LowerArmP", "LowerArmM",
                                   "HandP", "HandM", "UpperLegP", "UpperLegM", "LowerLegP", "LowerLegM", "FootP", "FootM" };
            var segA = new Vector3[boneOrder.Length]; var segB = new Vector3[boneOrder.Length];
            for (int i = 0; i < boneOrder.Length; i++)
            {
                string n = boneOrder[i];
                segA[i] = pos[n];
                var child = T[n].Cast<Transform>().FirstOrDefault(c => boneOrder.Contains(c.name));
                if (end.ContainsKey(n)) segB[i] = end[n];
                else if (n == "Hips") segB[i] = pos["Spine"];
                else if (child != null) segB[i] = pos[child.name];
                else segB[i] = segA[i] + Vector3.up * 0.05f;
            }
            // the pelvis extends below the hips joint, the shoulder bone toward the neck
            segA[0] = new Vector3(0, hipsY - 0.07f * H, torsoCz);

            var weights = new BoneWeight[total];
            var d = new float[boneOrder.Length];
            float eps = 0.015f * H;
            for (int i = 0; i < total; i++)
            {
                Vector3 p = wv[i];
                for (int k = 0; k < boneOrder.Length; k++)
                {
                    Vector3 a = segA[k], bb = segB[k], ab = bb - a;
                    float tpar = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                    d[k] = Vector3.Distance(p, a + ab * tpar);
                }
                // top 4 by weight
                int i0 = -1, i1 = -1, i2 = -1, i3 = -1; float w0 = 0, w1 = 0, w2 = 0, w3 = 0;
                for (int k = 0; k < boneOrder.Length; k++)
                {
                    float w = 1f / Mathf.Pow(d[k] + eps, 4f);
                    if (w > w0) { i3 = i2; w3 = w2; i2 = i1; w2 = w1; i1 = i0; w1 = w0; i0 = k; w0 = w; }
                    else if (w > w1) { i3 = i2; w3 = w2; i2 = i1; w2 = w1; i1 = k; w1 = w; }
                    else if (w > w2) { i3 = i2; w3 = w2; i2 = k; w2 = w; }
                    else if (w > w3) { i3 = k; w3 = w; }
                }
                float sum = w0 + w1 + w2 + w3;
                var bw = new BoneWeight();
                bw.boneIndex0 = i0; bw.weight0 = w0 / sum;
                bw.boneIndex1 = Mathf.Max(0, i1); bw.weight1 = i1 >= 0 ? w1 / sum : 0;
                bw.boneIndex2 = Mathf.Max(0, i2); bw.weight2 = i2 >= 0 ? w2 / sum : 0;
                bw.boneIndex3 = Mathf.Max(0, i3); bw.weight3 = i3 >= 0 ? w3 / sum : 0;
                weights[i] = bw;
            }

            // ---------- 6. build the skinned mesh ----------
            var mesh = new Mesh { name = who + "_Rigged" };
            if (total > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = wv; mesh.normals = wn; mesh.tangents = wt; mesh.uv = uv;
            if (anyColors) mesh.colors = colors;
            var allMats = new List<Material>();
            int subCount = parts.Sum(p => p.mesh.subMeshCount);
            mesh.subMeshCount = subCount;
            int si = 0, vo = 0;
            foreach (var p in parts)
            {
                for (int s = 0; s < p.mesh.subMeshCount; s++)
                {
                    var tris = p.mesh.GetTriangles(s);
                    for (int j = 0; j < tris.Length; j++) tris[j] += vo;
                    mesh.SetTriangles(tris, si++);
                    allMats.Add(s < p.mats.Length ? p.mats[s] : (p.mats.Length > 0 ? p.mats[p.mats.Length - 1] : null));
                }
                vo += p.mesh.vertexCount;
            }
            mesh.boneWeights = weights;
            var bones = boneOrder.Select(n => T[n]).ToArray();
            mesh.bindposes = bones.Select(bt => bt.worldToLocalMatrix * root.transform.localToWorldMatrix).ToArray();
            mesh.RecalculateBounds();

            string meshPath = OutFolder + "/Meshes/" + who + "_Rigged.asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            var smr = body.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh; smr.sharedMaterials = allMats.ToArray();
            smr.bones = bones; smr.rootBone = T["Hips"];
            smr.updateWhenOffscreen = true;
            smr.localBounds = new Bounds(new Vector3(0, H * 0.5f, 0), new Vector3(H * 1.4f, H * 1.4f, H * 1.4f));

            // ---------- 7. the animation component ----------
            var rig = root.AddComponent<CaptiveRig>();
            rig.hips = T["Hips"]; rig.height = H; rig.hipsBindLocalPos = T["Hips"].localPosition;
            string[] map = { "Spine", "Chest", "Neck", "Head", "ShoulderP", "ShoulderM", "UpperArmP", "UpperArmM", "LowerArmP", "LowerArmM", "HandP", "HandM",
                             "UpperLegP", "UpperLegM", "LowerLegP", "LowerLegM", "FootP", "FootM" };
            rig.bones = new Transform[18]; rig.bindDir = new Vector3[18];
            for (int i = 0; i < 18; i++)
            {
                string n = map[i];
                rig.bones[i] = T[n];
                int bi = System.Array.IndexOf(boneOrder, n);
                rig.bindDir[i] = (segB[bi] - pos[n]).normalized;
                if (rig.bindDir[i].sqrMagnitude < 0.5f) rig.bindDir[i] = Vector3.up;
            }
            rig.live = true;

            string prefabPath = OutFolder + "/" + who + "_Rigged.prefab";
            AssetDatabase.DeleteAsset(prefabPath);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);

            report += "rigged (" + total + " vertices, " + boneOrder.Length + " bones, forward fix " + fix.eulerAngles.y + " deg)";
            return true;
        }
        finally
        {
            Object.DestroyImmediate(inst);
        }
    }
}
