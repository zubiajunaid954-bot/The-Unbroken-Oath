using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GAMECON.Mission;

namespace GAMECON.Menu
{
    /// <summary>
    /// The main menu: a short cut scene (the four warriors walk forward through the Sun Temple gate plaza and take their hero poses),
    /// then the title and the buttons appear. PLAY fades into the story scene.
    /// </summary>
    public class MainMenuDirector : MonoBehaviour
    {
        [Header("Scene")]
        public Camera cam;
        public GameObject[] heroPrefabs;                 // Samrah, Duaa, Zubia, Maha (rigged)
        public GameObject riflePrefab, swordPrefab;
        public Vector3 stageCenter = new Vector3(80f, 0f, -150f);
        public ParticleSystem dust;

        [Header("Audio")]
        public AudioSource music, sfx, steps;
        public AudioClip hoverClip, clickClip, backClip, whooshClip, stepClip;

        [Header("UI")]
        public Canvas canvas;
        public Image fade;
        public CanvasGroup logoGroup;
        public RectTransform logoRect;
        public Image logoGlow, logoShine;
        public CanvasGroup hintGroup;
        public MenuButton playButton, controlsButton, settingsButton, quitButton;
        public MenuPanel settingsPanel, controlsPanel;
        public MenuButton settingsBack, controlsBack, fullscreenButton;
        public Slider masterSlider, musicSlider;
        public Text masterValue, musicValue, fullscreenLabel;
        public Text masterLabel, musicLabel;
        public string nextScene = "StoryScene";

        // ------------------------------------------------------------------------------------------------ timeline
        const float LogoAt = 7.9f, ButtonsAt = 9.4f, IntroEnd = 10.4f, HintAt = 11.8f;

        class Slot { public string name; public float s, dx, yaw, startAt, dist; public HeroRig.Pose pose; public MenuHero.Carry carry; public int prefab; }
        static readonly Slot[] slots =
        {
            new Slot { name = "Maha",   s = -1.55f, dx = 0.55f, yaw = 250f, startAt = 0.25f, dist = 12.1f, pose = HeroRig.Pose.HipsWide,   carry = MenuHero.Carry.SwordPlanted, prefab = 3 },
            new Slot { name = "Samrah", s = -0.40f, dx = -0.45f, yaw = 268f, startAt = 0.00f, dist = 11.7f, pose = HeroRig.Pose.Stride,     carry = MenuHero.Carry.Rifle,        prefab = 0 },
            new Slot { name = "Zubia",  s = 0.85f,  dx = 0.10f, yaw = 292f, startAt = 0.15f, dist = 11.8f, pose = HeroRig.Pose.StrideBack, carry = MenuHero.Carry.SwordLow,     prefab = 2 },
            new Slot { name = "Duaa",   s = 1.95f,  dx = 0.60f, yaw = 288f, startAt = 0.40f, dist = 12.0f, pose = HeroRig.Pose.Stride,     carry = MenuHero.Carry.Rifle,        prefab = 1 },
        };

        readonly List<MenuHero> heroes = new List<MenuHero>();
        List<MenuButton> mainButtons;
        float T;                           // seconds since the start of the cut scene
        bool built, introDone, logoShown, buttonsShown, hintShown, starting, skipped;
        int selected = -1;
        float logoAt = -1f, hintAt = -1f;
        Vector3 finalPos, finalFocus;
        float finalFov = 42f;
        Vector2 parallax;
        float musicFade;
        enum Mode { Main, Settings, Controls }
        Mode mode = Mode.Main;
        int panelIndex;
        bool fullscreen;
        float logoDim = 1f;

        // ------------------------------------------------------------------------------------------------ setup
        void Awake()
        {
            GameSettings.Apply();
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            ApplyFonts();
        }

        void Start()
        {
            mainButtons = new List<MenuButton> { playButton, controlsButton, settingsButton, quitButton };
            BuildHeroes();
            ComputeCamera();
            foreach (var b in AllButtons()) { b.sfx = sfx; b.hoverClip = hoverClip; b.clickClip = clickClip; b.HideInstantly(); }
            playButton.Clicked += StartGame; controlsButton.Clicked += OpenControls; settingsButton.Clicked += OpenSettings; quitButton.Clicked += Quit;
            settingsBack.Clicked += ClosePanel; controlsBack.Clicked += ClosePanel;
            fullscreenButton.Clicked += ToggleFullscreen;
            foreach (var b in mainButtons) { var bb = b; bb.Hovered += x => { Select(mainButtons.IndexOf(x)); }; }
            fullscreen = Screen.fullScreen;
            SetupSettings();

            if (logoGroup != null) logoGroup.alpha = 0f;
            if (hintGroup != null) hintGroup.alpha = 0f;
            if (fade != null) fade.color = Color.black;
            if (settingsPanel != null) settingsPanel.SetOpenInstantly(false);
            if (controlsPanel != null) controlsPanel.SetOpenInstantly(false);

            if (music != null) { music.volume = 0f; music.loop = true; music.Play(); }
            if (cam != null) { PositionCamera(0f); }
        }

        IEnumerable<MenuButton> AllButtons()
        {
            yield return playButton; yield return controlsButton; yield return settingsButton; yield return quitButton;
            yield return settingsBack; yield return controlsBack; yield return fullscreenButton;
        }

        public void ApplyFonts()
        {
            if (canvas == null) return;
            Font f = FailScreen.GetFont(), serif = FailScreen.GetSerifFont();
            foreach (var t in canvas.GetComponentsInChildren<Text>(true)) t.font = t.gameObject.name == "Title" ? serif : f;
            foreach (var b in canvas.GetComponentsInChildren<MenuButton>(true)) if (b.label != null) b.label.font = serif;
        }

        public void BuildHeroes()
        {
            if (built) return;
            built = true;
            Vector3 walkDir = new Vector3(-1f, 0f, 0f);
            foreach (var s in slots)
            {
                var go = Instantiate(heroPrefabs[s.prefab]);
                go.name = s.name + "_Hero";
                var cr = go.GetComponent<CaptiveRig>();
                var hr = go.AddComponent<HeroRig>(); hr.Setup(cr); hr.seed = s.prefab * 1.7f;
                var mh = go.AddComponent<MenuHero>();
                mh.rig = hr; mh.carry = s.carry; mh.finalPose = s.pose;
                float sx = stageCenter.x + s.dx, sz = stageCenter.z - s.s;
                mh.slot = new Vector3(sx, Ground(new Vector3(sx, 0f, sz), stageCenter.y), sz);
                mh.walkDir = walkDir; mh.dist = s.dist; mh.startAt = s.startAt; mh.walkYaw = 270f; mh.finalYaw = s.yaw;
                mh.stepSource = steps; mh.stepClip = stepClip; mh.listener = cam != null ? cam.transform : null;
                var wp = Instantiate(s.carry == MenuHero.Carry.Rifle ? riflePrefab : swordPrefab);
                wp.name = s.name + "_Weapon"; mh.weapon = wp.transform; wp.transform.SetParent(go.transform.parent, true);
                mh.Init();
                heroes.Add(mh);
            }
        }

        static float Ground(Vector3 p, float fallback)
        {
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(p.x, p.y + 400f, p.z), Vector3.down, out hit, 800f, ~0, QueryTriggerInteraction.Ignore)) return hit.point.y;
            return fallback;
        }

        void ComputeCamera()
        {
            // the key-art framing: low, close, looking slightly up at the four of them; the camera is panned a little so that they sit left of the buttons
            Vector3 p = new Vector3(stageCenter.x - 4.5f, 0f, stageCenter.z - 0.55f);
            finalPos = new Vector3(p.x, Ground(new Vector3(p.x, 0f, p.z), 0f) + 0.55f, p.z);
            Vector3 f = new Vector3(stageCenter.x, 0f, stageCenter.z - 0.75f);
            finalFocus = new Vector3(f.x, Ground(new Vector3(f.x, 0f, f.z), 0f) + 1.5f, f.z);
        }

        // ------------------------------------------------------------------------------------------------ the show
        static float Ease(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }

        void PositionCamera(float t)
        {
            if (cam == null) return;
            float e = Ease(t / 9.6f);
            Vector3 back = new Vector3(-1.7f * (1f - e), 0.10f * (1f - e), 0.35f * (1f - e));
            Vector3 pos = finalPos + back;
            Vector3 avg = Vector3.zero; foreach (var h in heroes) avg += h.transform.position; if (heroes.Count > 0) avg /= heroes.Count;
            Vector3 look = Vector3.Lerp(avg + Vector3.up * 1.25f, finalFocus, e);

            // after the intro: a slow breathing drift and a little parallax with the mouse
            float idle = Ease((t - IntroEnd) / 2.0f);
            Vector3 right = Vector3.Cross(Vector3.up, (finalFocus - finalPos).normalized);
            Vector3 drift = right * (0.07f * Mathf.Sin(t * 0.21f)) + Vector3.up * (0.025f * Mathf.Sin(t * 0.33f)) + (finalFocus - finalPos).normalized * (0.05f * Mathf.Sin(t * 0.17f));
            Vector2 target = Vector2.zero;
            if (Application.isPlaying && Screen.width > 0)
                target = new Vector2(Mathf.Clamp01(Input.mousePosition.x / Screen.width) - 0.5f, Mathf.Clamp01(Input.mousePosition.y / Screen.height) - 0.5f);
            parallax = Vector2.Lerp(parallax, target, 1f - Mathf.Exp(-2.5f * Time.unscaledDeltaTime));
            pos += (drift + right * (-parallax.x * 0.30f) + Vector3.up * (-parallax.y * 0.10f)) * idle;
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
            cam.fieldOfView = Mathf.Lerp(37f, finalFov, e);
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            T += dt;

            // the intro can be skipped with any key or click
            if (!introDone && !skipped && T > 0.8f && (Input.anyKeyDown || Input.GetMouseButtonDown(0))) Skip();

            foreach (var h in heroes) h.Step(T, dt);
            PositionCamera(T);

            // fade in from black, the music rises
            if (fade != null && !starting) { var c = fade.color; c.a = Mathf.Clamp01(1f - T / 1.4f); fade.color = c; }
            if (music != null && !starting) music.volume = Mathf.Min(GameSettings.Music, GameSettings.Music * Ease(T / 4.0f));
            if (music != null && starting) { musicFade = Mathf.MoveTowards(musicFade, 0f, dt / 0.9f); music.volume = GameSettings.Music * musicFade; }

            if (!logoShown && T >= LogoAt) { logoShown = true; logoAt = Time.unscaledTime; if (sfx != null && whooshClip != null) sfx.PlayOneShot(whooshClip, 0.5f); }
            if (!buttonsShown && T >= ButtonsAt)
            {
                buttonsShown = true;
                for (int i = 0; i < mainButtons.Count; i++) mainButtons[i].Show(i * 0.16f);
                Select(0);
            }
            if (!hintShown && T >= HintAt) { hintShown = true; hintAt = Time.unscaledTime; }
            if (T >= IntroEnd) introDone = true;

            AnimateLogo();
            if (hintGroup != null && hintShown) hintGroup.alpha = Ease((Time.unscaledTime - hintAt) / 1.0f) * (mode == Mode.Main && !starting ? 0.8f : 0f);

            if (introDone && !starting) HandleInput();
        }

        void Skip()
        {
            skipped = true;
            foreach (var h in heroes) h.SnapFinal();
            T = Mathf.Max(T, IntroEnd);
            logoShown = true; logoAt = Time.unscaledTime - 0.6f;
            buttonsShown = true; hintShown = true; hintAt = Time.unscaledTime - 0.5f;
            for (int i = 0; i < mainButtons.Count; i++) mainButtons[i].Show(0.1f + i * 0.1f);
            Select(0);
            introDone = true;
        }

        void AnimateLogo()
        {
            if (logoGroup == null) return;
            float k = logoShown ? Ease((Time.unscaledTime - logoAt) / 1.8f) : 0f;
            logoDim = Mathf.MoveTowards(logoDim, mode == Mode.Main ? 1f : 0.18f, Time.unscaledDeltaTime * 3f);
            logoGroup.alpha = k * logoDim;
            if (logoRect != null)
            {
                logoRect.localScale = Vector3.one * Mathf.Lerp(1.07f, 1f, k);
                logoRect.anchoredPosition = new Vector2(0f, Mathf.Lerp(-6f, -30f, k));
            }
            if (logoGlow != null)
            {
                var c = logoGlow.color; c.a = k * (0.40f + 0.22f * Mathf.Sin(Time.unscaledTime * 1.6f)); logoGlow.color = c;
            }
            if (logoShine != null && logoRect != null)
            {
                float w = logoRect.rect.width;
                float s = Mathf.Repeat(Time.unscaledTime * 0.16f, 1.8f) - 0.4f;
                logoShine.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-w * 0.6f, w * 0.6f, s), 0f);
                var c = logoShine.color; c.a = k * 0.55f * Mathf.Clamp01(Mathf.Min(s + 0.2f, 1.2f - s) * 4f); logoShine.color = c;
            }
        }

        // ------------------------------------------------------------------------------------------------ input
        void Select(int i)
        {
            if (mode != Mode.Main) return;
            selected = i;
            for (int k = 0; k < mainButtons.Count; k++) mainButtons[k].SetSelected(k == i);
        }

        void HandleInput()
        {
            if (mode == Mode.Main)
            {
                if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) MoveSelection(1);
                else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) MoveSelection(-1);
                else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
                {
                    if (selected >= 0) mainButtons[selected].Trigger();
                }
            }
            else if (mode == Mode.Settings)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { ClosePanel(); return; }
                if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) { panelIndex = (panelIndex + 1) % 4; RefreshSettingsFocus(true); }
                else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) { panelIndex = (panelIndex + 3) % 4; RefreshSettingsFocus(true); }
                else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) NudgeSlider(-0.05f);
                else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) NudgeSlider(0.05f);
                else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
                {
                    if (panelIndex == 2) fullscreenButton.Trigger(); else if (panelIndex == 3) settingsBack.Trigger();
                }
            }
            else
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) controlsBack.Trigger();
            }
        }

        void MoveSelection(int d)
        {
            int n = mainButtons.Count;
            int i = selected < 0 ? 0 : (selected + d + n) % n;
            Select(i);
            if (sfx != null && hoverClip != null) sfx.PlayOneShot(hoverClip, 0.8f);
        }

        // ------------------------------------------------------------------------------------------------ actions
        void StartGame()
        {
            if (starting) return;
            starting = true; musicFade = 1f;
            foreach (var b in mainButtons) b.Interactable = false;
            SceneTransition.Load(nextScene, 1.0f, 1.2f);
        }

        void Quit()
        {
            if (starting) return;
            starting = true; musicFade = 1f;
            foreach (var b in mainButtons) b.Interactable = false;
            StartCoroutine(QuitRoutine());
        }

        System.Collections.IEnumerator QuitRoutine()
        {
            float t = 0f;
            while (t < 0.9f)
            {
                t += Time.unscaledDeltaTime;
                if (fade != null) { var c = fade.color; c.a = Mathf.Clamp01(t / 0.9f); fade.color = c; }
                yield return null;
            }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OpenControls() { OpenPanel(Mode.Controls); }
        void OpenSettings() { OpenPanel(Mode.Settings); }

        void OpenPanel(Mode m)
        {
            if (mode != Mode.Main || starting) return;
            mode = m;
            foreach (var b in mainButtons) { b.Interactable = false; b.SetSelected(false); }
            if (sfx != null && whooshClip != null) sfx.PlayOneShot(whooshClip, 0.6f);
            if (m == Mode.Settings)
            {
                settingsPanel.Open(); settingsBack.Show(0.1f); fullscreenButton.Show(0.05f);
                panelIndex = 0; RefreshSettingsFocus(false); RefreshFullscreenLabel();
            }
            else { controlsPanel.Open(); controlsBack.Show(0.1f); }
        }

        void ClosePanel()
        {
            if (mode == Mode.Main) return;
            if (sfx != null && backClip != null) sfx.PlayOneShot(backClip, 0.9f);
            if (mode == Mode.Settings) { settingsPanel.Close(); settingsBack.HideInstantly(); fullscreenButton.HideInstantly(); GameSettings.Save(); }
            else { controlsPanel.Close(); controlsBack.HideInstantly(); }
            int prev = mode == Mode.Settings ? 2 : 1;
            mode = Mode.Main;
            foreach (var b in mainButtons) b.Interactable = true;
            Select(prev);
        }

        // ------------------------------------------------------------------------------------------------ settings
        void SetupSettings()
        {
            if (masterSlider != null)
            {
                masterSlider.SetValueWithoutNotify(GameSettings.Master);
                masterSlider.onValueChanged.AddListener(v => { GameSettings.Master = v; UpdateValueTexts(); });
            }
            if (musicSlider != null)
            {
                musicSlider.SetValueWithoutNotify(GameSettings.Music);
                musicSlider.onValueChanged.AddListener(v => { GameSettings.Music = v; UpdateValueTexts(); });
            }
            UpdateValueTexts(); RefreshFullscreenLabel();
        }

        void UpdateValueTexts()
        {
            if (masterValue != null) masterValue.text = Mathf.RoundToInt(GameSettings.Master * 100f) + "%";
            if (musicValue != null) musicValue.text = Mathf.RoundToInt(GameSettings.Music * 100f) + "%";
        }

        void NudgeSlider(float d)
        {
            if (panelIndex == 0 && masterSlider != null) masterSlider.value = Mathf.Clamp01(masterSlider.value + d);
            else if (panelIndex == 1 && musicSlider != null) musicSlider.value = Mathf.Clamp01(musicSlider.value + d);
        }

        void RefreshSettingsFocus(bool sound)
        {
            Color gold = new Color(1f, 0.86f, 0.42f), idle = new Color(0.93f, 0.94f, 0.87f);
            if (masterLabel != null) masterLabel.color = panelIndex == 0 ? gold : idle;
            if (musicLabel != null) musicLabel.color = panelIndex == 1 ? gold : idle;
            fullscreenButton.SetSelected(panelIndex == 2);
            settingsBack.SetSelected(panelIndex == 3);
            if (sound && sfx != null && hoverClip != null) sfx.PlayOneShot(hoverClip, 0.8f);
        }

        void ToggleFullscreen()
        {
            fullscreen = !fullscreen;
            Screen.fullScreen = fullscreen;
            RefreshFullscreenLabel();
        }

        void RefreshFullscreenLabel()
        {
            if (fullscreenLabel == null) return;
            fullscreenLabel.text = "FULLSCREEN:  " + (fullscreen ? "ON" : "OFF");
        }

        // ------------------------------------------------------------------------------------------------ editor preview
        /// <summary>(Editor tool) the cut scene at time t (before the title appears).</summary>
        public void PreviewAt(float t)
        {
            Awake();
            mainButtons = new List<MenuButton> { playButton, controlsButton, settingsButton, quitButton };
            BuildHeroes();
            ComputeCamera();
            const float dt = 1f / 30f;
            for (float x = 0f; x <= t; x += dt) foreach (var h in heroes) h.Step(x, dt);
            T = t;
            PositionCamera(t);
            if (logoGroup != null) logoGroup.alpha = 0f;
            if (hintGroup != null) hintGroup.alpha = 0f;
            foreach (var b in mainButtons) if (b.group != null) b.group.alpha = 0f;
            if (fade != null) { var c = fade.color; c.a = Mathf.Clamp01(1f - t / 1.4f); fade.color = c; }
        }

        /// <summary>(Editor tool) the final state of the menu: everybody posed, the title and the buttons visible.</summary>
        public void PreviewFinalState()
        {
            Awake();
            mainButtons = new List<MenuButton> { playButton, controlsButton, settingsButton, quitButton };
            BuildHeroes();
            ComputeCamera();
            foreach (var h in heroes) h.SnapFinal();
            T = 20f; logoShown = true; logoAt = -100f;
            if (logoGroup != null) { logoGroup.alpha = 1f; logoRect.localScale = Vector3.one; logoRect.anchoredPosition = new Vector2(0f, -30f); }
            if (logoGlow != null) { var c = logoGlow.color; c.a = 0.45f; logoGlow.color = c; }
            if (hintGroup != null) hintGroup.alpha = 0.8f;
            if (fade != null) { var c = fade.color; c.a = 0f; fade.color = c; }
            foreach (var b in mainButtons) { b.slideFrom = Vector2.zero; if (b.group != null) b.group.alpha = 1f; }
            if (cam != null)
            {
                cam.transform.position = finalPos; cam.transform.rotation = Quaternion.LookRotation(finalFocus - finalPos, Vector3.up); cam.fieldOfView = finalFov;
            }
        }

        /// <summary>(Editor tool) open a panel in its final state: 0 = none, 1 = settings, 2 = controls.</summary>
        public void PreviewPanel(int which)
        {
            if (settingsPanel != null) settingsPanel.SetOpenInstantly(which == 1);
            if (controlsPanel != null) controlsPanel.SetOpenInstantly(which == 2);
            if (which == 1) { settingsBack.slideFrom = Vector2.zero; fullscreenButton.slideFrom = Vector2.zero; if (settingsBack.group != null) settingsBack.group.alpha = 1f; if (fullscreenButton.group != null) fullscreenButton.group.alpha = 1f; }
            if (which == 2) { controlsBack.slideFrom = Vector2.zero; if (controlsBack.group != null) controlsBack.group.alpha = 1f; }
        }
    }
}
