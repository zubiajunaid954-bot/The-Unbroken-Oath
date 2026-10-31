using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using scgFullBodyController;
using GAMECON.Voice;
using GAMECON.Combat;
using GAMECON.Weather;

namespace GAMECON.Mission
{
    /// <summary>
    /// The briefing before the mission: the operator speaks to Samrah in the dialogue box (with her voice): your three teammates
    /// have been captured, here is all your inventory, use it wisely, keep an eye on the map, I will guide you.
    /// The game is paused while she talks (the inventory and the map pulse when they are mentioned). Press SPACE to skip.
    /// After the last line the mission starts.
    /// </summary>
    public class MissionBriefing : MonoBehaviour
    {
        public static MissionBriefing Instance { get; private set; }
        public static bool Enabled = true;               // set to false to start the mission without a briefing
        public bool Active { get; private set; }

        public KeyCode skipKey = KeyCode.Space;

        // 0 = nothing, 1 = highlight the inventory (weapons + grenades), 2 = highlight the map
        static readonly string[] lines =
        {
            "Samrah, my dear, listen to me carefully.",
            "Your three teammates, Duaa, Zubia and Maha, have been captured.",
            "They are being held in three separate rooms inside the temple.",
            "I have provided you with all the inventory you need: rifle, pistol and sniper, on keys one, two and three.",
            "Grenades are on the G key, and medical kits are waiting along the way. Use it all wisely.",
            "Keep an eye on the map. The gold diamonds show where each of them is held. Press M for the full map.",
            "Free the first one in the morning light. After that, night will fall and the rain will come. Be ready.",
            "I will guide you along the way. Stay sharp, and bring them home.",
            "The mission starts now. Good luck, Samrah.",
        };
        static readonly int[] highlight = { 0, 0, 0, 1, 1, 2, 0, 0, 0 };

        AudioSource src;
        int current;
        RectTransform inventoryBar, grenadeBox, mapPanel;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (!Enabled) return;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            Active = true;
            yield return WaitReal(1.0f);                                  // let the scene settle first

            var mgr = MissionManager.Instance;
            var box = VoiceAssistant.Dialogue;

            // pause the game and block the player
            float oldScale = Time.timeScale;
            Time.timeScale = 0f;
            var blocked = new List<Behaviour>();
            if (mgr != null && mgr.Player != null)
                foreach (var b in mgr.Player.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (b == null || !b.enabled) continue;
                    if (b is ThirdPersonUserControl || b is CameraController || b is GunController || b is GunManager) { b.enabled = false; blocked.Add(b); }
                }

            src = gameObject.AddComponent<AudioSource>(); src.spatialBlend = 0f; src.playOnAwake = false;
            FindUi();

            for (int i = 0; i < lines.Length; i++)
            {
                current = i;
                var clip = Resources.Load<AudioClip>("Audio/Briefing/Briefing_" + i);
                float dur = (clip != null ? clip.length : 3.5f) + 0.8f;
                if (box != null) box.Show("Operator", lines[i], dur);
                if (clip != null) { src.Stop(); src.clip = clip; src.Play(); }

                float t = 0f; bool skip = false;
                while (t < dur)
                {
                    t += Time.unscaledDeltaTime;
                    if (mgr != null) mgr.RequestPrompt("Press [" + skipKey + "] to skip the briefing");
                    Pulse(highlight[i]);
                    if (Input.GetKeyDown(skipKey)) { skip = true; break; }
                    yield return null;
                }
                if (skip) break;
            }

            // the mission starts
            src.Stop();
            Pulse(0);
            if (box != null) box.Hide();
            yield return null;                                            // (so the SPACE that skipped it does not also make her jump)
            foreach (var b in blocked) if (b != null) b.enabled = true;
            Time.timeScale = oldScale <= 0f ? 1f : oldScale;
            Active = false;

            if (mgr != null) mgr.ShowBanner("MISSION START\nFree Duaa, Zubia and Maha", 4f);
            if (DayNightController.Instance != null) DayNightController.Instance.ShowMorning();
        }

        // ------------------------------------------------------------------ highlight what she talks about
        void FindUi()
        {
            var mm = FindObjectOfType<MinimapController>(true);
            if (mm != null) mapPanel = mm.panel;
            var wh = FindObjectOfType<WeaponHud>(true);
            if (wh != null) inventoryBar = wh.transform as RectTransform;
            if (GrenadeThrower.Instance != null && GrenadeThrower.Instance.boxFrame != null)
                grenadeBox = GrenadeThrower.Instance.boxFrame.transform.parent as RectTransform;
        }

        void Pulse(int what)
        {
            float s = 1f + 0.07f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4.5f));
            if (inventoryBar != null) inventoryBar.localScale = Vector3.one * (what == 1 ? s : 1f);
            if (grenadeBox != null) grenadeBox.localScale = Vector3.one * (what == 1 ? s : 1f);
            if (mapPanel != null) mapPanel.localScale = Vector3.one * (what == 2 ? s : 1f);
        }

        static IEnumerator WaitReal(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
        }
    }
}
