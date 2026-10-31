using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using scgFullBodyController;
using GAMECON.Voice;

namespace GAMECON.Combat
{
    /// <summary>
    /// Grenades for Samrah. Press and hold G: the rifle is lowered, a grenade is taken out and the pin is pulled
    /// (the fuse starts - holding it "cooks" the grenade, the longer you hold the shorter the fuse after the throw).
    /// A dotted arc shows where it will land and a red ring shows the lethal radius. Release G to throw.
    /// </summary>
    public class GrenadeThrower : MonoBehaviour
    {
        public static GrenadeThrower Instance { get; private set; }

        [Header("Inventory")]
        public int grenades = 4;
        public int maxGrenades = 6;

        [Header("Throw")]
        public GameObject grenadePrefab;
        public float fuseTime = 3.5f;       // from the moment the pin is pulled
        public float maxCook = 2.8f;        // thrown automatically after this long (never explodes in your hand)
        public float throwSpeed = 15f;
        public float loft = 3.2f;
        public float minHold = 0.28f;       // taking the grenade out takes a moment
        public AudioClip pinClip, throwClip, ringingClip;
        public Material arcMaterial;

        [Header("HUD (built by the editor script)")]
        public Text countText;
        public Image boxFrame;
        public Image cookBar;
        public Image flash;

        enum S { Idle, Cooking, Throwing }
        S state = S.Idle;
        float held;
        bool releasePending;
        Grenade inHand;
        GunController gun;
        List<Renderer> hiddenGun = new List<Renderer>();
        Camera cam;
        Rigidbody playerBody;
        LineRenderer arc, ring;
        AudioSource audioSrc;
        float flashAlpha, shake;
        CameraController camCtl;
        float emptyBlink;

        void Awake()
        {
            Instance = this;
            audioSrc = gameObject.AddComponent<AudioSource>(); audioSrc.spatialBlend = 0f; audioSrc.playOnAwake = false;
        }

        void OnEnable() { Grenade.Exploded += OnExploded; }
        void OnDisable() { Grenade.Exploded -= OnExploded; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            MakeLines();
            RefreshHud();
        }

        // ------------------------------------------------------------------ input
        void Update()
        {
            if (cam == null) cam = Camera.main;
            if (camCtl == null) camCtl = FindObjectOfType<CameraController>();
            var m = Mission.MissionManager.Instance;
            bool blocked = m != null && (m.Failed || m.Complete || (Mission.CutsceneDirector.Instance != null && Mission.CutsceneDirector.Instance.Playing)
                                  || (Mission.MissionBriefing.Instance != null && Mission.MissionBriefing.Instance.Active));

            if (!blocked && state == S.Idle && Input.GetButtonDown("Grenade")) TryBegin();

            if (state == S.Cooking)
            {
                held += Time.deltaTime;
                if (Input.GetButtonUp("Grenade")) releasePending = true;
                if (releasePending && held >= minHold) StartCoroutine(Throw());
                else if (held >= maxCook) StartCoroutine(Throw());
                else { UpdateArc(); HoldVisual(); }
            }
            if (cookBar != null) cookBar.fillAmount = state == S.Cooking ? Mathf.Clamp01(held / fuseTime) : 0f;

            // HUD feedback
            emptyBlink = Mathf.MoveTowards(emptyBlink, 0f, Time.unscaledDeltaTime * 3f);
            if (boxFrame != null)
                boxFrame.color = state == S.Cooking ? new Color(1f, 0.75f, 0.2f, 0.95f)
                               : Color.Lerp(new Color(0f, 0f, 0f, 0.6f), new Color(0.9f, 0.1f, 0.1f, 0.9f), emptyBlink);

            // explosion feedback: flash + shake
            if (flash != null) { flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, Time.unscaledDeltaTime * 0.9f); var c = flash.color; c.a = flashAlpha; flash.color = c; }
            shake = Mathf.MoveTowards(shake, 0f, Time.unscaledDeltaTime * 1.4f);
            if (camCtl != null)
                camCtl.shakeOffset = shake > 0.001f
                    ? new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f) * 0.5f) * (shake * shake * 7f)
                    : Vector3.zero;
        }

        void TryBegin()
        {
            if (grenades <= 0)
            {
                emptyBlink = 1f;
                VoiceAssistant.Say(VoiceLine.GrenadeEmpty);
                return;
            }
            gun = FindObjectOfType<GunController>();
            if (gun == null || gun.reloading || cam == null || grenadePrefab == null) return;

            state = S.Cooking; held = 0f; releasePending = false;
            if (pinClip != null) audioSrc.PlayOneShot(pinClip, 0.9f);

            // put the rifle away, take a grenade out
            gun.swapping = true; gun.firing = false;
            hiddenGun.Clear();
            foreach (var r in gun.GetComponentsInChildren<Renderer>(true)) if (r.enabled) { r.enabled = false; hiddenGun.Add(r); }
            var go = Instantiate(grenadePrefab, cam.transform);
            inHand = go.GetComponent<Grenade>();
            inHand.Hold();
            go.transform.localPosition = new Vector3(0.30f, -0.50f, 0.40f);
            go.transform.localRotation = Quaternion.Euler(20f, 30f, 0f);
            if (playerBody == null) { var uc = FindObjectOfType<ThirdPersonUserControl>(); if (uc != null) playerBody = uc.GetComponent<Rigidbody>(); }
        }

        // the grenade sits in the lower right of the view; it rises while you hold G, and trembles a bit when it is nearly out of time
        void HoldVisual()
        {
            if (inHand == null) return;
            float k = Mathf.Clamp01(held / 0.35f);
            Vector3 target = new Vector3(0.20f, -0.24f, 0.55f);
            Vector3 from = new Vector3(0.30f, -0.50f, 0.40f);
            Vector3 pos = Vector3.Lerp(from, target, k * k * (3f - 2f * k));
            if (held > fuseTime - 1.4f) pos += Random.insideUnitSphere * 0.004f;
            inHand.transform.localPosition = pos;
        }

        // ------------------------------------------------------------------ throw
        Vector3 StartPoint() { return cam.transform.position + cam.transform.forward * 0.55f + cam.transform.right * 0.18f - cam.transform.up * 0.10f; }

        Vector3 ThrowVelocity()
        {
            Vector3 v = cam.transform.forward * throwSpeed + Vector3.up * loft;
            if (playerBody != null) v += playerBody.velocity * 0.6f;
            return v;
        }

        IEnumerator Throw()
        {
            state = S.Throwing;
            HideArc();
            if (inHand != null && cam != null)
            {
                // a quick forward flick of the arm
                Vector3 a = inHand.transform.localPosition, b = new Vector3(0.12f, -0.10f, 0.95f);
                for (float t = 0; t < 0.12f; t += Time.deltaTime) { inHand.transform.localPosition = Vector3.Lerp(a, b, t / 0.12f); yield return null; }

                float remaining = Mathf.Max(0.6f, fuseTime - held);
                inHand.transform.position = StartPoint();
                inHand.Launch(ThrowVelocity(), remaining);
                inHand = null;
                grenades = Mathf.Max(0, grenades - 1);
                if (throwClip != null) audioSrc.PlayOneShot(throwClip, 0.8f);
                VoiceAssistant.Say(VoiceLine.GrenadeThrow);
                RefreshHud();
            }
            yield return new WaitForSeconds(0.55f);

            // rifle back in hand
            foreach (var r in hiddenGun) if (r != null) r.enabled = true;
            hiddenGun.Clear();
            if (gun != null) gun.swapping = false;
            state = S.Idle;
        }

        // ------------------------------------------------------------------ trajectory preview
        void MakeLines()
        {
            arc = NewLine("GrenadeArc", 0.035f, 40);
            ring = NewLine("GrenadeRing", 0.06f, 49);
            if (ring != null) { ring.loop = false; ring.startColor = ring.endColor = new Color(1f, 0.15f, 0.1f, 0.85f); }
            HideArc();
        }

        LineRenderer NewLine(string name, float width, int points)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 0; lr.widthMultiplier = width; lr.numCapVertices = 2;
            lr.useWorldSpace = true; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
            if (arcMaterial != null) lr.sharedMaterial = arcMaterial;
            lr.startColor = lr.endColor = new Color(1f, 0.9f, 0.5f, 0.8f);
            return lr;
        }

        void HideArc()
        {
            if (arc != null) arc.positionCount = 0;
            if (ring != null) ring.positionCount = 0;
        }

        void UpdateArc()
        {
            if (arc == null || cam == null) return;
            Vector3 p = StartPoint(), v = ThrowVelocity();
            var pts = new List<Vector3> { p };
            Vector3 landing = p; bool landed = false;
            for (int i = 0; i < 60; i++)
            {
                Vector3 next = p + v * 0.05f + 0.5f * Physics.gravity * 0.0025f;
                v += Physics.gravity * 0.05f;
                if (Physics.Linecast(p, next, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<ThirdPersonUserControl>() == null)
                {
                    pts.Add(hit.point); landing = hit.point; landed = true; break;
                }
                pts.Add(next); p = next;
                if (i > 38) break;
            }
            arc.positionCount = pts.Count;
            arc.SetPositions(pts.ToArray());

            // lethal radius ring where it lands
            if (ring != null)
            {
                if (landed)
                {
                    float r = inHand != null ? inHand.lethalRadius : 6.5f;
                    ring.positionCount = 49;
                    for (int i = 0; i < 49; i++)
                    {
                        float a = i / 48f * Mathf.PI * 2f;
                        Vector3 q = landing + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                        if (Physics.Raycast(q + Vector3.up * 3f, Vector3.down, out RaycastHit gh, 8f, ~0, QueryTriggerInteraction.Ignore)) q.y = gh.point.y + 0.08f;
                        else q.y = landing.y + 0.08f;
                        ring.SetPosition(i, q);
                    }
                }
                else ring.positionCount = 0;
            }
        }

        // ------------------------------------------------------------------ explosions
        void OnExploded(Vector3 pos, float lethal, float blast)
        {
            if (cam == null) return;
            float d = Vector3.Distance(cam.transform.position, pos);
            shake = Mathf.Max(shake, Mathf.Clamp01(1f - d / 28f));
            if (d < 14f) flashAlpha = Mathf.Max(flashAlpha, Mathf.Clamp01(1.1f - d / 14f));
            if (d < 9f && ringingClip != null) audioSrc.PlayOneShot(ringingClip, Mathf.Clamp01(0.7f - d / 14f));
        }

        // ------------------------------------------------------------------ HUD
        public void AddGrenades(int n) { grenades = Mathf.Min(maxGrenades, grenades + n); RefreshHud(); }

        void RefreshHud()
        {
            if (countText != null) countText.text = "x " + grenades;
        }
    }
}
