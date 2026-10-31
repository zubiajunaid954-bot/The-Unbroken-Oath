using UnityEngine;
using UnityEngine.UI;
using scgFullBodyController;

namespace GAMECON.Combat
{
    /// <summary>
    /// The ammo panel (bottom right) in the tactical amber / olive theme: weapon icon, fire mode, a big two-digit magazine count
    /// that punches on every shot, the reserve, a row of cartridges that go dark as you fire, a reload progress bar,
    /// a LOW AMMO warning and a pulsing RELOAD prompt when the magazine is empty.
    /// </summary>
    public class AmmoHud : MonoBehaviour
    {
        public RectTransform root;
        public Image frame, icon, reloadFill, lowGlow;
        public Text weaponName, mode, mag, reserve, status;
        public Image[] pills;

        public Color amber = new Color(1f, 0.78f, 0.22f, 1f);
        public Color danger = new Color(0.95f, 0.25f, 0.15f, 1f);
        public Color spent = new Color(1f, 1f, 1f, 0.12f);

        GunManager mgr;
        WeaponHud weaponHud;
        GunController gun;
        int lastMag = -1, lastReserve = -1, lastIndex = -1, shownPills = -1;
        float punch, reserveFlash, reloadStart;
        bool wasReloading;

        void Update()
        {
            if (mgr == null) { mgr = FindObjectOfType<GunManager>(true); if (mgr == null) return; }
            if (weaponHud == null) weaponHud = FindObjectOfType<WeaponHud>(true);

            // the old "30/90" text of the stock HUD is replaced by this panel
            var old = FindObjectOfType<hudController>();
            if (old != null && old.uiBullets != null && old.uiBullets.gameObject.activeSelf) old.uiBullets.gameObject.SetActive(false);

            int idx = mgr.CurrentIndex;
            if (mgr.weapons == null || idx < 0 || idx >= mgr.weapons.Length || mgr.weapons[idx] == null) { root.gameObject.SetActive(false); return; }
            root.gameObject.SetActive(true);
            var gc = mgr.weapons[idx].GetComponent<GunController>();
            if (gc == null) { root.gameObject.SetActive(false); return; }

            if (gc != gun || idx != lastIndex) { gun = gc; lastIndex = idx; Setup(); punch = 0.6f; }

            int m = gc.BulletsInMag, r = gc.totalBullets, per = Mathf.Max(1, gc.bulletsPerMag);
            float frac = Mathf.Clamp01(m / (float)per);
            bool low = frac <= 0.25f && m > 0, empty = m <= 0;

            if (lastMag >= 0 && m < lastMag) punch = 1f;                         // a shot
            if (lastReserve >= 0 && r != lastReserve) reserveFlash = 1f;
            lastMag = m; lastReserve = r;
            punch = Mathf.MoveTowards(punch, 0f, Time.unscaledDeltaTime * 7f);
            reserveFlash = Mathf.MoveTowards(reserveFlash, 0f, Time.unscaledDeltaTime * 2.5f);

            // reload progress
            if (gc.reloading && !wasReloading) reloadStart = Time.unscaledTime;
            wasReloading = gc.reloading;
            float prog = gc.reloading ? Mathf.Clamp01((Time.unscaledTime - reloadStart) / Mathf.Max(0.2f, gc.reloadTime)) : 0f;
            if (reloadFill != null) reloadFill.fillAmount = prog;

            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f);
            Color numCol = empty ? Color.Lerp(danger, Color.white, 0.25f * pulse) : low ? danger : Color.white;
            mag.text = m.ToString("D2");
            mag.color = numCol;
            mag.rectTransform.localScale = Vector3.one * (1f + 0.16f * punch);
            reserve.text = "/ " + r.ToString("D2");
            reserve.color = Color.Lerp(new Color(1f, 0.85f, 0.45f, 0.85f), Color.white, reserveFlash);

            // status line
            if (gc.reloading) { status.text = "RELOADING"; status.color = amber; }
            else if (empty && r > 0) { status.text = "RELOAD  [R]"; status.color = Color.Lerp(danger, Color.white, 0.3f * pulse); }
            else if (empty) { status.text = "NO AMMO"; status.color = danger; }
            else if (low) { status.text = "LOW AMMO"; status.color = Color.Lerp(danger, amber, 0.3f * pulse); }
            else { status.text = ""; }

            // the cartridges
            int n = shownPills;
            int lit = Mathf.CeilToInt(frac * n - 0.001f);
            for (int i = 0; i < pills.Length; i++)
            {
                if (pills[i] == null) continue;
                if (i >= n) continue;
                bool on = i < lit;
                Color c = low || empty ? danger : amber;
                if (gc.reloading) { on = i < Mathf.RoundToInt(prog * n); c = amber; }
                Color target = on ? c : spent;
                pills[i].color = Color.Lerp(pills[i].color, target, Time.unscaledDeltaTime * 20f);
            }

            // the frame glows red when low, amber otherwise
            Color f = low || empty ? Color.Lerp(amber, danger, 0.5f + 0.5f * pulse) : amber;
            if (frame != null) frame.color = Color.Lerp(frame.color, f, Time.unscaledDeltaTime * 10f);
            if (lowGlow != null) { var g = danger; g.a = (low || empty) ? 0.10f + 0.12f * pulse : 0f; lowGlow.color = g; }
            if (icon != null) icon.color = Color.Lerp(icon.color, empty ? new Color(1f, 1f, 1f, 0.5f) : amber, Time.unscaledDeltaTime * 10f);
        }

        void Setup()
        {
            string label = "RIFLE"; Sprite sp = weaponHud != null ? weaponHud.rifleIcon : null;
            string fire = "AUTO";
            if (gun.Weapon == GunController.WeaponTypes.Pistol) { label = "PISTOL"; sp = weaponHud != null ? weaponHud.pistolIcon : sp; }
            else if (gun.shootType == GunController.ShootTypes.BoltAction || gun.name.ToLower().Contains("sniper")) { label = "SNIPER"; sp = weaponHud != null ? weaponHud.sniperIcon : sp; }
            if (gun.shootType == GunController.ShootTypes.SemiAuto) fire = "SEMI";
            else if (gun.shootType == GunController.ShootTypes.BoltAction) fire = "BOLT";
            weaponName.text = label; mode.text = fire;
            if (icon != null) icon.sprite = sp;

            // cartridge row: one per round (max 30), spaced over the width
            int per = Mathf.Max(1, gun.bulletsPerMag);
            shownPills = Mathf.Min(per, pills.Length);
            float total = 332f, step = total / shownPills, w = Mathf.Clamp(step * 0.66f, 3f, 12f);
            for (int i = 0; i < pills.Length; i++)
            {
                if (pills[i] == null) continue;
                bool show = i < shownPills;
                pills[i].gameObject.SetActive(show);
                if (!show) continue;
                var rt = pills[i].rectTransform;
                rt.anchoredPosition = new Vector2(step * (i + 0.5f), 0f);
                rt.sizeDelta = new Vector2(w, per <= 12 ? 16f : 13f);
            }
        }
    }
}
