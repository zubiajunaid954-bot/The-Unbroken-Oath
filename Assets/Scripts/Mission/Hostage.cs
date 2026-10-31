using UnityEngine;
using GAMECON.Voice;

namespace GAMECON.Mission
{
    /// <summary>
    /// A captured, tied-up, kneeling teammate (Duaa, Zubia or Maha). Walk up and press the interact key (E): the rescue
    /// cut scene plays, she stands up and thanks Samrah, then the rescue counts.
    /// </summary>
    public class Hostage : MonoBehaviour
    {
        public float interactRadius = 2.6f;
        public GameObject beacon;           // floating marker above the head
        public AudioClip freedSound;
        public string displayName = "Teammate";

        [Header("Captive look")]
        public Animator animator;                         // (only used by rigged characters)
        public RuntimeAnimatorController freedController; // normal standing idle, used after being freed
        public GameObject[] ropes;                        // hidden when freed

        [Header("Rigged character (built by GAMECON > Rig Teammates)")]
        public CaptiveRig rig;                            // skeleton animation: kneeling -> standing -> talking

        [Header("Kneeling pose (models without a skeleton are lowered into the floor and bowed forward)")]
        public Transform kneelPivot;                      // hips; lowering / bowing happens around it
        public float kneelDrop = 0.45f;                   // metres the body is lowered while kneeling
        public float kneelPitch = 14f;                    // degrees bowed forward while kneeling
        Vector3 kneelLocalPos;

        public bool Freed { get; private set; }

        void Start()
        {
            if (kneelPivot != null) kneelLocalPos = kneelPivot.localPosition;     // pose as saved in the scene (kneeling)
        }

        // The interact prompt / key is handled by the MissionManager, which picks the ONE teammate you are looking at,
        // so the prompt, the cut scene and the dialogue always belong to the same person.

        public void Free()
        {
            if (Freed) return;
            Freed = true;
            if (beacon != null) beacon.SetActive(false);
            VoiceAssistant.Say(VoiceLine.Rescue);
            if (freedSound != null) AudioSource.PlayClipAtPoint(freedSound, transform.position, 1f);

            var m = MissionManager.Instance;
            var cs = CutsceneDirector.Instance;
            if (m == null) { CutRopes(); SetStanding(1f); return; }
            bool last = (m.hostages.Count - m.FreedCount) <= 1;
            if (cs != null) cs.PlayRescue(this, last, () => m.OnHostageFreed(this));
            else { CutRopes(); SetStanding(1f); m.OnHostageFreed(this); }
        }

        /// <summary>The ropes fall away.</summary>
        public void CutRopes()
        {
            if (ropes != null) foreach (var r in ropes) if (r != null) r.SetActive(false);
            if (animator != null && freedController != null) animator.runtimeAnimatorController = freedController;
        }

        /// <summary>Where her head is right now (for the camera).</summary>
        public Vector3 HeadWorld()
        {
            if (rig != null) return rig.HeadWorld();
            var rs = GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return transform.position + Vector3.up * 1.2f;
            Bounds b = rs[0].bounds;
            foreach (var r in rs) if (!(r is SpriteRenderer)) b.Encapsulate(r.bounds);
            return new Vector3(b.center.x, b.max.y - 0.12f, b.center.z);
        }

        public void SetTalking(bool talking) { if (rig != null) rig.SetTalking(talking ? 1f : 0f); }
        public void SetListening(bool listening) { if (rig != null) rig.SetListening(listening ? 1f : 0f); }

        /// <summary>0 = kneeling, 1 = standing.</summary>
        public void SetStanding(float k)
        {
            if (rig != null) { rig.SetState(k); return; }
            if (kneelPivot == null) return;
            k = Mathf.Clamp01(k);
            kneelPivot.localPosition = kneelLocalPos + Vector3.up * (kneelDrop * k);
            kneelPivot.localRotation = Quaternion.Euler(kneelPitch * (1f - k), 0f, 0f);
        }
    }
}
