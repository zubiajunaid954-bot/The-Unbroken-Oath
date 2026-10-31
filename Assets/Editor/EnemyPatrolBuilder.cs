using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using GAMECON.Weather;
using scgFullBodyController;

/// <summary>
/// Rebuilds all enemies in SunTemple_Night.unity:
///  - count scales with the walkable area of the level
///  - each soldier gets a short patrol loop (3 waypoints) instead of standing still
///  - each soldier carries a flaming torch in the left hand so they are visible at night
/// </summary>
public static class EnemyPatrolBuilder
{
    const string NightScene = "Assets/Scenes/SunTemple_Night.unity";
    const string Folder = "Assets/Environment/Night";
    const float AreaPerEnemy = 1400f;     // m2 of walkable ground per soldier
    const int MinEnemies = 30, MaxEnemies = 50;
    const float MinDistFromPlayer = 25f;
    const float NearRadius = 90f;      // 60% of the soldiers are placed within this distance of the player
    const float MinDistBetween = 14f;
    const bool EnemyTorches = false;     // the player carries the torch now (see PlayerTorchBuilder)

    [MenuItem("GAMECON/Rebuild Enemies (Patrol + Torches)")]
    public static void Build()
    {
        if (!File.Exists(NightScene)) { EditorUtility.DisplayDialog("Error", "SunTemple_Night.unity not found.", "OK"); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.OpenScene(NightScene, OpenSceneMode.Single);

        // fresh NavMesh so patrol routes are valid
#pragma warning disable CS0618
        try { UnityEditor.AI.NavMeshBuilder.BuildNavMesh(); } catch (System.Exception e) { Debug.LogWarning("NavMesh bake failed: " + e.Message); }
#pragma warning restore CS0618

        var tri = NavMesh.CalculateTriangulation();
        if (tri.vertices.Length == 0)
        {
            EditorUtility.DisplayDialog("No NavMesh", "The NavMesh is empty, so enemies can't walk. Bake it in Window > AI > Navigation, then run this again.", "OK");
            return;
        }
        float area = 0f;
        var bounds = new Bounds(tri.vertices[0], Vector3.zero);
        foreach (var v in tri.vertices) bounds.Encapsulate(v);
        for (int i = 0; i + 2 < tri.indices.Length; i += 3)
            area += Vector3.Cross(tri.vertices[tri.indices[i + 1]] - tri.vertices[tri.indices[i]],
                                  tri.vertices[tri.indices[i + 2]] - tri.vertices[tri.indices[i]]).magnitude * 0.5f;
        int target = Mathf.Clamp(Mathf.RoundToInt(area / AreaPerEnemy), MinEnemies, MaxEnemies);
        Debug.Log("[EnemyPatrol] Walkable area " + Mathf.RoundToInt(area) + " m2, bounds " + bounds.size + " -> " + target + " enemies.");

        Vector3 playerPos = FindPlayerPos();
        NavMesh.SamplePosition(playerPos, out NavMeshHit pHit, 10f, NavMesh.AllAreas);
        Vector3 playerNav = pHit.hit ? pHit.position : playerPos;

        RemoveOldEnemies(scene);
        Transform group = FindOrCreateRoot(scene, "====== ENEMIES ======", 4);
        var routesRoot = new GameObject("Patrol Routes").transform;
        routesRoot.SetParent(group, false);
        var soldiers = new GameObject("Soldiers").transform;
        soldiers.SetParent(group, false);

        var prefabs = new[]
        {
            Load("Assets/scgFullBodyController/Prefabs/AI/Bot(Ai) Easy.prefab"),
            Load("Assets/scgFullBodyController/Prefabs/AI/Bot(Ai) Medium.prefab"),
            Load("Assets/scgFullBodyController/Prefabs/AI/Bot(Ai) Hard.prefab"),
        }.Where(p => p != null).ToArray();
        if (prefabs.Length == 0) { Debug.LogError("[EnemyPatrol] Bot prefabs not found."); return; }

        Material torchWood = MakeMat("Torch_Wood", "Standard", new Color(0.18f, 0.1f, 0.05f));
        Material flameMat = MakeFlameMat();

        var rng = new System.Random(77);
        var used = new List<Vector3>();
        int placed = 0, attempts = 0;
        int failDist = 0, failSep = 0, failReach = 0, failRoute = 0, failNav = 0, reachOk = 0, reachTried = 0;
        bool useReach = true;
        int nearTarget = Mathf.RoundToInt(target * 0.6f);

        while (placed < target && attempts++ < target * 600)
        {
            // first 60%: sample around the player so you actually meet them; the rest: whole level
            Vector3 c;
            if (placed < nearTarget)
            {
                float a = (float)(rng.NextDouble() * Mathf.PI * 2);
                float r = Mathf.Lerp(MinDistFromPlayer, NearRadius, Mathf.Sqrt((float)rng.NextDouble()));
                c = playerPos + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
            }
            else
            {
                c = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, (float)rng.NextDouble()), playerPos.y,
                                Mathf.Lerp(bounds.min.z, bounds.max.z, (float)rng.NextDouble()));
            }
            // sample over the full height range of the level (terrain / floors at any height)
            if (!SampleAnyHeight(c, bounds, out Vector3 p)) { failNav++; continue; }
            if ((new Vector3(p.x, 0, p.z) - new Vector3(playerPos.x, 0, playerPos.z)).sqrMagnitude < MinDistFromPlayer * MinDistFromPlayer) { failDist++; continue; }
            if (used.Any(u => (u - p).sqrMagnitude < MinDistBetween * MinDistBetween)) { failSep++; continue; }

            if (useReach)
            {
                reachTried++;
                if (Reachable(playerNav, p, 600f)) reachOk++;
                else
                {
                    failReach++;
                    // if the player's own NavMesh island is tiny, almost nothing is "reachable": stop filtering
                    if (reachTried >= 150 && reachOk * 20 < reachTried) { useReach = false; Debug.LogWarning("[EnemyPatrol] Player NavMesh area looks isolated - skipping reachability filter."); }
                    if (useReach) continue;
                }
            }

            Vector3[] route = MakeRoute(p, rng);
            if (route == null) { failRoute++; continue; }

            used.Add(p);
            placed++;
            var prefab = prefabs[placed % prefabs.Length];
            var bot = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            bot.name = "Enemy_Soldier_" + placed;
            bot.transform.SetParent(soldiers, true);
            bot.transform.SetPositionAndRotation(p, Quaternion.Euler(0, rng.Next(0, 360), 0));
            var agent = bot.GetComponent<NavMeshAgent>();
            if (agent != null) agent.Warp(p);

            var ai = bot.GetComponent<AiController>();
            if (ai != null)
            {
                ai.sightRange = 30f;        // how far a soldier can see (needs a clear line of sight, inside the view cone)
                ai.attackRange = 22f;
                ai.viewAngle = 110f;        // soldiers only react to what is in front of them
                ai.patrolPause = 1.5f;      // short pause at each end of the beat
                ai.patrolPoints = BuildWaypoints(routesRoot, placed, route);
            }
            if (EnemyTorches) AttachTorch(bot, torchWood, flameMat);
        }
        Debug.Log("[EnemyPatrol] attempts=" + attempts + " placed=" + placed + " | rejected: nav=" + failNav + " dist=" + failDist + " sep=" + failSep + " unreachable=" + failReach + " route=" + failRoute);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[EnemyPatrol] Placed " + placed + " enemies.");
        EditorUtility.DisplayDialog("Enemies rebuilt",
            placed + " enemies placed (walkable area " + Mathf.RoundToInt(area) + " m2).\nEach one patrols a short loop and carries a torch.\n\nPress Play.", "OK");
    }

    // ---------- placement helpers ----------
    // Finds a NavMesh point under/over (x,z) anywhere in the level's height range.
    static bool SampleAnyHeight(Vector3 c, Bounds b, out Vector3 result)
    {
        result = c;
        float[] ys = { c.y, b.center.y, b.min.y + 2f, b.max.y - 2f };
        foreach (float y in ys)
            if (NavMesh.SamplePosition(new Vector3(c.x, y, c.z), out NavMeshHit h, b.size.y * 0.5f + 5f, NavMesh.AllAreas)
                && Mathf.Abs(h.position.x - c.x) < 4f && Mathf.Abs(h.position.z - c.z) < 4f)
            { result = h.position; return true; }
        return false;
    }

    static bool Reachable(Vector3 from, Vector3 to, float maxLen)
    {
        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return false;
        return PathLength(path) <= maxLen;
    }

    static float PathLength(NavMeshPath path)
    {
        float len = 0;
        for (int i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        return len;
    }

    // Short back-and-forth beat: START (where the soldier spawns) <-> END, 7-12 m apart, easy straight walk.
    static Vector3[] MakeRoute(Vector3 start, System.Random rng)
    {
        for (int t = 0; t < 24; t++)
        {
            float ang = (float)(rng.NextDouble() * Mathf.PI * 2);
            float dist = 7f + (float)rng.NextDouble() * 5f;
            Vector3 c = start + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * dist;
            if (!NavMesh.SamplePosition(c, out NavMeshHit h, 3f, NavMesh.AllAreas)) continue;
            if (Mathf.Abs(h.position.y - start.y) > 1.5f) continue;                     // same level (no stairs / drops)
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(start, h.position, NavMesh.AllAreas, path)) continue;
            if (path.status != NavMeshPathStatus.PathComplete) continue;
            float len = PathLength(path);
            float straight = Vector3.Distance(start, h.position);
            if (len > 16f || len > straight * 1.35f) continue;                           // short and fairly direct
            return new[] { start, h.position };
        }
        return null;
    }

    static Transform[] BuildWaypoints(Transform root, int index, Vector3[] route)
    {
        var r = new GameObject("Route_" + index).transform;
        r.SetParent(root, false);
        r.gameObject.AddComponent<PatrolRouteGizmo>();
        var list = new List<Transform>();
        for (int i = 0; i < route.Length; i++)
        {
            var wp = new GameObject("WP_" + (i + 1)).transform;
            wp.SetParent(r, false);
            wp.position = route[i];
            list.Add(wp);
        }
        return list.ToArray();
    }

    // ---------- torch ----------
    static void AttachTorch(GameObject bot, Material wood, Material flameMat)
    {
        Transform hand = null;
        var anim = bot.GetComponent<Animator>();
        if (anim != null && anim.isHuman) hand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        if (hand == null)
            hand = bot.GetComponentsInChildren<Transform>(true)
                      .FirstOrDefault(t => t.name.ToLower().Contains("left") && t.name.ToLower().Contains("hand") && !t.name.ToLower().Contains("index")
                                        && !t.name.ToLower().Contains("thumb") && !t.name.ToLower().Contains("middle") && !t.name.ToLower().Contains("ring") && !t.name.ToLower().Contains("pinky"));
        if (hand == null) { Debug.LogWarning("[EnemyPatrol] No left hand bone on " + bot.name + " - torch attached to root."); hand = bot.transform; }

        var torch = new GameObject("Torch");
        torch.transform.SetParent(hand, false);
        // hold it upright in world space, slightly forward of the hand
        torch.transform.position = hand.position + bot.transform.forward * 0.12f + Vector3.up * 0.06f;
        torch.transform.rotation = Quaternion.identity;

        // Sun Temple torch prop (auto-fitted: upright, ~0.5 m long, grip near the hand)
        var torchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Sun_Temple/Prefabs/Props/Prop_FireTorch_A.prefab");
        Bounds fit = new Bounds(torch.transform.position, Vector3.zero);
        if (torchPrefab != null)
        {
            var prop = (GameObject)PrefabUtility.InstantiatePrefab(torchPrefab);
            prop.name = "Prop_FireTorch_A";
            prop.transform.SetParent(torch.transform, false);
            prop.transform.localPosition = Vector3.zero;
            prop.transform.localRotation = Quaternion.identity;
            prop.transform.localScale = Vector3.one;
            // the prop has a MeshCollider: remove it so it never blocks the NavMesh agent or bullets
            foreach (var c in prop.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var r in prop.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = ShadowCastingMode.Off; }
            GameObjectUtility.SetStaticEditorFlags(prop, 0);

            Bounds b0 = RendererBounds(prop);
            // longest axis should point up
            if (b0.size.y < Mathf.Max(b0.size.x, b0.size.z) * 0.9f)
            {
                prop.transform.localRotation = b0.size.x >= b0.size.z ? Quaternion.Euler(0, 0, 90) : Quaternion.Euler(90, 0, 0);
                b0 = RendererBounds(prop);
            }
            float s = 0.5f / Mathf.Max(0.001f, b0.size.y);
            prop.transform.localScale = Vector3.one * s;
            b0 = RendererBounds(prop);
            // base of the prop sits 0.12 m below the hand, centred on the hand
            Vector3 shift = new Vector3(torch.transform.position.x - b0.center.x, torch.transform.position.y - 0.12f - b0.min.y, torch.transform.position.z - b0.center.z);
            prop.transform.position += shift;
            fit = RendererBounds(prop);
        }
        else
        {
            Debug.LogWarning("[EnemyPatrol] Prop_FireTorch_A prefab not found - torch has no model.");
            fit = new Bounds(torch.transform.position + Vector3.up * 0.3f, Vector3.zero);
        }
        Vector3 flameWorld = new Vector3(fit.center.x, fit.max.y, fit.center.z);

        // flame
        var fgo = new GameObject("Flame");
        fgo.transform.SetParent(torch.transform, false);
        fgo.transform.position = flameWorld;
        var ps = fgo.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.24f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.15f, 0.9f), new Color(1f, 0.85f, 0.35f, 0.9f));
        main.maxParticles = 40;
        var em = ps.emission; em.rateOverTime = 42f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8f; sh.radius = 0.02f;
        sh.rotation = new Vector3(-90, 0, 0);
        var sol = ps.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.5f), 0f), new GradientColorKey(new Color(1f, 0.35f, 0.05f), 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var rend = fgo.GetComponent<ParticleSystemRenderer>();
        rend.sharedMaterial = flameMat;
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;
        ps.Play();

        // soft halo so the torch is visible from far away through rain and fog
        var hgo = new GameObject("TorchGlow");
        hgo.transform.SetParent(torch.transform, false);
        hgo.transform.position = flameWorld + Vector3.up * 0.05f;
        var hps = hgo.AddComponent<ParticleSystem>();
        hps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var hm = hps.main;
        hm.loop = true; hm.playOnAwake = true;
        hm.simulationSpace = ParticleSystemSimulationSpace.Local;
        hm.startLifetime = 0.6f; hm.startSpeed = 0f;
        hm.startSize = new ParticleSystem.MinMaxCurve(1.2f, 1.7f);
        hm.startColor = new Color(1f, 0.5f, 0.18f, 0.55f);
        hm.maxParticles = 8;
        var hem = hps.emission; hem.rateOverTime = 10f;
        var hsh = hps.shape; hsh.enabled = false;
        var hr = hgo.GetComponent<ParticleSystemRenderer>();
        hr.sharedMaterial = flameMat;
        hr.shadowCastingMode = ShadowCastingMode.Off;
        hr.receiveShadows = false;
        hps.Play();

        // light: lets the player see the enemy and the ground around them
        var lgo = new GameObject("TorchLight");
        lgo.transform.SetParent(torch.transform, false);
        lgo.transform.position = flameWorld + Vector3.up * 0.1f;
        var l = lgo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.6f, 0.25f);
        l.range = 11f;
        l.intensity = 2.6f;
        l.shadows = LightShadows.None;
        l.lightmapBakeType = LightmapBakeType.Realtime;
        var fl = lgo.AddComponent<FlickerLight>();
        fl.amount = 0.3f; fl.speed = 9f;
    }

    // ---------- misc ----------
    static void RemoveOldEnemies(Scene scene)
    {
        var roots = new HashSet<GameObject>();
        foreach (var ai in Object.FindObjectsOfType<AiController>(true))
        {
            if (ai.gameObject.scene != scene) continue;
            if (ai.GetComponentInParent<GAMECON.Mission.Hostage>() != null) continue;      // keep the captive teammates
            var r = PrefabUtility.GetOutermostPrefabInstanceRoot(ai.gameObject);
            roots.Add(r != null ? r : ai.gameObject);
        }
        foreach (var r in roots) if (r != null) Object.DestroyImmediate(r);
        // old empty containers from earlier builds
        foreach (var sold in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                     .Where(t => t.name == "Soldiers" || t.name == "Patrol Routes").ToList())
            if (sold != null) Object.DestroyImmediate(sold.gameObject);
    }

    static Vector3 FindPlayerPos()
    {
        var uc = Object.FindObjectOfType<scgFullBodyController.ThirdPersonUserControl>();
        if (uc != null) return uc.transform.position;
        var p = GameObject.FindGameObjectWithTag("Player");
        return p != null ? p.transform.position : Vector3.zero;
    }

    static Transform FindOrCreateRoot(Scene scene, string name, int idx)
    {
        var g = scene.GetRootGameObjects().FirstOrDefault(r => r.name == name);
        if (g == null)
        {
            g = new GameObject(name);
            SceneManager.MoveGameObjectToScene(g, scene);
            g.transform.SetSiblingIndex(Mathf.Min(idx, scene.rootCount - 1));
        }
        return g.transform;
    }

    static Bounds RendererBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static GameObject Load(string p) { return AssetDatabase.LoadAssetAtPath<GameObject>(p); }

    static Material MakeMat(string name, string shader, Color c)
    {
        string path = Folder + "/" + name + ".mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(Shader.Find(shader));
        m.color = c;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Material MakeFlameMat()
    {
        string path = Folder + "/Torch_Flame.mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
        m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/SoftDot.png");
        m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
