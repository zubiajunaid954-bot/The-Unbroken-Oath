using UnityEngine;
using GAMECON.Mission;

namespace GAMECON.Menu
{
    /// <summary>
    /// One of the four warriors in the main menu: she walks forward with her weapon (footsteps on the stone), stops at her place and settles into
    /// her hero pose. The arms follow the weapon (two-bone IK), so the rifle and the sword stay in her hands all the time.
    /// </summary>
    public class MenuHero : MonoBehaviour
    {
        public enum Carry { Rifle, SwordLow, SwordPlanted }

        public HeroRig rig;
        public Carry carry;
        public Transform weapon;
        public HeroRig.Pose finalPose;
        public Vector3 slot;                   // final position (x, z used)
        public Vector3 walkDir;                // the direction she walks in (unit, horizontal)
        public float dist, startAt, walkYaw, finalYaw;
        public AudioSource stepSource;
        public AudioClip stepClip;
        public Transform listener;

        float travelled, arrivedAt = -1f, groundY;
        int lastStep;
        bool inited;
        const float WalkSpeed = 1.45f, CycleLength = 1.39f;

        struct Spec { public Vector3 pos; public Quaternion rot; }

        static Vector3 Dn(float x, float y, float z) { return new Vector3(x, y, z).normalized; }

        // hero-local weapon placements (the same as the key art)
        static Spec Rifle() { return new Spec { pos = new Vector3(0.24f, 1.00f, 0.18f), rot = Quaternion.LookRotation(Dn(-0.80f, 0.52f, 0.30f), Dn(0.35f, 0.85f, -0.20f)) }; }
        static Spec SwordLow() { return new Spec { pos = new Vector3(0.30f, 0.94f, 0.10f), rot = Quaternion.LookRotation(Dn(0f, 0f, 1f), Dn(0.30f, -0.60f, 0.74f)) }; }
        static Spec SwordPlanted() { return new Spec { pos = new Vector3(0.0f, 0.99f, 0.30f), rot = Quaternion.LookRotation(Dn(0f, 0f, 1f), Dn(0f, -1f, 0f)) }; }

        static readonly Vector3 RifleRight = new Vector3(0f, -0.10f, -0.07f), RifleLeft = new Vector3(0f, -0.045f, 0.20f);

        static float Ease(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }

        static float GroundAt(Vector3 p, float fallback)
        {
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(p.x, fallback + 8f, p.z), Vector3.down, out hit, 20f, ~0, QueryTriggerInteraction.Ignore) && hit.normal.y > 0.5f) return hit.point.y;
            return fallback;
        }

        public Vector3 StartPosition { get { return slot - walkDir * dist; } }

        public void Init()
        {
            Vector3 s = StartPosition;
            groundY = GroundAt(s, slot.y);
            transform.position = new Vector3(s.x, groundY, s.z);
            transform.rotation = Quaternion.Euler(0f, walkYaw, 0f);
            rig.pose = finalPose; rig.walkWeight = 0f; rig.poseWeight = 0f;
            travelled = 0f; arrivedAt = -1f; lastStep = 0; inited = true;
            Pose(0f, 0f);
        }

        /// <summary>Advance to time T (seconds since the start).</summary>
        public void Step(float T, float dt)
        {
            if (!inited) Init();
            float remaining = dist - travelled;
            float pw = 0f, ww = 0f;
            if (T >= startAt && remaining > 0.02f)
            {
                float speed = Mathf.Min(WalkSpeed, Mathf.Max(0.3f, remaining * 1.4f + 0.2f));
                travelled += speed * dt;
                rig.phase += speed / CycleLength * Mathf.PI * 2f * dt;
                ww = 1f; pw = 0f;
            }
            else if (remaining <= 0.02f)
            {
                travelled = dist;
                if (arrivedAt < 0f) arrivedAt = T;
                float since = T - arrivedAt;
                ww = 1f - Ease(since / 0.5f);
                pw = Ease((since - 0.15f) / 1.2f);
                if (since < 0.5f) rig.phase += 0.45f * (1f - since / 0.5f) / CycleLength * Mathf.PI * 2f * dt;
            }
            Vector3 p = slot - walkDir * (dist - travelled);
            groundY = Mathf.Lerp(groundY, GroundAt(p, groundY), 1f - Mathf.Exp(-12f * dt));
            transform.position = new Vector3(p.x, groundY, p.z);
            transform.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(walkYaw, finalYaw, pw), 0f);
            Pose(ww, pw);

            // footsteps: a foot lands every half cycle
            if (Application.isPlaying && ww > 0.5f && stepSource != null && stepClip != null)
            {
                int step = Mathf.FloorToInt((rig.phase - Mathf.PI * 0.5f) / Mathf.PI);
                if (step != lastStep)
                {
                    lastStep = step;
                    float d = listener != null ? Vector3.Distance(listener.position, transform.position) : 6f;
                    stepSource.pitch = Random.Range(0.9f, 1.1f);
                    stepSource.PlayOneShot(stepClip, Mathf.Clamp01(1.15f - d / 20f) * 0.5f + 0.08f);
                }
            }
        }

        /// <summary>The four of them in their final poses.</summary>
        public void SnapFinal()
        {
            if (!inited) Init();
            travelled = dist; arrivedAt = -100f;
            groundY = GroundAt(slot, groundY);
            transform.position = new Vector3(slot.x, groundY, slot.z);
            transform.rotation = Quaternion.Euler(0f, finalYaw, 0f);
            Pose(0f, 1f);
        }

        void Pose(float ww, float pw)
        {
            rig.walkWeight = ww; rig.poseWeight = pw; rig.pose = finalPose;
            rig.ClearOverrides();
            rig.Apply();                                              // torso, legs, and the arms as animated
            if (weapon == null) return;

            Transform T = transform;
            Spec a = carry == Carry.SwordPlanted ? SwordLow() : (carry == Carry.Rifle ? Rifle() : SwordLow());
            Spec b = carry == Carry.SwordPlanted ? SwordPlanted() : a;
            float w = Ease(pw);
            Vector3 lp = Vector3.Lerp(a.pos, b.pos, w);
            Quaternion lr = Quaternion.Slerp(a.rot, b.rot, w);
            Quaternion wr = T.rotation * lr;
            Vector3 gripWorld = T.TransformPoint(lp);

            Vector3 rightGrip, leftGrip;
            if (carry == Carry.Rifle) { rightGrip = RifleRight; leftGrip = RifleLeft; }
            else if (carry == Carry.SwordPlanted) { rightGrip = Vector3.Lerp(Vector3.zero, new Vector3(0f, -0.045f, 0f), w); leftGrip = new Vector3(0f, 0.025f, 0f); }
            else { rightGrip = Vector3.zero; leftGrip = Vector3.zero; }

            weapon.rotation = wr;
            Vector3 anchor = carry == Carry.Rifle ? RifleRight : Vector3.zero;                      // the point of the weapon that is placed at "lp"
            weapon.position = gripWorld - wr * anchor;

            Vector3 D(float x, float y, float z) { return T.TransformDirection(new Vector3(x, y, z)).normalized; }
            rig.SolveArm(true, weapon.TransformPoint(rightGrip), carry == Carry.Rifle ? D(0.7f, -0.7f, -0.2f) : D(0.6f, -0.8f, -0.3f), carry == Carry.Rifle ? 0.07f : 0.05f, 1f);
            if (carry == Carry.Rifle)
                rig.SolveArm(false, weapon.TransformPoint(leftGrip), D(-0.6f, -0.8f, -0.1f), 0.07f, 1f);
            else if (carry == Carry.SwordPlanted)
                rig.SolveArm(false, weapon.TransformPoint(leftGrip), D(-0.7f, -0.7f, -0.2f), 0.07f, w);
            else
                rig.SolveArm(false, T.TransformPoint(new Vector3(-0.23f, 0.98f, 0f)), D(-0.8f, -0.4f, -0.4f), 0.04f, w);
            rig.Apply();                                              // the arms now follow the weapon
        }
    }
}
