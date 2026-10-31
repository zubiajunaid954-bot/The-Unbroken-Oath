using UnityEngine;
using UnityEngine.UI;
using scgFullBodyController;

namespace GAMECON.Combat
{
    /// <summary>
    /// The weapon box on the HUD (like the grenade box): one slot per weapon with its icon, the key to select it (1 / 2 / 3)
    /// and its name. The weapon in your hands is highlighted and shows its ammo.
    /// </summary>
    public class WeaponHud : MonoBehaviour
    {
        [System.Serializable]
        public class Slot
        {
            public RectTransform root;
            public Image frame, icon;
            public Text key, label, ammo;
        }

        public Slot[] slots;
        public Sprite rifleIcon, pistolIcon, sniperIcon;

        public Color idleFrame = new Color(0f, 0f, 0f, 0.55f);
        public Color activeFrame = new Color(1f, 0.78f, 0.2f, 0.95f);

        GunManager mgr;
        int shown = -1;
        float pop;

        void Update()
        {
            if (mgr == null)
            {
                mgr = FindObjectOfType<GunManager>(true);
                if (mgr == null) return;
                Setup();
            }

            int cur = mgr.CurrentIndex;
            if (cur != shown) { shown = cur; pop = 1f; }
            pop = Mathf.MoveTowards(pop, 0f, Time.unscaledDeltaTime * 5f);

            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s == null || s.root == null) continue;
                bool exists = mgr.weapons != null && i < mgr.weapons.Length && mgr.weapons[i] != null;
                s.root.gameObject.SetActive(exists);
                if (!exists) continue;

                bool active = i == cur;
                s.frame.color = Color.Lerp(s.frame.color, active ? activeFrame : idleFrame, Time.unscaledDeltaTime * 14f);
                s.icon.color = active ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                s.label.color = active ? new Color(1f, 0.9f, 0.5f) : new Color(1f, 1f, 1f, 0.6f);
                float scale = active ? 1f + 0.08f * pop : 1f;
                s.root.localScale = Vector3.Lerp(s.root.localScale, Vector3.one * (active ? 1.04f * scale : 1f), Time.unscaledDeltaTime * 14f);

                // ammo only for the weapon in your hands
                var gc = mgr.weapons[i].GetComponent<GunController>();
                s.ammo.text = "";          // the ammo count lives in the AmmoHud panel
            }
        }

        void Setup()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s == null || s.root == null) continue;
                if (mgr.weapons == null || i >= mgr.weapons.Length || mgr.weapons[i] == null) continue;
                var go = mgr.weapons[i];
                var gc = go.GetComponent<GunController>();

                // pick the icon from the weapon type
                Sprite sp = rifleIcon;
                string label = "RIFLE";
                if (gc != null)
                {
                    if (gc.Weapon == GunController.WeaponTypes.Pistol) { sp = pistolIcon; label = "PISTOL"; }
                    else if (gc.shootType == GunController.ShootTypes.BoltAction) { sp = sniperIcon; label = "SNIPER"; }
                    else label = go.name.ToLower().Contains("sniper") ? "SNIPER" : "RIFLE";
                    if (label == "SNIPER") sp = sniperIcon;
                }
                s.icon.sprite = sp;
                s.label.text = label;
                s.key.text = (i + 1).ToString();
            }
        }
    }
}
