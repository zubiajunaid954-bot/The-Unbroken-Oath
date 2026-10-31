using System.IO;
using UnityEngine;

namespace GAMECON.Mission
{
    [System.Serializable]
    public class RescueDoorData
    {
        public string doorName;
        public Vector3 door;      // centre of the door
        public Vector3 player;    // where you stood
        public Vector3 forward;   // the direction you were facing (= the way into the room)
        public bool pinned;       // true = use "spot" exactly (the room position is fixed, no door search)
        public Vector3 spot;
    }

    [System.Serializable]
    public class RescueDoorList
    {
        public RescueDoorData[] doors = new RescueDoorData[0];
    }

    /// <summary>
    /// Level-design helper (Editor only). Walk up to the door of the room where the teammates should be held,
    /// face it and press F10: the door is saved to RescueDoor.json in the project folder. Then stop the game
    /// and run GAMECON > Build Minimap + Rescue Mission: the teammates are placed in the room behind that door.
    /// </summary>
    public class RescueDoorMarker : MonoBehaviour
    {
        public KeyCode key = KeyCode.F10;
        public const string FileName = "RescueDoor.json";

        public static string FilePath
        {
            get { return Path.Combine(Directory.GetParent(Application.dataPath).FullName, FileName); }
        }

        void Update()
        {
#if UNITY_EDITOR
            if (!Input.GetKeyDown(key)) return;
            var m = MissionManager.Instance;
            var cam = Camera.main;
            if (m == null || m.Player == null || cam == null) return;

            SunTemple.Door best = null; float bestD = 9f; Vector3 centre = Vector3.zero;
            foreach (var d in FindObjectsOfType<SunTemple.Door>())
            {
                Bounds b; if (!DoorBounds(d, out b)) continue;
                float dist = Vector3.Distance(b.ClosestPoint(cam.transform.position), cam.transform.position);
                if (dist < bestD) { bestD = dist; best = d; centre = b.center; }
            }
            if (best == null) { m.ShowBanner("No door within reach - stand in front of the door and press " + key, 3f); return; }

            Vector3 f = cam.transform.forward; f.y = 0f; f.Normalize();
            var data = new RescueDoorData { doorName = best.name, door = centre, player = m.Player.position, forward = f };

            // up to 3 doors: the 1st is Duaa's room, the 2nd Zubia's, the 3rd Maha's (a 4th press starts again)
            var list = new System.Collections.Generic.List<RescueDoorData>(Load());
            if (list.Count >= 3) list.Clear();
            list.Add(data);
            File.WriteAllText(FilePath, JsonUtility.ToJson(new RescueDoorList { doors = list.ToArray() }, true));
            string[] who = { "Duaa", "Zubia", "Maha" };
            string msg = "Room " + list.Count + "/3 saved (" + who[list.Count - 1] + "): door " + best.name;
            if (list.Count < 3) msg += "\nGo to the next teammate's door and press " + key + " again.";
            else msg += "\nAll three rooms saved. Stop the game, then run  GAMECON > Build Minimap + Rescue Mission";
            m.ShowBanner(msg, 6f);
            Debug.Log("[Mission] saved rescue door " + list.Count + "/3: " + best.name + " at " + centre + " -> " + FilePath);
#endif
        }

        /// <summary>The saved doors (also reads the older single-door file).</summary>
        public static RescueDoorData[] Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new RescueDoorData[0];
                string json = File.ReadAllText(FilePath);
                var l = JsonUtility.FromJson<RescueDoorList>(json);
                if (l != null && l.doors != null && l.doors.Length > 0) return l.doors;
                var one = JsonUtility.FromJson<RescueDoorData>(json);
                if (one != null && !string.IsNullOrEmpty(one.doorName)) return new[] { one };
            }
            catch { }
            return new RescueDoorData[0];
        }

        public static bool DoorBounds(SunTemple.Door d, out Bounds b)
        {
            var r = d.GetComponentInChildren<Renderer>();
            if (r != null) { b = r.bounds; return true; }
            var c = d.GetComponent<Collider>();
            if (c != null) { b = c.bounds; return true; }
            b = new Bounds(d.transform.position, Vector3.one);
            return true;
        }
    }
}
