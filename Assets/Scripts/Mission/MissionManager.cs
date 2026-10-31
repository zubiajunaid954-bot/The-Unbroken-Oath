using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using scgFullBodyController;
using GAMECON.Voice;
using GAMECON.Weather;

namespace GAMECON.Mission
{
    /// <summary>
    /// Story + objective: the player's teammates were abducted and are held in a room of the temple.
    /// Shows the objective, interaction prompts (hostages and doors), the intro and the victory screen.
    /// </summary>
    public class MissionManager : MonoBehaviour
    {
        public static MissionManager Instance { get; private set; }
        public static KeyCode InteractKey = KeyCode.E;     // also used by the temple doors
        public KeyCode restartKey = KeyCode.Return;

        [Header("HUD (built by the editor script)")]
        public Text objectiveText;
        public Text promptText;
        public Text bannerText;
        public GameObject winPanel;
        public Text winText;
        public GameObject failPanel;
        public Text failText;
        public Text voiceText;
        public DialogueBox dialogue;                        // speech bubble with the operator portrait                              // subtitle for the AI voice

        [Header("Mission")]
        public Transform objectiveRoom;                     // centre of the hostage room (minimap icon)
        public List<Hostage> hostages = new List<Hostage>();
        [TextArea] public string introText =
            "OPERATION SUN TEMPLE\nYour teammates were abducted and are held in the temple.\nFight through the enemy, reach the marked room and free them.";
        public float introSeconds = 9f;

        public Transform Player { get; private set; }
        public int FreedCount { get; private set; }
        public bool Complete { get; private set; }
        public bool Failed { get; private set; }

        HealthController playerHealth;
        bool hadPlayer;

        string promptThisFrame;
        float bannerUntil;
        float bannerFade = 1f;
        Camera cam;

        void Awake() { Instance = this; }

        void Start()
        {
            var uc = FindObjectOfType<ThirdPersonUserControl>();
            if (uc != null) Player = uc.transform;
            else { var p = GameObject.FindGameObjectWithTag("Player"); if (p != null) Player = p.transform; }
            if (Player != null) { playerHealth = Player.GetComponent<HealthController>(); hadPlayer = true; }
            // every door of the level can be opened with the interact key
            foreach (var d in FindObjectsOfType<SunTemple.Door>()) d.IsLocked = false;
            if (GetComponent<RescueDoorMarker>() == null) gameObject.AddComponent<RescueDoorMarker>();
            if (GetComponent<CutsceneDirector>() == null) gameObject.AddComponent<CutsceneDirector>();
            if (GetComponent<MissionBriefing>() == null) gameObject.AddComponent<MissionBriefing>();
            VoiceAssistant.Subtitle = voiceText;
            VoiceAssistant.Dialogue = dialogue;
            if (voiceText != null) voiceText.text = "";
            if (!MissionBriefing.Enabled) Invoke("SayMissionStart", 1.5f);
            if (winPanel != null) winPanel.SetActive(false);
            if (failPanel != null) failPanel.SetActive(false);
            if (!MissionBriefing.Enabled) ShowBanner(introText, introSeconds);
            RefreshObjective();
        }

        void SayMissionStart() { VoiceAssistant.Say(VoiceLine.MissionStart); }

        System.Collections.IEnumerator SayAfter(VoiceLine line, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            VoiceAssistant.Say(line);
        }

        /// <summary>Distance to the nearest teammate that is still captive.</summary>
        public float DistanceToObjective()
        {
            if (Player == null) return 0f;
            float best = float.MaxValue;
            foreach (var h in hostages)
                if (h != null && !h.Freed) best = Mathf.Min(best, Vector3.Distance(Player.position, h.transform.position));
            return best == float.MaxValue ? 0f : best;
        }

        public void RequestPrompt(string text) { promptThisFrame = text; }

        public void ShowBanner(string text, float seconds)
        {
            if (bannerText == null) return;
            bannerText.text = text;
            bannerUntil = Time.time + seconds;
            bannerFade = 1f;
        }

        public void OnHostageFreed(Hostage h)
        {
            FreedCount++;
            // after the first rescue the morning turns into a rainy night for the remaining teammates
            if (FreedCount == 1 && hostages.Count > 1 && DayNightController.Instance != null) DayNightController.Instance.GoNight();
            if (FreedCount >= hostages.Count)
            {
                Complete = true;
                if (bannerText != null) bannerText.text = "";
                if (promptText != null) promptText.text = "";
                var cut = GetComponent<CutsceneDirector>();
                if (cut != null && cut.HasVictoryScene)
                {
                    // the squad walks in and poses (victory cut scene), then the MISSION COMPLETE screen
                    Vector3 origin = Player != null ? Player.position : transform.position;
                    cut.PlayVictory(origin, ShowWinScreen);
                }
                else
                {
                    StartCoroutine(SayAfter(VoiceLine.AllRescued, 1.4f));
                    ShowWinScreen();
                }
            }
            else
            {
                ShowBanner("Teammate freed  (" + FreedCount + "/" + hostages.Count + ")", 3.5f);
                StartCoroutine(SayAfter(VoiceLine.Freed, 1.4f));
            }
            RefreshObjective();
        }

        void ShowWinScreen()
        {
            if (winPanel != null) winPanel.SetActive(false);
            if (failPanel != null && failPanel.transform.parent != null)
                foreach (Transform c in failPanel.transform.parent) c.gameObject.SetActive(false);       // only the result screen is visible
            FailScreen.ShowVictory(restartKey);
        }

        // The teammate you are looking at (and standing next to). Prompt, cut scene and dialogue all use this one person.
        Hostage FindHostageInView()
        {
            if (Player == null) return null;
            if (cam == null) cam = Camera.main;
            Hostage best = null; float bestScore = float.MaxValue;
            foreach (var h in hostages)
            {
                if (h == null || h.Freed) continue;
                float dist = Vector3.Distance(Player.position, h.transform.position);
                if (dist > h.interactRadius + 0.8f) continue;
                float ang = 0f;
                if (cam != null) ang = Vector3.Angle(cam.transform.forward, (h.transform.position + Vector3.up * 0.9f) - cam.transform.position);
                float score = ang + dist * 6f;
                if (score < bestScore) { bestScore = score; best = h; }
            }
            return best;
        }

        const float DoorReach = 3.6f;
        SunTemple.Door[] doorCache;
        float nextDoorRefresh;

        // The door you are looking at and standing next to: closest visible part within reach, near the middle of the view.
        // (Does not depend on raycasts, so invisible collision boxes or trigger colliders cannot block it.)
        SunTemple.Door FindDoorInView()
        {
            if (cam == null) cam = Camera.main;
            if (cam == null) return null;
            if (doorCache == null || Time.time >= nextDoorRefresh)
            {
                doorCache = FindObjectsOfType<SunTemple.Door>();
                nextDoorRefresh = Time.time + 3f;
            }
            Vector3 o = cam.transform.position, f = cam.transform.forward;
            SunTemple.Door best = null; float bestScore = float.MaxValue;
            foreach (var d in doorCache)
            {
                if (d == null) continue;
                Bounds b; RescueDoorMarker.DoorBounds(d, out b);
                float dist = Vector3.Distance(b.ClosestPoint(o), o);
                if (dist > DoorReach) continue;
                float ang = Vector3.Angle(f, b.center - o);
                if (ang > 40f) continue;
                float score = ang + dist * 4f;
                if (score < bestScore) { bestScore = score; best = d; }
            }
            return best;
        }

        void RefreshObjective()
        {
            if (objectiveText == null) return;
            if (Complete) objectiveText.text = "Mission complete";
            else objectiveText.text = "OBJECTIVE: Rescue your teammates  " + FreedCount + "/" + hostages.Count +
                                      (hostages.Count > 0 ? "   -   nearest " + Mathf.RoundToInt(DistanceToObjective()) + " m" : "");
        }

        Vector3 lastPos; float lastYaw;
        bool deathSceneStarted;

        void Fail()
        {
            if (Failed || Complete) return;
            Failed = true;
            if (bannerText != null) bannerText.text = "";
            if (promptText != null) promptText.text = "";
            if (objectiveText != null) objectiveText.text = "Mission failed";
            if (VoiceAssistant.Dialogue != null) VoiceAssistant.Dialogue.Hide();

            // Samrah's death scene (her hand falls, the drone shot, "I failed you..."), then the MISSION FAILED screen
            var cut = GetComponent<CutsceneDirector>();
            if (cut != null && cut.HasDeathScene && !deathSceneStarted)
            {
                deathSceneStarted = true;
                cut.PlayDeath(lastPos, lastYaw, ShowFailPanel);
            }
            else
            {
                VoiceAssistant.Say(VoiceLine.MissionFailed);
                ShowFailPanel();
            }
        }

        void ShowFailPanel()
        {
            if (failPanel != null) failPanel.SetActive(false);             // (replaced by the styled FailScreen)
            // only the fail screen is visible: the rest of the HUD stays hidden
            if (failPanel != null && failPanel.transform.parent != null)
                foreach (Transform c in failPanel.transform.parent) if (c != failPanel.transform) c.gameObject.SetActive(false);
            FailScreen.Show(hostages, restartKey, deathSceneStarted ? "Samrah has fallen. The mission cannot continue." : "You were killed. The mission cannot continue.");
        }

        void Update()
        {
            if (Player != null && !Failed) { lastPos = Player.position; lastYaw = Player.eulerAngles.y; }
            // life ran out -> mission failed (the controller destroys the player and spawns a ragdoll)
            if (!Complete && !Failed && hadPlayer)
            {
                bool dead = (playerHealth != null && playerHealth.health <= 0f) || (Player == null);
                if (dead) Fail();
            }
            if (Failed)
            {
                bool sceneRunning = CutsceneDirector.Instance != null && CutsceneDirector.Instance.DeathPlaying;
                if (!sceneRunning && Input.GetKeyDown(restartKey)) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;
            }

            if (Time.frameCount % 10 == 0) RefreshObjective();

            // doors: look at a door within reach, press the interact key
            bool inCutscene = (CutsceneDirector.Instance != null && CutsceneDirector.Instance.Playing) || (MissionBriefing.Instance != null && MissionBriefing.Instance.Active);

            // free a teammate: the one you are looking at
            if (Player != null && !Complete && !inCutscene)
            {
                var target = FindHostageInView();
                if (target != null)
                {
                    promptThisFrame = "Press [" + InteractKey + "] to cut the ropes and free " + target.displayName;
                    if (Input.GetKeyDown(InteractKey)) target.Free();
                }
            }

            if (Player != null && !Complete && !inCutscene)
            {
                var door = FindDoorInView();
                if (door != null)
                {
                    if (door.IsLocked) { if (promptThisFrame == null) promptThisFrame = "Door is locked"; }
                    else
                    {
                        if (promptThisFrame == null)
                            promptThisFrame = "Press [" + InteractKey + "] to " + (door.DoorClosed ? "open" : "close") + " the door";
                        if (Input.GetKeyDown(InteractKey))
                        {
                            bool wasClosed = door.DoorClosed;
                            door.Activate();
                            VoiceAssistant.Say(wasClosed ? VoiceLine.Unlock : VoiceLine.Unlock);
                        }
                    }
                }
            }
            if (promptText != null) promptText.text = promptThisFrame ?? "";
            promptThisFrame = null;

            // banner fade
            if (bannerText != null && !string.IsNullOrEmpty(bannerText.text))
            {
                float left = bannerUntil - Time.time;
                if (left <= 0f) bannerText.text = "";
                else
                {
                    var c = bannerText.color; c.a = Mathf.Clamp01(left / 1.2f); bannerText.color = c;
                }
            }

            bool victoryRunning = CutsceneDirector.Instance != null && CutsceneDirector.Instance.VictoryPlaying;
            if (Complete && !victoryRunning && Input.GetKeyDown(restartKey))
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
