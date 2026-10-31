using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using scgFullBodyController;

namespace GAMECON.Mission
{
    /// <summary>
    /// Rotating mini-map (and full map on M). The map picture is a baked top-down image of the level;
    /// icons are drawn on top: white arrow = player, red dots = enemies, gold diamond = rescue room.
    /// </summary>
    public class MinimapController : MonoBehaviour
    {
        [Header("World rectangle covered by the map picture")]
        public Vector2 worldCenter;          // x,z
        public Vector2 worldSize;            // metres

        [Header("UI (built by the editor script)")]
        public RectTransform panel;          // the whole minimap widget
        public Image maskImage;              // mask shape (circle / square)
        public RectTransform mapContent;     // RawImage with the map picture
        public RectTransform icons;          // parent of the dynamic icons (inside the mask)
        public RectTransform playerArrow;
        public RectTransform objectiveIcon;  // outside the mask so it can sit on the border
        public RectTransform compassN;
        public Image ring;
        public Sprite circleSprite, squareSprite, enemySprite, pickupSprite;

        [Header("Settings")]
        public float pixelsPerMetre = 1.3f;
        public Vector2 smallSize = new Vector2(260, 260);
        public Vector2 bigSize = new Vector2(760, 760);
        public KeyCode toggleKey = KeyCode.M;
        public Color enemyColor = new Color(1f, 0.2f, 0.2f, 1f);
        public Color enemyAlertColor = new Color(1f, 0.85f, 0.2f, 1f);

        bool big;
        Transform player;
        Camera cam;
        readonly List<AiController> enemies = new List<AiController>();
        readonly List<Image> enemyIcons = new List<Image>();
        readonly List<HealthPickup> pickups = new List<HealthPickup>();
        readonly List<Image> pickupIcons = new List<Image>();
        readonly List<RectTransform> objectiveIcons = new List<RectTransform>();
        float nextRefresh;
        Vector2 smallAnchoredPos;
        Vector2 smallAnchorMin, smallAnchorMax, smallPivot;

        void Start()
        {
            smallAnchoredPos = panel.anchoredPosition;
            smallAnchorMin = panel.anchorMin; smallAnchorMax = panel.anchorMax; smallPivot = panel.pivot;
            panel.sizeDelta = smallSize;
            ApplyMode();
        }

        void ResolvePlayer()
        {
            if (player == null)
            {
                var uc = FindObjectOfType<ThirdPersonUserControl>();
                if (uc != null) player = uc.transform;
            }
            if (cam == null) cam = Camera.main;
        }

        void Update()
        {
            ResolvePlayer();
            if (Input.GetKeyDown(toggleKey)) { big = !big; ApplyMode(); }
            if (player == null) return;

            if (Time.time >= nextRefresh)
            {
                nextRefresh = Time.time + 0.4f;
                enemies.Clear();
                foreach (var ai in FindObjectsOfType<AiController>())
                    if (ai != null && ai.enabled && ai.gameObject.activeInHierarchy) enemies.Add(ai);
                pickups.Clear();
                pickups.AddRange(FindObjectsOfType<HealthPickup>());
            }
        }

        void LateUpdate()
        {
            if (player == null || cam == null) return;

            float ppm = pixelsPerMetre * Zoom();
            float yaw = big ? 0f : cam.transform.eulerAngles.y;       // small map rotates, big map is north-up
            Quaternion rot = Quaternion.Euler(0, 0, yaw);

            // 1. map picture: shift/rotate so that the player is at the centre of the panel
            Vector2 pOffset = new Vector2(player.position.x - worldCenter.x, player.position.z - worldCenter.y);
            mapContent.sizeDelta = new Vector2(worldSize.x * ppm, worldSize.y * ppm);
            mapContent.localRotation = rot;
            mapContent.anchoredPosition = (Vector2)(rot * (Vector3)(-pOffset * ppm));

            // 2. player arrow
            playerArrow.localRotation = Quaternion.Euler(0, 0, big ? -cam.transform.eulerAngles.y : 0f);

            // 3. enemies
            float radius = (panel.sizeDelta.x * 0.5f) / ppm;           // visible radius in metres
            int used = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                var ai = enemies[i];
                if (ai == null) continue;
                Vector3 d = ai.transform.position - player.position;
                Vector2 flat = new Vector2(d.x, d.z);
                if (!big && flat.magnitude > radius * 0.98f) continue;
                var icon = GetEnemyIcon(used++);
                bool alert = ai.playerInSightRange || ai.overrideAttack;
                icon.color = alert ? enemyAlertColor : enemyColor;
                float s = alert ? 15f : 11f;
                icon.rectTransform.sizeDelta = new Vector2(s, s);
                icon.rectTransform.anchoredPosition = (Vector2)(rot * (Vector3)(flat * ppm));
            }
            for (int i = used; i < enemyIcons.Count; i++) enemyIcons[i].gameObject.SetActive(false);

            // 3b. health kits (green +)
            int usedP = 0;
            for (int i = 0; i < pickups.Count; i++)
            {
                var pk = pickups[i];
                if (pk == null) continue;
                Vector3 d = pk.transform.position - player.position;
                Vector2 flat = new Vector2(d.x, d.z);
                if (!big && flat.magnitude > radius * 0.98f) continue;
                var icon = GetPickupIcon(usedP++);
                float s = 18f + Mathf.Sin(Time.time * 4f) * 1.5f;
                icon.rectTransform.sizeDelta = new Vector2(s, s);
                icon.rectTransform.anchoredPosition = (Vector2)(rot * (Vector3)(flat * ppm));
            }
            for (int i = usedP; i < pickupIcons.Count; i++) pickupIcons[i].gameObject.SetActive(false);

            // 4. rescue rooms: one gold diamond per teammate still captive (clamped to the map edge so you know the direction)
            var mm = MissionManager.Instance;
            int shown = 0;
            if (mm != null && !mm.Complete)
                foreach (var h in mm.hostages)
                {
                    if (h == null || h.Freed) continue;
                    var icon = GetObjectiveIcon(shown++);
                    icon.gameObject.SetActive(true);
                    Vector3 d = h.transform.position - player.position;
                    Vector2 p = (Vector2)(rot * (Vector3)(new Vector2(d.x, d.z) * ppm));
                    float half = panel.sizeDelta.x * 0.5f - 12f;
                    if (p.magnitude > half) p = p.normalized * half;
                    icon.anchoredPosition = p;
                    icon.localScale = Vector3.one * (1f + Mathf.Sin(Time.time * 4f + shown) * 0.12f);
                }
            for (int i = shown; i < objectiveIcons.Count; i++) objectiveIcons[i].gameObject.SetActive(false);
            if (shown == 0 && objectiveIcon != null) objectiveIcon.gameObject.SetActive(false);

            // 5. compass N
            if (compassN != null)
            {
                float r = panel.sizeDelta.x * 0.5f - 11f;
                compassN.anchoredPosition = (Vector2)(rot * (Vector3)(Vector2.up * r));
            }
        }

        float Zoom()
        {
            if (!big) return 1f;
            // fit the whole level into the big panel
            float fit = Mathf.Min(bigSize.x / (worldSize.x * pixelsPerMetre), bigSize.y / (worldSize.y * pixelsPerMetre));
            return fit * 0.95f;
        }

        void ApplyMode()
        {
            if (big)
            {
                panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
                panel.pivot = new Vector2(0.5f, 0.5f);
                panel.anchoredPosition = Vector2.zero;
                panel.sizeDelta = bigSize;
                if (maskImage != null && squareSprite != null) maskImage.sprite = squareSprite;
            }
            else
            {
                panel.anchorMin = smallAnchorMin; panel.anchorMax = smallAnchorMax; panel.pivot = smallPivot;
                panel.anchoredPosition = smallAnchoredPos;
                panel.sizeDelta = smallSize;
                if (maskImage != null && circleSprite != null) maskImage.sprite = circleSprite;
            }
            if (ring != null) ring.enabled = !big;
        }

        RectTransform GetObjectiveIcon(int i)
        {
            if (objectiveIcons.Count == 0 && objectiveIcon != null) objectiveIcons.Add(objectiveIcon);   // the one built into the scene
            while (objectiveIcons.Count <= i)
            {
                var copy = Instantiate(objectiveIcon.gameObject, objectiveIcon.parent);
                copy.name = "ObjectiveIcon_" + (objectiveIcons.Count + 1);
                objectiveIcons.Add(copy.GetComponent<RectTransform>());
            }
            return objectiveIcons[i];
        }

        Image GetPickupIcon(int i)
        {
            while (pickupIcons.Count <= i)
            {
                var go = new GameObject("HealthIcon", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(icons, false);
                var img = go.GetComponent<Image>();
                img.sprite = pickupSprite;
                img.color = new Color(0.35f, 1f, 0.45f, 1f);
                img.raycastTarget = false;
                pickupIcons.Add(img);
            }
            var icon = pickupIcons[i];
            if (!icon.gameObject.activeSelf) icon.gameObject.SetActive(true);
            return icon;
        }

        Image GetEnemyIcon(int i)
        {
            while (enemyIcons.Count <= i)
            {
                var go = new GameObject("EnemyIcon", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(icons, false);
                var img = go.GetComponent<Image>();
                img.sprite = enemySprite;
                img.raycastTarget = false;
                enemyIcons.Add(img);
            }
            var icon = enemyIcons[i];
            if (!icon.gameObject.activeSelf) icon.gameObject.SetActive(true);
            return icon;
        }
    }
}
