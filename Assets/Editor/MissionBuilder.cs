using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using GAMECON.Mission;
using scgFullBodyController;

/// <summary>
/// Builds, in SunTemple_Night.unity:
///  - a baked top-down map of the level + a rotating mini-map (M = full map) with player / enemy / objective icons
///  - the rescue mission: teammates held in the biggest interior room of the temple, mission HUD, intro and win screen
///  - door interaction key (E) for the Sun Temple doors
/// </summary>
public static class MissionBuilder
{
    const string NightScene = "Assets/Scenes/SunTemple_Night.unity";
    const string UiFolder = "Assets/UI/Minimap";
    const string EnvFolder = "Assets/Environment/Night";
    const int HostageCount = 3;      // Duaa, Zubia, Maha

    [MenuItem("GAMECON/Build Minimap + Rescue Mission")]
    public static void Build()
    {
        if (!File.Exists(NightScene)) { EditorUtility.DisplayDialog("Error", "SunTemple_Night.unity not found.", "OK"); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = EditorSceneManager.OpenScene(NightScene, OpenSceneMode.Single);
        Directory.CreateDirectory(UiFolder);

        // ---- world rectangle for the map ----
        var tri = NavMesh.CalculateTriangulation();
        if (tri.vertices.Length == 0) { EditorUtility.DisplayDialog("No NavMesh", "Bake the NavMesh first (run 'Rebuild Enemies').", "OK"); return; }
        var nb = new Bounds(tri.vertices[0], Vector3.zero);
        foreach (var v in tri.vertices) nb.Encapsulate(v);
        nb.Expand(new Vector3(60f, 0, 60f));
        Vector2 worldCenter = new Vector2(nb.center.x, nb.center.z);
        Vector2 worldSize = new Vector2(nb.size.x, nb.size.z);

        // ---- map picture ----
        Texture2D mapTex = BakeMap(scene, worldCenter, worldSize);

        // ---- icon sprites ----
        Sprite circle = MakeSprite("Icon_Circle", 128, (u, v) => new Color(1, 1, 1, Mathf.Clamp01((0.495f - Dist(u, v)) / 0.02f)));
        Sprite square = MakeSprite("Icon_Square", 64, (u, v) => new Color(1, 1, 1, 1));
        Sprite ringSp = MakeSprite("Icon_Ring", 256, (u, v) => { float d = Dist(u, v); return new Color(1, 1, 1, Edge(d, 0.465f, 0.5f)); });
        Sprite enemy = MakeSprite("Icon_Enemy", 64, (u, v) =>
        {
            float d = Dist(u, v);
            if (d > 0.48f) return Color.clear;
            return d > 0.34f ? new Color(0, 0, 0, 0.95f) : Color.white;
        });
        Sprite arrow = MakeSprite("Icon_PlayerArrow", 64, (u, v) => ArrowPixel(u, v));
        Sprite diamond = MakeSprite("Icon_Objective", 64, (u, v) =>
        {
            float d = Mathf.Abs(u - 0.5f) + Mathf.Abs(v - 0.5f);
            if (d > 0.47f) return Color.clear;
            return d > 0.36f ? new Color(0, 0, 0, 0.95f) : Color.white;
        });

        Sprite plusIcon = MakeSprite("Icon_HealthCross", 64, (u, v) => PlusPixel(u, v));

        // ---- three rooms: one captive teammate in each ----
        string roomName;
        Vector3[] roomSpots = FindThreeRooms(out roomName);
        Vector3 roomFloor = (roomSpots[0] + roomSpots[1] + roomSpots[2]) / 3f;
        Bounds room = new Bounds(roomFloor + Vector3.up * 3f, new Vector3(14f, 8f, 14f));
        float roomArea = 0f;

        // ---- UI ----
        Transform uiGroup = FindOrCreateRoot(scene, "====== UI ======");
        var old = uiGroup.Find("MissionCanvas");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        UiRefs ui = BuildUi(uiGroup, mapTex, circle, square, ringSp, enemy, arrow, diamond);

        // ---- mission objects ----
        Transform missionGroup = FindOrCreateRoot(scene, "====== MISSION ======");
        foreach (var c in missionGroup.Cast<Transform>().ToList()) Object.DestroyImmediate(c.gameObject);
        var hostages = BuildHostages(scene, missionGroup, room, roomFloor, roomSpots, diamond, out Transform roomMarker);

        BuildPickups(missionGroup, roomSpots, plusIcon);

        var mgrGo = new GameObject("MissionManager");
        mgrGo.transform.SetParent(missionGroup, false);
        var mm = mgrGo.AddComponent<MissionManager>();
        mgrGo.AddComponent<CutsceneDirector>();
        mm.introText = "OPERATION SUN TEMPLE\nSamrah, your teammates Duaa, Zubia and Maha were abducted and are held in the temple.\nFight through the enemy, reach the marked room and free them.";
        mm.objectiveText = ui.objective; mm.promptText = ui.prompt; mm.bannerText = ui.banner;
        mm.winPanel = ui.winPanel; mm.winText = ui.winText;
        mm.voiceText = ui.voice;
        mm.objectiveRoom = roomMarker;
        mm.hostages = hostages;

        // initial layout (so the Game view looks right even before pressing Play)
        {
            var pcInit = Object.FindObjectOfType<ThirdPersonUserControl>();
            float ppmInit = 1.3f;
            ui.mapContent.sizeDelta = new Vector2(worldSize.x * ppmInit, worldSize.y * ppmInit);
            if (pcInit != null)
                ui.mapContent.anchoredPosition = -new Vector2((pcInit.transform.position.x - worldCenter.x) * ppmInit, (pcInit.transform.position.z - worldCenter.y) * ppmInit);
        }

        var mini = ui.miniMap.gameObject.AddComponent<MinimapController>();
        mini.worldCenter = worldCenter; mini.worldSize = worldSize;
        mini.panel = ui.miniMap; mini.maskImage = ui.maskImage; mini.mapContent = ui.mapContent; mini.icons = ui.icons;
        mini.playerArrow = ui.playerArrow; mini.objectiveIcon = ui.objectiveIcon; mini.compassN = ui.compassN; mini.ring = ui.ring;
        mini.circleSprite = circle; mini.squareSprite = square; mini.enemySprite = enemy; mini.pickupSprite = plusIcon;

        CombatPolishBuilder.Apply(scene);
        GrenadeBuilder.Apply(scene);
        AmmoHudBuilder.Apply(scene);
        DeathCutsceneBuilder.Apply(scene);
        DialogueBuilder.Apply(scene);
        DayNightBuilder.Apply(scene);
        AddToBuildSettings(NightScene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Mission] Map " + Mathf.RoundToInt(worldSize.x) + "x" + Mathf.RoundToInt(worldSize.y) + " m; hostage room = '" + roomName + "' at " + room.center);
        EditorUtility.DisplayDialog("Minimap + mission ready",
            "Mini-map top-right (M = full map).\n3 separate rooms: " + roomName + "\nDoors: press E.\nHostages: press E next to them.\n\nIf the room is not where you want it, move the 'HostageRoom' object in ====== MISSION ======.", "OK");
    }

    // ================= map baking =================
    static Texture2D BakeMap(Scene scene, Vector2 center, Vector2 size)
    {
        int w = 2048;
        int h = Mathf.Clamp(Mathf.RoundToInt(w * size.y / size.x), 256, 4096);

        // hide everything that should not appear on the map
        var hidden = new List<GameObject>();
        foreach (var t in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(false)).ToList())
        {
            if (t == null) continue;
            string n = t.name;
            if (n == "====== ENEMIES ======" || n == "====== PLAYER ======" || n == "====== WEATHER & AUDIO ======" || n == "====== UI ======" || n == "====== MISSION ======" || n.StartsWith("Bgr_"))
            { if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); hidden.Add(t.gameObject); } }
        }
        var lights = Object.FindObjectsOfType<Light>(true).Where(l => l.enabled).ToList();
        foreach (var l in lights) l.enabled = false;

        var sunGo = new GameObject("TEMP_MapSun");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1.15f; sun.color = Color.white;
        sunGo.transform.rotation = Quaternion.Euler(62f, 38f, 0f);
        sun.shadows = LightShadows.Soft;

        var oldAmbMode = RenderSettings.ambientMode; var oldAmb = RenderSettings.ambientLight;
        bool oldFog = RenderSettings.fog; float oldShadowDist = QualitySettings.shadowDistance;
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.66f);
        RenderSettings.fog = false;
        QualitySettings.shadowDistance = 1400f;

        var camGo = new GameObject("TEMP_MapCam");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = size.y * 0.5f;
        cam.aspect = size.x / size.y;
        cam.transform.position = new Vector3(center.x, 650f, center.y);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // looks down, image-up = world +Z, image-right = world +X
        cam.nearClipPlane = 1f; cam.farClipPlane = 1600f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.03f, 0.05f, 0.08f);
        cam.renderingPath = RenderingPath.Forward;
        cam.allowHDR = false; cam.allowMSAA = false;

        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        // restore the scene
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(sunGo);
        foreach (var l in lights) if (l != null) l.enabled = true;
        foreach (var g in hidden) if (g != null) g.SetActive(true);
        RenderSettings.ambientMode = oldAmbMode; RenderSettings.ambientLight = oldAmb; RenderSettings.fog = oldFog;
        QualitySettings.shadowDistance = oldShadowDist;

        // tactical tint: dark blue-green monochrome with a bit of contrast
        var px = tex.GetPixels32();
        for (int i = 0; i < px.Length; i++)
        {
            float lum = (0.299f * px[i].r + 0.587f * px[i].g + 0.114f * px[i].b) / 255f;
            lum = Mathf.Clamp01(Mathf.Pow(lum, 0.85f) * 1.15f);
            px[i] = new Color32((byte)((lum * 0.55f + 0.03f) * 255), (byte)((lum * 0.82f + 0.06f) * 255), (byte)((lum * 0.95f + 0.10f) * 255), 255);
        }
        tex.SetPixels32(px); tex.Apply();

        string path = UiFolder + "/Map_SunTemple.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Default;
        imp.mipmapEnabled = false; imp.wrapMode = TextureWrapMode.Clamp;
        imp.maxTextureSize = 4096; imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.npotScale = TextureImporterNPOTScale.None;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ================= hostages =================
    // ---------------------------------------------------------------------------------------------
    // THREE SEPARATE ROOMS - one captive teammate in each (Duaa, Zubia, Maha).
    //  1. rooms you marked yourself in Play mode (F10 at a door: 1st press = Duaa, 2nd = Zubia, 3rd = Maha)
    //  2. otherwise automatic: the rooms behind the doors closest to your start, at least 22 m apart
    // ---------------------------------------------------------------------------------------------
    static bool RoomBehindDoor(Bounds cb, Vector3 knownInward, out Vector3 spot, out float score)
    {
        spot = Vector3.zero; score = -1f;
        Vector3 c = cb.center;
        Vector3 n = cb.size.x < cb.size.z ? Vector3.right : Vector3.forward;          // door normal (thin axis)
        Vector3 lat = Vector3.Cross(Vector3.up, n);
        float baseY = cb.min.y;
        bool known = knownInward.sqrMagnitude > 0.01f;

        var temp = AddTempColliders(new Bounds(c, new Vector3(50f, 30f, 50f)));
        float[] bestS = { -1f, -1f }; var bestP = new Vector3[2];
        for (int side = 0; side < 2; side++)
        {
            float sgn = side == 0 ? 1f : -1f;
            if (known && Vector3.Dot(n * sgn, knownInward) < 0f) continue;                // only the side you walked into
            foreach (float dist in new[] { 3.5f, 4.5f, 5.5f, 6.5f, 8f })
                foreach (float l in new[] { -3f, -1.5f, 0f, 1.5f, 3f })
                {
                    Vector3 probe = c + n * sgn * dist + lat * l;
                    if (!Physics.Raycast(new Vector3(probe.x, baseY + 1.6f, probe.z), Vector3.down, out RaycastHit fh, 3.5f)) continue;
                    if (fh.normal.y < 0.85f || Mathf.Abs(fh.point.y - baseY) > 1.1f) continue;
                    float s = Enclosure(fh.point) - Mathf.Abs(l) * 0.4f;
                    if (s > bestS[side]) { bestS[side] = s; bestP[side] = fh.point; }
                }
        }
        int inside = bestS[0] >= bestS[1] ? 0 : 1;
        bool ok;
        if (known)
        {
            ok = true;
            if (bestS[inside] >= 0f) { spot = bestP[inside]; score = bestS[inside]; }
            else
            {
                Vector3 inward = n * (Vector3.Dot(n, knownInward) >= 0f ? 1f : -1f);
                spot = new Vector3(c.x, baseY + 0.02f, c.z) + inward * 4.5f; score = 0f;   // no walls detected: 4.5 m inside the door
            }
        }
        else
        {
            ok = bestS[inside] >= 0f && bestS[1 - inside] < bestS[inside] - 6f;           // clearly one side is a room, the other is open
            if (ok) { spot = bestP[inside]; score = bestS[inside]; }
        }
        foreach (var mc in temp) if (mc != null) Object.DestroyImmediate(mc);
        return ok;
    }

    static Vector3[] FindThreeRooms(out string info)
    {
        var pc = Object.FindObjectOfType<ThirdPersonUserControl>();
        Vector3 start = pc != null ? pc.transform.position : Vector3.zero;
        // slot 0 = Duaa's room, slot 1 = Zubia's room, slot 2 = Maha's room
        var slots = new Vector3?[3];
        var notes = new List<string>();
        string[] who = { "Duaa", "Zubia", "Maha" };
        System.Func<List<Vector3>> have = () => slots.Where(s => s.HasValue).Select(s => s.Value).ToList();
        System.Func<int> firstFree = () => System.Array.FindIndex(slots, s => !s.HasValue);

        var allDoors = Object.FindObjectsOfType<SunTemple.Door>();

        // 1. the rooms you marked yourself: the Nth F10 press belongs to the Nth teammate
        var saved = RescueDoorMarker.Load();
        for (int i = 0; i < saved.Length && i < 3; i++)
        {
            var data = saved[i];
            if (data.pinned)
            {
                // a room that is fixed: use the exact spot, snapped to the real floor
                Vector3 ps = data.spot;
                var tmp = AddTempColliders(new Bounds(ps, new Vector3(30f, 20f, 30f)));
                if (Physics.Raycast(ps + Vector3.up * 1.2f, Vector3.down, out RaycastHit ph, 3f) && ph.normal.y > 0.85f) ps = ph.point;
                foreach (var mc in tmp) if (mc != null) Object.DestroyImmediate(mc);
                slots[i] = ps; notes.Add(who[i] + " -> fixed room at " + Vector3Int.RoundToInt(ps));
                continue;
            }
            SunTemple.Door door = null; Bounds db = new Bounds(); float bestD = 7f;
            foreach (var d in allDoors)
            {
                Bounds bb; RescueDoorMarker.DoorBounds(d, out bb);
                float dist = Vector3.Distance(bb.center, data.door);
                if (dist < bestD) { bestD = dist; door = d; db = bb; }
            }
            if (door == null) { notes.Add(who[i] + ": saved door '" + data.doorName + "' not found"); continue; }
            Vector3 sp; float sc;
            if (RoomBehindDoor(db, data.forward, out sp, out sc)) { slots[i] = sp; notes.Add(who[i] + " -> your door " + door.name + " at " + Vector3Int.RoundToInt(db.center)); }
        }

        // 2. automatic rooms behind the nearest doors for the teammates that have no saved door
        if (firstFree() >= 0)
        {
            var doors = allDoors.Select(d => { Bounds bb; RescueDoorMarker.DoorBounds(d, out bb); return new { d, bb }; })
                                .OrderBy(x => (x.bb.center - start).sqrMagnitude).Take(20).ToList();
            var cands = new List<(Vector3 spot, float score, string name, float dist)>();
            foreach (var x in doors)
            {
                Vector3 sp; float sc;
                if (RoomBehindDoor(x.bb, Vector3.zero, out sp, out sc)) cands.Add((sp, sc, x.d.name, Vector3.Distance(x.bb.center, start)));
            }
            for (int i = 0; i < cands.Count; i++)
                Debug.Log("[Mission] room candidate " + i + ": door '" + cands[i].name + "' at " + Vector3Int.RoundToInt(cands[i].spot) + ", " + Mathf.RoundToInt(cands[i].dist) + " m from you, score " + cands[i].score.ToString("0"));

            int rot = cands.Count > 0 ? Mathf.Abs(EditorPrefs.GetInt(CandidatePref, 0)) % cands.Count : 0;
            cands = cands.Skip(rot).Concat(cands.Take(rot)).ToList();
            foreach (var cd in cands)
            {
                int slot = firstFree();
                if (slot < 0) break;
                if (have().All(ch => Vector3.Distance(ch, cd.spot) > 22f)) { slots[slot] = cd.spot; notes.Add(who[slot] + " -> auto door " + cd.name); }
            }
        }

        // 3. a furnished temple room as another fallback
        if (firstFree() >= 0)
        {
            Bounds fb; Vector3 ff; Vector3[] fsp; string finfo;
            if (FindFurnishedRoom(out fb, out ff, out fsp, out finfo) && have().All(ch => Vector3.Distance(ch, ff) > 22f)) { int slot = firstFree(); slots[slot] = ff; notes.Add(who[slot] + " -> " + finfo); }
        }
        // 4. last resort: spread them out so they are at least not in the same spot
        while (firstFree() >= 0)
        {
            int slot = firstFree();
            var h = have();
            slots[slot] = h.Count > 0 ? h[0] + new Vector3(10f * (slot + 1), 0f, 8f) : start + new Vector3(0, 0, 60);
            notes.Add(who[slot] + " -> placeholder (not enough rooms found - press F10 at her door)");
        }
        info = string.Join(" | ", notes.ToArray());
        return new[] { slots[0].Value, slots[1].Value, slots[2].Value };
    }

    // ---------------------------------------------------------------------------------------------
    // The room YOU chose: in Play mode stand in front of the door and press F10 (see RescueDoorMarker). That door and the
    // direction you faced are saved to RescueDoor.json; here we put the captives in the space behind that door.
    // ---------------------------------------------------------------------------------------------
    static bool FindRoomFromSavedDoor(out Bounds b, out Vector3 floor, out Vector3[] spots, out string info)
    {
        b = new Bounds(Vector3.zero, Vector3.zero); floor = Vector3.zero; spots = null; info = "";
        string path = RescueDoorMarker.FilePath;
        if (!File.Exists(path)) { info = "no saved door"; return false; }
        RescueDoorData data;
        try { data = JsonUtility.FromJson<RescueDoorData>(File.ReadAllText(path)); }
        catch { info = "RescueDoor.json is unreadable"; return false; }
        if (data == null) { info = "RescueDoor.json is empty"; return false; }

        // the door in this scene closest to the saved one
        SunTemple.Door door = null; Bounds db = new Bounds(); float bestD = 7f;
        foreach (var d in Object.FindObjectsOfType<SunTemple.Door>())
        {
            Bounds bb; RescueDoorMarker.DoorBounds(d, out bb);
            float dist = Vector3.Distance(bb.center, data.door);
            if (dist < bestD) { bestD = dist; door = d; db = bb; }
        }
        if (door == null) { info = "the saved door is not in this scene any more"; return false; }

        Vector3 axis = db.size.x < db.size.z ? Vector3.right : Vector3.forward;      // door normal (thin axis)
        Vector3 inward = axis * (Vector3.Dot(axis, data.forward) >= 0f ? 1f : -1f);   // the way you walked through it
        Vector3 lat = Vector3.Cross(Vector3.up, inward);
        float baseY = db.min.y;
        Vector3 c = db.center;

        var temp = AddTempColliders(new Bounds(c, new Vector3(50f, 30f, 50f)));
        float bestS = -1f; Vector3 bestP = Vector3.zero;
        foreach (float dist in new[] { 3.5f, 4.5f, 5.5f, 6.5f, 8f })
            foreach (float l in new[] { -3f, -1.5f, 0f, 1.5f, 3f })
            {
                Vector3 probe = c + inward * dist + lat * l;
                if (!Physics.Raycast(new Vector3(probe.x, baseY + 1.6f, probe.z), Vector3.down, out RaycastHit fh, 3.5f)) continue;
                if (fh.normal.y < 0.85f || Mathf.Abs(fh.point.y - baseY) > 1.1f) continue;
                float s = Enclosure(fh.point) - Mathf.Abs(l) * 0.4f;                   // a little preference for straight ahead
                if (s > bestS) { bestS = s; bestP = fh.point; }
            }
        bool enclosed = bestS >= 0f;
        Vector3 p = enclosed ? bestP : new Vector3(c.x, baseY + 0.02f, c.z) + inward * 4.5f;

        spots = new Vector3[HostageCount];
        for (int i = 0; i < HostageCount; i++)
        {
            Vector3 want = p + inward * ((i / 2) * 1.6f - 0.8f) + lat * ((i % 2 - 0.5f) * 1.6f);
            spots[i] = new Vector3(want.x, p.y, want.z);
            if (Physics.Raycast(want + Vector3.up, Vector3.down, out RaycastHit h2, 2.2f) && h2.normal.y > 0.85f && Mathf.Abs(h2.point.y - p.y) < 0.8f)
                spots[i] = h2.point;
        }
        foreach (var mc in temp) if (mc != null) Object.DestroyImmediate(mc);

        floor = p;
        b = new Bounds(p + Vector3.up * 3f, new Vector3(14f, 8f, 14f));
        info = "room behind YOUR door '" + door.name + "' at " + Vector3Int.RoundToInt(c) + (enclosed ? "" : " (no walls detected - placed 4.5 m inside)");
        return true;
    }

    [MenuItem("GAMECON/Rescue Room: Forget Saved Door")]
    public static void ForgetSavedDoor()
    {
        if (File.Exists(RescueDoorMarker.FilePath)) File.Delete(RescueDoorMarker.FilePath);
        Debug.Log("[Mission] saved rescue door removed.");
    }

    // ---------------------------------------------------------------------------------------------
    // Rescue room BEHIND A DOOR: find the closest temple doors to the player's start, work out which side of each
    // door is the inside (roof + walls around), and put the captives in the enclosed space behind it.
    // So the story works: reach the building, open the door (E), go in, free your teammates.
    // GAMECON > "Rescue Room: Try Next Door" cycles to the next candidate if you prefer another building.
    // ---------------------------------------------------------------------------------------------
    const string CandidatePref = "GAMECON_RoomCandidate";

    static bool FindRoomBehindDoor(out Bounds b, out Vector3 floor, out Vector3[] spots, out string info)
    {
        b = new Bounds(Vector3.zero, Vector3.zero); floor = Vector3.zero; spots = null; info = "";
        var pc = Object.FindObjectOfType<ThirdPersonUserControl>();
        Vector3 anchor = pc != null ? pc.transform.position : Vector3.zero;

        var doors = Object.FindObjectsOfType<SunTemple.Door>()
            .Select(d => new { d, col = d.GetComponent<BoxCollider>() })
            .Where(x => x.col != null)
            .OrderBy(x => (x.col.bounds.center - anchor).sqrMagnitude)
            .Take(16).ToList();
        if (doors.Count == 0) { info = "no doors in the scene"; return false; }

        // pass 1: evaluate every nearby door; pass 2: take the Nth valid one
        var valid = new List<(float score, Vector3 door, Vector3 best, Vector3[] spots, string name, float dist)>();
        foreach (var x in doors)
        {
            var cb = x.col.bounds; Vector3 c = cb.center;
            Vector3 n = cb.size.x < cb.size.z ? Vector3.right : Vector3.forward;       // thin axis = door normal
            Vector3 lat = Vector3.Cross(Vector3.up, n);
            var temp = AddTempColliders(new Bounds(c, new Vector3(50f, 30f, 50f)));

            float[] bestS = { -1f, -1f }; Vector3[] bestP = { Vector3.zero, Vector3.zero };
            for (int side = 0; side < 2; side++)
            {
                float sgn = side == 0 ? 1f : -1f;
                foreach (float dist in new[] { 3f, 4.5f, 6f, 7.5f })
                    foreach (float l in new[] { -3f, 0f, 3f })
                    {
                        Vector3 probe = c + n * sgn * dist + lat * l;
                        if (!Physics.Raycast(new Vector3(probe.x, cb.min.y + 1.6f, probe.z), Vector3.down, out RaycastHit fh, 3.5f)) continue;
                        if (fh.normal.y < 0.85f || Mathf.Abs(fh.point.y - cb.min.y) > 1.1f) continue;   // same level as the door
                        float s = Enclosure(fh.point);
                        if (s > bestS[side]) { bestS[side] = s; bestP[side] = fh.point; }
                    }
            }
            int inside = bestS[0] >= bestS[1] ? 0 : 1;
            if (bestS[inside] >= 0f && bestS[1 - inside] < bestS[inside] - 6f)         // clearly one side is a room, the other is open
            {
                Vector3 p = bestP[inside];
                var sp = new Vector3[HostageCount];
                for (int i = 0; i < HostageCount; i++)
                {
                    Vector3 want = p + new Vector3((i % 2 - 0.5f) * 1.6f, 0, (i / 2 - 0.5f) * 1.6f);
                    sp[i] = p;
                    if (Physics.Raycast(want + Vector3.up, Vector3.down, out RaycastHit h2, 2.2f) && h2.normal.y > 0.85f
                        && Physics.Raycast(h2.point + Vector3.up * 0.1f, Vector3.up, 8f))
                        sp[i] = h2.point;
                }
                valid.Add((bestS[inside], c, p, sp, x.d.name, Vector3.Distance(c, anchor)));
            }
            foreach (var mc in temp) if (mc != null) Object.DestroyImmediate(mc);
        }

        if (valid.Count == 0) { info = "no door with an enclosed room behind it near the player"; return false; }
        for (int i = 0; i < valid.Count; i++)
            Debug.Log("[Mission] room candidate " + i + ": door '" + valid[i].name + "' at " + Vector3Int.RoundToInt(valid[i].door) + ", " + Mathf.RoundToInt(valid[i].dist) + " m from you, room score " + valid[i].score.ToString("0"));

        int idx = EditorPrefs.GetInt(CandidatePref, 0);
        if (idx >= valid.Count || idx < 0) idx = 0;
        var pick = valid[idx];
        floor = pick.best; spots = pick.spots;
        b = new Bounds(pick.best + Vector3.up * 3f, new Vector3(14f, 8f, 14f));
        info = "room behind door " + idx + "/" + (valid.Count - 1) + " at " + Vector3Int.RoundToInt(pick.door) + " (open it with E)";
        return true;
    }

    [MenuItem("GAMECON/Rescue Room: Try Next Door")]
    public static void NextCandidate()
    {
        EditorPrefs.SetInt(CandidatePref, EditorPrefs.GetInt(CandidatePref, 0) + 1);
        Build();
    }

    [MenuItem("GAMECON/Rescue Room: Back To First Door")]
    public static void FirstCandidate()
    {
        EditorPrefs.SetInt(CandidatePref, 0);
        Build();
    }

    // ---------------------------------------------------------------------------------------------
    // The temple's rooms are furnished by "InteriorObjects_Temple*" groups (columns, rugs, beds, tables...).
    // Everything in such a group is by definition INSIDE the building, so we put the captives on the ground
    // floor of the biggest furnished room, in a free 2x2 spot between the furniture.
    // ---------------------------------------------------------------------------------------------
    static bool FindFurnishedRoom(out Bounds b, out Vector3 floor, out Vector3[] spots, out string info)
    {
        b = new Bounds(Vector3.zero, Vector3.zero); floor = Vector3.zero; spots = null; info = "";
        string[] priority = { "InteriorObjects_TempleWall_B", "InteriorObjects_TempleWall_A", "InteriorObjects_TempleTower" };

        foreach (string gname in priority)
        {
            var group = Object.FindObjectsOfType<Transform>().FirstOrDefault(t => t.name == gname);
            if (group == null) continue;
            var kids = group.GetComponentsInChildren<Transform>().Where(t => t != group && t.name != "NightLight").ToList();
            if (kids.Count < 10) continue;

            // ground floor level = lowest level with a decent number of objects
            var levels = kids.GroupBy(k => Mathf.RoundToInt(k.position.y * 2f)).Where(g => g.Count() >= 8).OrderBy(g => g.Key).ToList();
            if (levels.Count == 0) continue;
            float groundY = levels[0].Key / 2f;
            var ground = kids.Where(k => k.position.y < groundY + 1.6f).ToList();
            var hullPts = ground.Select(k => new Vector2(k.position.x, k.position.z)).ToList();
            float hx0 = hullPts.Min(p => p.x), hx1 = hullPts.Max(p => p.x), hz0 = hullPts.Min(p => p.y), hz1 = hullPts.Max(p => p.y);
            Vector2 hc = new Vector2((hx0 + hx1) * 0.5f, (hz0 + hz1) * 0.5f);

            // obstacles: everything that is not a flat rug / decal
            var obst = ground.Where(k => !k.name.StartsWith("Prop_Rug") && !k.name.StartsWith("Dec_"))
                             .Select(k => new Vector2(k.position.x, k.position.z)).ToList();

            const float sp = 1.6f, clear = 1.3f, margin = 2.0f;
            float bestScore = float.MaxValue; Vector2 bestC = Vector2.zero; bool found = false;
            for (float cx = hx0 + margin + sp * 0.5f; cx <= hx1 - margin - sp * 0.5f; cx += 0.5f)
                for (float cz = hz0 + margin + sp * 0.5f; cz <= hz1 - margin - sp * 0.5f; cz += 0.5f)
                {
                    bool free = true; float minD = 99f;
                    for (int i = 0; i < 4 && free; i++)
                    {
                        var s = new Vector2(cx + (i % 2 - 0.5f) * sp, cz + (i / 2 - 0.5f) * sp);
                        foreach (var o in obst)
                        {
                            float d = Vector2.Distance(s, o);
                            if (d < clear) { free = false; break; }
                            if (d < minD) minD = d;
                        }
                    }
                    if (!free) continue;
                    float score = Vector2.Distance(new Vector2(cx, cz), hc) - Mathf.Min(minD, 3f) * 0.8f;   // central and roomy
                    if (score < bestScore) { bestScore = score; bestC = new Vector2(cx, cz); found = true; }
                }
            if (!found) continue;

            floor = new Vector3(bestC.x, groundY, bestC.y);
            spots = new Vector3[HostageCount];
            for (int i = 0; i < HostageCount; i++)
                spots[i] = new Vector3(bestC.x + (i % 2 - 0.5f) * sp, groundY, bestC.y + (i / 2 - 0.5f) * sp);
            b = new Bounds(new Vector3(hc.x, groundY + 3f, hc.y), new Vector3(hx1 - hx0, 8f, hz1 - hz0));
            info = gname + " (inside the temple), floor y=" + groundY + ", room " + Mathf.RoundToInt(hx1 - hx0) + "x" + Mathf.RoundToInt(hz1 - hz0) + " m";
            return true;
        }
        info = "no furnished temple room found";
        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // Finds a room INSIDE the main temple complex: probes the floors around the temple towers / interior
    // objects (the temple lives around x 100..230, z -100..-230 in this level), keeps the most enclosed
    // spot (roof + walls on all sides) and lays out 4 spots on its floor.
    // ---------------------------------------------------------------------------------------------
    static bool FindTempleRoom(out Bounds b, out Vector3 floor, out Vector3[] spots, out string info)
    {
        b = new Bounds(Vector3.zero, Vector3.zero); floor = Vector3.zero; spots = null; info = "";

        var anchors = new List<Vector3>();
        foreach (var t in Object.FindObjectsOfType<Transform>())
        {
            string n = t.name;
            bool isAnchor = n.StartsWith("InteriorObjects_Temple") ||
                            System.Text.RegularExpressions.Regex.IsMatch(n, @"^Bld_TempleTower_[AB]( \(\d+\))?$");
            if (!isAnchor) continue;
            Vector3 p = t.position;
            if (anchors.Any(a => (a - p).sqrMagnitude < 8f * 8f)) continue;
            anchors.Add(p);
        }
        if (anchors.Count == 0) { info = "no temple objects found"; return false; }

        float best = -1f; Vector3 bestP = Vector3.zero; Bounds bestRegion = b; int probed = 0, enclosed = 0;
        Vector3[] bestSpots = null;

        foreach (var a in anchors)
        {
            var region = new Bounds(a + Vector3.up * 8f, new Vector3(40f, 34f, 40f));
            var temp = AddTempColliders(region);
            float lowY = region.min.y;
            for (float x = region.min.x; x <= region.max.x; x += 2f)
                for (float z = region.min.z; z <= region.max.z; z += 2f)
                {
                    var hits = Physics.RaycastAll(new Vector3(x, region.max.y, z), Vector3.down, region.size.y + 4f);
                    foreach (var h in hits)
                    {
                        if (h.normal.y < 0.85f) continue;                                    // floors only
                        Vector3 p = h.point;
                        if (Physics.Raycast(p + Vector3.up * 0.1f, Vector3.up, 1.9f)) continue; // headroom
                        probed++;
                        float s = Enclosure(p);
                        if (s < 0f) continue;
                        enclosed++;
                        float centre = 1f - Mathf.Clamp01(Vector2.Distance(new Vector2(p.x, p.z), new Vector2(a.x, a.z)) / 18f);
                        s += centre * 4f - (p.y - lowY) * 0.05f;                          // slight preference for lower storeys
                        if (s > best)
                        {
                            // lay out 4 spots around p, each on a floor with a roof overhead
                            var sp = new Vector3[HostageCount];
                            bool ok = true;
                            for (int i = 0; i < HostageCount; i++)
                            {
                                Vector3 want = p + new Vector3((i % 2 - 0.5f) * 1.6f, 0, (i / 2 - 0.5f) * 1.6f);
                                sp[i] = p;
                                if (Physics.Raycast(want + Vector3.up * 1.0f, Vector3.down, out RaycastHit fh, 2.2f) && fh.normal.y > 0.85f
                                    && Physics.Raycast(fh.point + Vector3.up * 0.1f, Vector3.up, 8f))
                                    sp[i] = fh.point;
                                else if (i == 0) ok = false;
                            }
                            if (!ok) continue;
                            best = s; bestP = p; bestRegion = region; bestSpots = sp;
                        }
                    }
                }
            foreach (var mc in temp) if (mc != null) Object.DestroyImmediate(mc);
        }
        Debug.Log("[Mission] temple room search: " + anchors.Count + " temple anchors, probed " + probed + " floor points, " + enclosed + " enclosed");
        if (best < 0f) { info = "no enclosed room found around the temple"; return false; }

        floor = bestP; b = new Bounds(bestP + Vector3.up * 3f, new Vector3(14f, 8f, 14f)); spots = bestSpots;
        info = "temple room near " + Vector3Int.RoundToInt(bestRegion.center - Vector3.up * 8f);
        return true;
    }

    // Picks a point that is really INSIDE a building: it needs a roof overhead and walls on (almost) every side.
    // The temple meshes have no colliders, so temporary MeshColliders are added around each candidate volume while we probe.
    static string FindHostageRoom(NavMeshTriangulation tri, out Bounds b, out Vector3 floor, out float areaOut)
    {
        string name = "";
        b = new Bounds(Vector3.zero, Vector3.zero);
        floor = Vector3.zero;
        areaOut = 0f;
        var v = tri.vertices; var ix = tri.indices;

        float bestScore = -1f; string bestName = ""; Bounds bestBounds = b; Vector3 bestFloor = Vector3.zero;
        float fbArea = 0f; string fbName = ""; Bounds fbBounds = b; Vector3 fbFloor = Vector3.zero;   // fallback: biggest walkable floor
        int probed = 0, enclosed = 0;

        foreach (var col in Object.FindObjectsOfType<BoxCollider>(true))
        {
            if (!col.gameObject.name.StartsWith("InteriorOcclusionArea")) continue;
            var cb = col.bounds;

            // walkable points inside this volume: one per 2.5 m cell
            var cells = new Dictionary<Vector2Int, Vector3>();
            var area = new Dictionary<int, float>(); var sum = new Dictionary<int, Vector3>();
            for (int i = 0; i + 2 < ix.Length; i += 3)
            {
                Vector3 p0 = v[ix[i]], p1 = v[ix[i + 1]], p2 = v[ix[i + 2]];
                Vector3 c = (p0 + p1 + p2) / 3f;
                if (c.x < cb.min.x || c.x > cb.max.x || c.z < cb.min.z || c.z > cb.max.z || c.y < cb.min.y - 0.5f || c.y > cb.max.y) continue;
                var key2 = new Vector2Int(Mathf.RoundToInt(c.x / 2.5f), Mathf.RoundToInt(c.z / 2.5f) + 100000 * Mathf.RoundToInt(c.y / 3f));
                if (!cells.ContainsKey(key2)) cells[key2] = c;
                float a = Vector3.Cross(p1 - p0, p2 - p0).magnitude * 0.5f;
                int lvl = Mathf.RoundToInt(c.y / 1.5f);
                area.TryGetValue(lvl, out float av); area[lvl] = av + a;
                sum.TryGetValue(lvl, out Vector3 sv); sum[lvl] = sv + c * a;
            }
            if (cells.Count == 0) continue;
            int bestLvl = area.OrderByDescending(k => k.Value).First().Key;
            if (area[bestLvl] > fbArea) { fbArea = area[bestLvl]; fbName = col.gameObject.name; fbBounds = cb; fbFloor = sum[bestLvl] / area[bestLvl]; }

            // temporary colliders for every visible mesh around this volume
            var temp = AddTempColliders(cb);
            foreach (var pt in cells.Values)
            {
                probed++;
                float s = Enclosure(pt);
                if (s < 0f) continue;
                enclosed++;
                // prefer points near the middle of the volume
                float centre = 1f - Mathf.Clamp01(Vector2.Distance(new Vector2(pt.x, pt.z), new Vector2(cb.center.x, cb.center.z)) / 12f);
                s += centre * 4f;
                if (s > bestScore) { bestScore = s; bestName = col.gameObject.name; bestBounds = cb; bestFloor = pt; }
            }
            foreach (var mc in temp) if (mc != null) Object.DestroyImmediate(mc);
        }
        Debug.Log("[Mission] room search: probed " + probed + " floor points, " + enclosed + " are enclosed (roof + walls)");

        if (bestScore >= 0f)
        {
            b = bestBounds; floor = bestFloor; areaOut = Mathf.Max(fbArea, 1f); name = bestName + " (enclosed room)";
            return name;
        }
        if (fbArea > 0f)
        {
            b = fbBounds; floor = fbFloor; areaOut = fbArea; name = fbName + " (no enclosed room found - biggest floor)";
            return name;
        }
        var pc = Object.FindObjectOfType<ThirdPersonUserControl>();
        floor = (pc != null ? pc.transform.position : Vector3.zero) + new Vector3(0, 0, 60);
        b = new Bounds(floor + Vector3.up * 3, new Vector3(12, 8, 12));
        return "(none found - use GAMECON > Move Rescue Room To Scene View)";
    }

    // < 0 : not a room. Higher = more enclosed and roomier.
    static float Enclosure(Vector3 floorPoint)
    {
        Vector3 o = floorPoint + Vector3.up * 1.2f;
        if (!Physics.Raycast(o, Vector3.up, out RaycastHit up, 14f)) return -1f;      // open sky
        if (up.distance + 1.2f < 2.4f) return -1f;                                       // crawl space
        int hits = 0; float sum = 0f, minD = 99f;
        for (int k = 0; k < 12; k++)
        {
            float ang = k * 30f * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            if (Physics.Raycast(o, dir, out RaycastHit h, 16f)) { hits++; sum += h.distance; if (h.distance < minD) minD = h.distance; }
        }
        if (hits < 10) return -1f;                                                       // open on several sides
        float mean = sum / hits;
        if (minD < 1.2f || mean < 2.5f) return -1f;                                      // closet / too cramped for 4 people
        return hits * 5f + Mathf.Min(minD, 4f) * 6f + Mathf.Min(mean, 7f) * 2f;
    }

    static List<MeshCollider> AddTempColliders(Bounds region)
    {
        var list = new List<MeshCollider>();
        var r = region; r.Expand(16f);
        foreach (var mr in Object.FindObjectsOfType<MeshRenderer>())
        {
            if (!mr.enabled || !mr.gameObject.activeInHierarchy) continue;
            string root = mr.transform.root.name;
            if (root.StartsWith("======") && root != "====== ENVIRONMENT ======") continue;   // not enemies / player / mission objects
            if (!mr.bounds.Intersects(r) || mr.bounds.size.magnitude > 120f) continue;
            if (mr.GetComponent<Collider>() != null) continue;
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var mc = mr.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            list.Add(mc);
        }
        Physics.SyncTransforms();
        return list;
    }

    static Bounds RenderBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Mesh _ropeMesh;
    static Mesh RopeMesh()
    {
        if (_ropeMesh != null) return _ropeMesh;
        const string path = "Assets/Environment/Night/Rope_Torus.asset";
        Directory.CreateDirectory(EnvFolder);
        AssetDatabase.DeleteAsset(path);
        const int seg = 28, sides = 8; const float R = 1f, r = 0.07f;
        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float u = (float)i / seg * Mathf.PI * 2f;
            Vector3 centre = new Vector3(Mathf.Cos(u) * R, 0, Mathf.Sin(u) * R);
            for (int j = 0; j <= sides; j++)
            {
                float w = (float)j / sides * Mathf.PI * 2f;
                Vector3 n = new Vector3(Mathf.Cos(u) * Mathf.Cos(w), Mathf.Sin(w), Mathf.Sin(u) * Mathf.Cos(w));
                verts.Add(centre + n * r); norms.Add(n); uvs.Add(new Vector2((float)i / seg * 12f, (float)j / sides));
            }
        }
        for (int i = 0; i < seg; i++)
            for (int j = 0; j < sides; j++)
            {
                int a = i * (sides + 1) + j, bb = (i + 1) * (sides + 1) + j;
                tris.AddRange(new[] { a, bb, a + 1, bb, bb + 1, a + 1 });
            }
        var m = new Mesh { name = "Rope_Torus" };
        m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
        AssetDatabase.CreateAsset(m, path);
        _ropeMesh = m;
        return m;
    }

    static Material RopeMaterial()
    {
        const string path = "Assets/Environment/Night/Rope.mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(Shader.Find("Standard"));
        m.color = new Color(0.42f, 0.30f, 0.14f);
        m.SetFloat("_Glossiness", 0.1f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static GameObject MakeRope(Transform parent, string name, Vector3 worldPos, float radius, Quaternion worldRot, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(worldPos, worldRot);
        // unit torus has major radius 1 -> scale to the wanted radius, compensating the bone's own scale
        float s = Mathf.Max(0.0001f, parent.lossyScale.x);
        go.transform.localScale = Vector3.one * (radius / s);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    // crouched "captive" animation (falls back to the idle clip)
    static RuntimeAnimatorController CaptiveController()
    {
        const string path = "Assets/Environment/Night/Hostage_Captive.controller";
        AssetDatabase.DeleteAsset(path);
        AnimationClip clip = null;
        foreach (string fbx in new[] { "Assets/scgFullBodyController/Animations/HumanoidCrouch.fbx", "Assets/scgFullBodyController/Animations/HumanoidIdle.fbx" })
        {
            clip = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip != null) break;
        }
        var ctrl = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(path);
        var state = ctrl.layers[0].stateMachine.AddState("Captive");
        state.motion = clip;
        ctrl.layers[0].stateMachine.defaultState = state;
        return ctrl;
    }

    static List<Hostage> BuildHostages(Scene scene, Transform group, Bounds room, Vector3 floor, Vector3[] presetSpots, Sprite beaconSprite, out Transform roomMarker)
    {
        // 4 teammates in a tight 2x2 cluster on the walkable floor, all inside the same room
        const float spacing = 1.6f;
        var spots = new List<Vector3>();
        if (presetSpots != null && presetSpots.Length == HostageCount) spots.AddRange(presetSpots);
        else
        for (int i = 0; i < HostageCount; i++)
        {
            Vector3 want = floor + new Vector3((i % 2 - 0.5f) * spacing, 0, (i / 2 - 0.5f) * spacing);
            Vector3 p = floor;
            if (NavMesh.SamplePosition(want, out NavMeshHit nh, 1.5f, NavMesh.AllAreas)
                && Mathf.Abs(nh.position.y - floor.y) < 1.2f
                && nh.position.x > room.min.x && nh.position.x < room.max.x && nh.position.z > room.min.z && nh.position.z < room.max.z)
                p = nh.position;
            spots.Add(p);
        }
        // if two ended up on the same spot, nudge them apart
        for (int i = 1; i < spots.Count; i++)
            for (int j = 0; j < i; j++)
                if ((spots[i] - spots[j]).sqrMagnitude < 0.5f * 0.5f) spots[i] += new Vector3(0.7f * i, 0, 0.5f);

        var roomGo = new GameObject("HostageRoom");
        roomGo.transform.SetParent(group, false);
        roomGo.transform.position = floor + Vector3.up * 1.5f;      // moving this object moves the whole group of teammates
        roomMarker = roomGo.transform;

        Material beaconMat = MakeBeaconMaterial();
        AudioClip chime = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Rescue_Chime.wav");

        var pc = Object.FindObjectOfType<ThirdPersonUserControl>();
        Vector3 lookAt = pc != null ? pc.transform.position : floor + Vector3.back * 10f;

        // the three abducted teammates: Duaa, Zubia and Maha (Assets/Prefabs)
        string[] names = { "Duaa", "Zubia", "Maha" };
        Mesh ropeMesh = RopeMesh(); Material ropeMat = RopeMaterial();
        var list = new List<Hostage>();
        for (int i = 0; i < HostageCount; i++)
        {
            string who = i < names.Length ? names[i] : "Teammate " + (i + 1);

            // ---------- rigged + animated version (GAMECON > Rig Teammates) ----------
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Rigged/" + who + "_Rigged.prefab");
            if (rigPrefab != null)
            {
                var holderR = new GameObject("Hostage_" + who);
                holderR.transform.SetParent(roomGo.transform, true);
                Vector3 flatR = lookAt - spots[i]; flatR.y = 0;
                holderR.transform.SetPositionAndRotation(spots[i], flatR.sqrMagnitude > 0.01f ? Quaternion.LookRotation(flatR) : Quaternion.identity);

                var modelR = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
                modelR.name = who;
                modelR.transform.SetParent(holderR.transform, false);
                modelR.transform.localPosition = Vector3.zero;
                modelR.transform.localRotation = Quaternion.identity;

                var rig = modelR.GetComponent<CaptiveRig>();
                rig.live = true;
                rig.SetState(0f);                                   // kneeling, head bowed, hands tied behind the back
                float Hh = rig.height;

                // rope loops attached to the bones, so they follow the animation
                var ropesR = new List<GameObject>();
                System.Func<CaptiveRig.B, Transform> bone = bb2 => rig.bones[(int)bb2];
                System.Func<CaptiveRig.B, Quaternion> ringRot = bb2 => bone(bb2).rotation * Quaternion.FromToRotation(Vector3.up, rig.bindDir[(int)bb2]);
                var chestT = bone(CaptiveRig.B.Chest); var spineT = bone(CaptiveRig.B.Spine);
                ropesR.Add(MakeRope(chestT, "Rope_Chest", chestT.position + chestT.up * 0.02f * Hh, 0.118f * Hh, ringRot(CaptiveRig.B.Chest), ropeMesh, ropeMat));
                ropesR.Add(MakeRope(chestT, "Rope_Arms", chestT.position + chestT.up * 0.095f * Hh, 0.122f * Hh, ringRot(CaptiveRig.B.Chest), ropeMesh, ropeMat));
                ropesR.Add(MakeRope(spineT, "Rope_Waist", spineT.position, 0.108f * Hh, ringRot(CaptiveRig.B.Spine), ropeMesh, ropeMat));
                foreach (var hb in new[] { CaptiveRig.B.HandP, CaptiveRig.B.HandM })
                    ropesR.Add(MakeRope(bone(hb), "Rope_Wrist", bone(hb).position, 0.030f * Hh, ringRot(hb), ropeMesh, ropeMat));
                foreach (var pair in new[] { new[] { CaptiveRig.B.LowerLegP, CaptiveRig.B.FootP }, new[] { CaptiveRig.B.LowerLegM, CaptiveRig.B.FootM } })
                    ropesR.Add(MakeRope(bone(pair[0]), "Rope_Ankle", Vector3.Lerp(bone(pair[1]).position, bone(pair[0]).position, 0.16f), 0.045f * Hh, ringRot(pair[0]), ropeMesh, ropeMat));

                Vector3 headR = rig.HeadWorld();
                var beaconR = MakeBeacon(holderR.transform, "Beacon", beaconSprite, beaconMat, new Color(1f, 0.82f, 0.2f, 0.95f), 0.75f,
                                         holderR.transform.InverseTransformPoint(headR + Vector3.up * 0.75f));
                var hsR = holderR.AddComponent<Hostage>();
                hsR.beacon = beaconR.gameObject;
                hsR.freedSound = chime;
                hsR.displayName = who;
                hsR.rig = rig; hsR.ropes = ropesR.ToArray();

                var lgR = new GameObject("CaptiveLight");
                lgR.transform.SetParent(holderR.transform, false);
                lgR.transform.position = headR + Vector3.up * 0.5f + holderR.transform.forward * 0.5f;
                var lR = lgR.AddComponent<Light>();
                lR.type = LightType.Point; lR.color = new Color(0.4f, 1f, 0.6f); lR.range = 6f; lR.intensity = 1.1f; lR.shadows = LightShadows.None;
                list.Add(hsR);
                continue;
            }
            Debug.LogWarning("[Mission] " + who + " is not rigged yet - run GAMECON > Rig Teammates first. Using the static model.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/" + who + ".prefab");
            if (prefab == null) Debug.LogWarning("[Mission] Assets/Prefabs/" + who + ".prefab not found - using a placeholder.");

            // holder = the captive (position / facing / Hostage script); the character prefab keeps its own import rotation inside it
            var holder = new GameObject("Hostage_" + who);
            holder.transform.SetParent(roomGo.transform, true);
            Vector3 flat = lookAt - spots[i]; flat.y = 0;
            holder.transform.SetPositionAndRotation(spots[i], flat.sqrMagnitude > 0.01f ? Quaternion.LookRotation(flat) : Quaternion.identity);

            GameObject model;
            if (prefab != null) model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            else model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            model.name = who;
            model.transform.SetParent(holder.transform, false);
            model.transform.localPosition = Vector3.zero;            // keep the prefab's own rotation / scale
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

            // stand on the floor and have a sensible height (about 1.7 m)
            Bounds bb = RenderBounds(model);
            if (bb.size.y > 0.05f && (bb.size.y < 1.4f || bb.size.y > 2.1f))
            {
                model.transform.localScale *= 1.7f / bb.size.y;
                bb = RenderBounds(model);
            }
            model.transform.position += new Vector3(0, spots[i].y - bb.min.y, 0);
            bb = RenderBounds(model);
            float h = Mathf.Max(0.5f, bb.size.y);

            // no movement scripts / agents on a captive (these prefabs normally have none)
            foreach (var a in model.GetComponentsInChildren<Animator>(true)) a.applyRootMotion = false;

            // kneeling: these models have no skeleton, so the body is lowered into the floor (the floor hides the lower legs)
            // and bowed forward around the hips. The rescue cut scene reverses this when she stands up.
            var pivot = new GameObject("KneelPivot").transform;
            pivot.SetParent(holder.transform, false);
            pivot.position = new Vector3(bb.center.x, bb.min.y + h * 0.52f, bb.center.z);
            model.transform.SetParent(pivot, true);

            // tied up: rope loops round the chest, arms and hips (sized from the model's height)
            var ropes = new List<GameObject>();
            Quaternion upRot = Quaternion.identity;
            Vector3 centre = new Vector3(bb.center.x, bb.min.y, bb.center.z);
            float chestR = Mathf.Clamp(bb.extents.x * 0.55f, 0.18f, 0.3f);
            ropes.Add(MakeRope(pivot, "Rope_Chest_1", centre + Vector3.up * h * 0.70f, chestR, upRot, ropeMesh, ropeMat));
            ropes.Add(MakeRope(pivot, "Rope_Chest_2", centre + Vector3.up * h * 0.63f, chestR * 0.98f, upRot, ropeMesh, ropeMat));
            ropes.Add(MakeRope(pivot, "Rope_Arms", centre + Vector3.up * h * 0.56f, chestR * 1.02f, upRot, ropeMesh, ropeMat));
            ropes.Add(MakeRope(pivot, "Rope_Hips", centre + Vector3.up * h * 0.47f, chestR * 0.95f, upRot, ropeMesh, ropeMat));
            ropes.Add(MakeRope(pivot, "Rope_Ankles", centre + Vector3.up * h * 0.08f, chestR * 0.55f, upRot, ropeMesh, ropeMat));

            // lower + bow the body
            float drop = h * 0.27f;
            const float bow = 14f;
            pivot.position += Vector3.down * drop;
            pivot.localRotation = Quaternion.Euler(bow, 0f, 0f);
            Bounds kb = RenderBounds(model);

            var beacon = MakeBeacon(holder.transform, "Beacon", beaconSprite, beaconMat, new Color(1f, 0.82f, 0.2f, 0.95f), 0.75f,
                                    holder.transform.InverseTransformPoint(new Vector3(centre.x, kb.max.y + 0.7f, centre.z)));
            var hs = holder.AddComponent<Hostage>();
            hs.beacon = beacon.gameObject;
            hs.freedSound = chime;
            hs.displayName = who;
            hs.animator = null; hs.freedController = null; hs.ropes = ropes.ToArray();
            hs.kneelPivot = pivot; hs.kneelDrop = drop; hs.kneelPitch = bow;

            var lg = new GameObject("CaptiveLight");
            lg.transform.SetParent(holder.transform, false);
            lg.transform.position = new Vector3(centre.x, kb.max.y + 0.5f, centre.z) + holder.transform.forward * 0.5f;
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(0.4f, 1f, 0.6f); l.range = 6f; l.intensity = 1.1f; l.shadows = LightShadows.None;
            list.Add(hs);
        }
        Debug.Log("[Mission] " + HostageCount + " tied teammates at " + floor + " (room bounds " + room.min + " .. " + room.max + ")");
        return list;
    }

    // ================= health kits =================
    // 7 medical kits spread over the level: one early on the way, then two on the way to each of the three rooms.
    static Vector3 PointAlongRoute(Vector3 start, Vector3 goal, float frac, float sideSign)
    {
        var path = new NavMeshPath();
        Vector3[] corners = null;
        if (NavMesh.SamplePosition(start, out NavMeshHit s0, 8f, NavMesh.AllAreas) && NavMesh.SamplePosition(goal, out NavMeshHit g0, 12f, NavMesh.AllAreas)
            && NavMesh.CalculatePath(s0.position, g0.position, NavMesh.AllAreas, path) && path.corners.Length >= 2)
            corners = path.corners;
        if (corners == null) corners = new[] { start, goal };

        float total = 0f;
        for (int i = 1; i < corners.Length; i++) total += Vector3.Distance(corners[i - 1], corners[i]);
        float want = total * frac, acc = 0f; Vector3 p = corners[0], dir = Vector3.forward;
        for (int i = 1; i < corners.Length; i++)
        {
            float seg = Vector3.Distance(corners[i - 1], corners[i]);
            if (acc + seg >= want) { dir = (corners[i] - corners[i - 1]).normalized; p = Vector3.Lerp(corners[i - 1], corners[i], seg > 0f ? (want - acc) / seg : 0f); break; }
            acc += seg;
        }
        Vector3 pos = p + Vector3.Cross(Vector3.up, dir).normalized * 2.8f * sideSign;      // beside the path, not in the middle of it
        if (NavMesh.SamplePosition(pos, out NavMeshHit nh, 6f, NavMesh.AllAreas)) return nh.position;
        if (NavMesh.SamplePosition(p, out NavMeshHit nh2, 8f, NavMesh.AllAreas)) return nh2.position;
        return p;
    }

    static void BuildPickups(Transform group, Vector3[] rooms, Sprite icon)
    {
        var pg = new GameObject("HealthPickups");
        pg.transform.SetParent(group, false);

        var pc = Object.FindObjectOfType<ThirdPersonUserControl>();
        Vector3 start = pc != null ? pc.transform.position : rooms[0] + new Vector3(0, 0, -90);

        Material beaconMat = MakeBeaconMaterial();
        Material kitMat = new Material(Shader.Find("Standard")); kitMat.color = new Color(0.92f, 0.94f, 0.92f);
        Material crossMat = new Material(Shader.Find("Standard")); crossMat.color = new Color(0.1f, 0.85f, 0.25f);
        crossMat.EnableKeyword("_EMISSION"); crossMat.SetColor("_EmissionColor", new Color(0.1f, 1f, 0.3f) * 1.6f);
        Directory.CreateDirectory(EnvFolder);
        AssetDatabase.DeleteAsset(EnvFolder + "/HealthKit.mat"); AssetDatabase.CreateAsset(kitMat, EnvFolder + "/HealthKit.mat");
        AssetDatabase.DeleteAsset(EnvFolder + "/HealthKitCross.mat"); AssetDatabase.CreateAsset(crossMat, EnvFolder + "/HealthKitCross.mat");
        var chime = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Resources/Audio/Pickup/Health_Pickup.wav");

        // where the kits go: (room, fraction of the walk, which side of the path)
        int nearest = 0; float nd = float.MaxValue;
        for (int i = 0; i < rooms.Length; i++) { float d = Vector3.Distance(start, rooms[i]); if (d < nd) { nd = d; nearest = i; } }
        var wanted = new List<(Vector3 room, float frac, float side)> { (rooms[nearest], 0.16f, 1f) };
        for (int r = 0; r < rooms.Length; r++)
        {
            wanted.Add((rooms[r], 0.45f, r % 2 == 0 ? 1f : -1f));
            wanted.Add((rooms[r], 0.82f, r % 2 == 0 ? -1f : 1f));
        }

        var placed = new List<Vector3>();
        int k = 0;
        foreach (var w in wanted)
        {
            Vector3 pos = Vector3.zero; bool ok = false;
            foreach (float shift in new[] { 0f, 0.06f, -0.06f, 0.12f, -0.12f })
            {
                pos = PointAlongRoute(start, w.room, Mathf.Clamp(w.frac + shift, 0.08f, 0.92f), w.side);
                if (placed.All(p => Vector3.Distance(p, pos) > 10f)) { ok = true; break; }
            }
            if (!ok) continue;
            placed.Add(pos);
            MakeKit(pg.transform, ++k, pos, kitMat, crossMat, beaconMat, icon, chime);
        }
        Debug.Log("[Mission] placed " + placed.Count + " health kits.");
    }

    static void MakeKit(Transform parent, int index, Vector3 pos, Material kitMat, Material crossMat, Material beaconMat, Sprite icon, AudioClip chime)
    {
        var kit = new GameObject("HealthKit_" + index);
        kit.transform.SetParent(parent, false);
        kit.transform.position = pos + Vector3.up * 0.75f;

        var model = new GameObject("Model"); model.transform.SetParent(kit.transform, false);
        Part(model.transform, PrimitiveType.Cube, "Box", new Vector3(0, 0, 0), new Vector3(0.52f, 0.36f, 0.28f), kitMat);
        foreach (float z in new[] { 0.145f, -0.145f })
        {
            Part(model.transform, PrimitiveType.Cube, "CrossH", new Vector3(0, 0, z), new Vector3(0.26f, 0.075f, 0.03f), crossMat);
            Part(model.transform, PrimitiveType.Cube, "CrossV", new Vector3(0, 0, z), new Vector3(0.075f, 0.26f, 0.03f), crossMat);
        }

        var lg = new GameObject("KitLight"); lg.transform.SetParent(kit.transform, false);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(0.3f, 1f, 0.45f); l.range = 7f; l.intensity = 1.5f; l.shadows = LightShadows.None;

        MakeBeacon(kit.transform, "Beacon", icon, beaconMat, new Color(0.35f, 1f, 0.5f, 0.95f), 0.4f, new Vector3(0, 0.9f, 0));

        var hp = kit.AddComponent<HealthPickup>();
        hp.visual = model.transform; hp.pickupSound = chime; hp.healAmount = 150f; hp.radius = 1.7f;
    }

    static void Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos; go.transform.localScale = scale;
        var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off;
    }

    [MenuItem("GAMECON/Move Rescue Room To Scene View")]
    public static void MoveRoomToSceneView()
    {
        var room = GameObject.Find("HostageRoom");
        var sv = SceneView.lastActiveSceneView;
        if (room == null || sv == null) { EditorUtility.DisplayDialog("Move Rescue Room", "Open SunTemple_Night, run 'Build Minimap + Rescue Mission' once, and have the Scene view open.", "OK"); return; }
        Vector3 target = sv.pivot;
        if (NavMesh.SamplePosition(target, out NavMeshHit h, 8f, NavMesh.AllAreas)) target = h.position;
        Vector3 current = room.transform.position - Vector3.up * 1.5f;
        Undo.RecordObject(room.transform, "Move rescue room");
        room.transform.position += target - current;
        // snap every captive onto the floor near the new spot
        foreach (var hs in room.GetComponentsInChildren<Hostage>())
            if (NavMesh.SamplePosition(hs.transform.position, out NavMeshHit nh, 3f, NavMesh.AllAreas)) hs.transform.position = nh.position;
        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Mission] Rescue room moved to " + target);
    }

    static Transform MakeBeacon(Transform parent, string name, Sprite sprite, Material mat, Color color, float scale, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one * scale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite; sr.sharedMaterial = mat; sr.color = color; sr.sortingOrder = 100;
        sr.shadowCastingMode = ShadowCastingMode.Off;
        go.AddComponent<FaceCamera>();
        return go.transform;
    }

    static Material MakeBeaconMaterial()
    {
        string path = EnvFolder + "/Beacon.mat";
        AssetDatabase.DeleteAsset(path);
        var m = new Material(Shader.Find("UI/Default"));
        m.SetFloat("unity_GUIZTestMode", (float)CompareFunction.Always);   // draw through walls
        Directory.CreateDirectory(EnvFolder);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // ================= UI =================
    class UiRefs
    {
        public RectTransform miniMap, mapContent, icons, playerArrow, objectiveIcon, compassN;
        public Image maskImage, ring;
        public Text objective, prompt, banner, winText, voice;
        public GameObject winPanel;
    }

    static UiRefs BuildUi(Transform parent, Texture2D mapTex, Sprite circle, Sprite square, Sprite ringSp, Sprite enemy, Sprite arrow, Sprite diamond)
    {
        var r = new UiRefs();
        var root = new GameObject("MissionCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(parent, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 5;
        var sc = root.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // mini-map widget
        var mini = NewRect("MiniMap", root.transform);
        mini.anchorMin = mini.anchorMax = mini.pivot = new Vector2(1, 1);
        mini.anchoredPosition = new Vector2(-30, -30);
        mini.sizeDelta = new Vector2(260, 260);
        r.miniMap = mini;

        var back = NewImage("Backdrop", mini, circle, new Color(0, 0, 0, 0.6f)); Stretch(back.rectTransform);

        var maskRt = NewRect("MapMask", mini); Stretch(maskRt);
        var maskImg = maskRt.gameObject.AddComponent<Image>(); maskImg.sprite = circle; maskImg.raycastTarget = false;
        var mask = maskRt.gameObject.AddComponent<Mask>(); mask.showMaskGraphic = false;
        r.maskImage = maskImg;

        var contentRt = NewRect("MapContent", maskRt);
        contentRt.anchorMin = contentRt.anchorMax = contentRt.pivot = new Vector2(0.5f, 0.5f);
        var raw = contentRt.gameObject.AddComponent<RawImage>(); raw.texture = mapTex; raw.raycastTarget = false;
        r.mapContent = contentRt;

        var iconsRt = NewRect("Icons", maskRt); Stretch(iconsRt);
        r.icons = iconsRt;

        var ring = NewImage("Ring", mini, ringSp, new Color(0.55f, 0.85f, 1f, 0.95f)); Stretch(ring.rectTransform);
        r.ring = ring;

        var pa = NewImage("PlayerArrow", mini, arrow, new Color(0.35f, 0.85f, 1f, 1f));
        Center(pa.rectTransform, new Vector2(26, 26)); r.playerArrow = pa.rectTransform;

        var obj = NewImage("ObjectiveIcon", mini, diamond, new Color(1f, 0.82f, 0.2f, 1f));
        Center(obj.rectTransform, new Vector2(30, 30)); r.objectiveIcon = obj.rectTransform;

        var n = NewText("CompassN", mini, font, "N", 18, new Color(1, 1, 1, 0.95f), TextAnchor.MiddleCenter);
        Center(n.rectTransform, new Vector2(24, 24)); r.compassN = n.rectTransform;

        var legend = NewText("MapLegend", root.transform, font,
            "<color=#59d9ff>▲</color> You   <color=#ff3a3a>●</color> Enemy   <color=#ffd133>◆</color> Rescue room   <color=#59ff7a>+</color> Health   [M] Map",
            15, Color.white, TextAnchor.UpperRight);
        legend.supportRichText = true;
        legend.rectTransform.anchorMin = legend.rectTransform.anchorMax = legend.rectTransform.pivot = new Vector2(1, 1);
        legend.rectTransform.anchoredPosition = new Vector2(-30, -296);
        legend.rectTransform.sizeDelta = new Vector2(420, 26);

        // mission HUD
        r.objective = NewText("ObjectiveText", root.transform, font, "", 26, new Color(1f, 0.9f, 0.5f), TextAnchor.UpperCenter);
        r.objective.rectTransform.anchorMin = r.objective.rectTransform.anchorMax = r.objective.rectTransform.pivot = new Vector2(0.5f, 1f);
        r.objective.rectTransform.anchoredPosition = new Vector2(0, -24);
        r.objective.rectTransform.sizeDelta = new Vector2(1000, 40);

        r.prompt = NewText("PromptText", root.transform, font, "", 30, Color.white, TextAnchor.MiddleCenter);
        r.prompt.rectTransform.anchorMin = r.prompt.rectTransform.anchorMax = r.prompt.rectTransform.pivot = new Vector2(0.5f, 0f);
        r.prompt.rectTransform.anchoredPosition = new Vector2(0, 230);
        r.prompt.rectTransform.sizeDelta = new Vector2(1000, 50);

        r.banner = NewText("BannerText", root.transform, font, "", 34, Color.white, TextAnchor.MiddleCenter);
        r.banner.rectTransform.anchorMin = r.banner.rectTransform.anchorMax = r.banner.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        r.banner.rectTransform.anchoredPosition = new Vector2(0, 230);
        r.banner.rectTransform.sizeDelta = new Vector2(1300, 220);

        r.voice = NewText("VoiceText", root.transform, font, "", 26, Color.white, TextAnchor.MiddleCenter);
        r.voice.supportRichText = true;
        r.voice.rectTransform.anchorMin = r.voice.rectTransform.anchorMax = r.voice.rectTransform.pivot = new Vector2(0.5f, 0f);
        r.voice.rectTransform.anchoredPosition = new Vector2(0, 170);
        r.voice.rectTransform.sizeDelta = new Vector2(1100, 40);

        var win = NewRect("WinPanel", root.transform); Stretch(win);
        var wimg = win.gameObject.AddComponent<Image>(); wimg.color = new Color(0, 0, 0, 0.78f); wimg.raycastTarget = false;
        r.winText = NewText("WinText", win, font, "", 64, new Color(1f, 0.9f, 0.4f), TextAnchor.MiddleCenter);
        Stretch(r.winText.rectTransform);
        r.winPanel = win.gameObject;
        win.gameObject.SetActive(false);
        return r;
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
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var o = rt.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0, 0, 0, 0.9f); o.effectDistance = new Vector2(1.5f, -1.5f);
        return t;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void Center(RectTransform rt, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero; rt.sizeDelta = size;
    }

    // ================= sprites =================
    delegate Color PixelFn(float u, float v);

    static float Dist(float u, float v) { float dx = u - 0.5f, dy = v - 0.5f; return Mathf.Sqrt(dx * dx + dy * dy); }
    static float Circle(float u, float v, float r) { return Dist(u, v) <= r ? 1f : 0f; }
    static Color Soft(float a, float s) { return new Color(1, 1, 1, a); }
    static float Edge(float d, float a, float b) { float e = Mathf.Min(d - a, b - d) / 0.012f; return Mathf.Clamp01(e); }

    static Color PlusPixel(float u, float v)
    {
        float x = Mathf.Abs(u - 0.5f), y = Mathf.Abs(v - 0.5f);
        bool inner = (y < 0.13f && x < 0.38f) || (x < 0.13f && y < 0.38f);
        if (inner) return Color.white;
        bool outer = (y < 0.18f && x < 0.44f) || (x < 0.18f && y < 0.44f);
        return outer ? new Color(0, 0, 0, 0.95f) : Color.clear;
    }

    static Color ArrowPixel(float u, float v)
    {
        // upward pointing arrow head (white fill, dark outline)
        float x = u - 0.5f;
        // triangle: tip at (0.5, 0.92), base corners at (0.12, 0.1) and (0.88, 0.1), notch at (0.5, 0.3)
        bool inside = InTri(u, v, new Vector2(0.5f, 0.92f), new Vector2(0.12f, 0.1f), new Vector2(0.5f, 0.3f))
                   || InTri(u, v, new Vector2(0.5f, 0.92f), new Vector2(0.88f, 0.1f), new Vector2(0.5f, 0.3f));
        if (inside) return Color.white;
        bool outline = InTri(u, v, new Vector2(0.5f, 1.0f), new Vector2(0.03f, 0.04f), new Vector2(0.5f, 0.27f))
                    || InTri(u, v, new Vector2(0.5f, 1.0f), new Vector2(0.97f, 0.04f), new Vector2(0.5f, 0.27f));
        return outline ? new Color(0, 0, 0, 0.95f) : Color.clear;
    }

    static bool InTri(float px, float py, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = (px - b.x) * (a.y - b.y) - (a.x - b.x) * (py - b.y);
        float d2 = (px - c.x) * (b.y - c.y) - (b.x - c.x) * (py - c.y);
        float d3 = (px - a.x) * (c.y - a.y) - (c.x - a.x) * (py - a.y);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    static Sprite MakeSprite(string name, int size, PixelFn fn)
    {
        string path = UiFolder + "/" + name + ".png";
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, fn((x + 0.5f) / size, (y + 0.5f) / size));
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.alphaIsTransparency = true; imp.mipmapEnabled = false;
        imp.filterMode = FilterMode.Bilinear; imp.wrapMode = TextureWrapMode.Clamp;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ================= misc =================
    static Transform FindOrCreateRoot(Scene scene, string name)
    {
        var g = scene.GetRootGameObjects().FirstOrDefault(r => r.name == name);
        if (g == null) { g = new GameObject(name); SceneManager.MoveGameObjectToScene(g, scene); }
        return g.transform;
    }

    static void AddToBuildSettings(string path)
    {
        var list = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
        list.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = list.ToArray();
    }
}
