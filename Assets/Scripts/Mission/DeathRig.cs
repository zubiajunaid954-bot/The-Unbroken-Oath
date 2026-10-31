using UnityEngine;

namespace GAMECON.Mission
{
    /// <summary>
    /// Samrah's death pose, on the skeleton the TeammateRigBuilder generated (it re-uses the bones of the CaptiveRig).
    /// She lies on her back, head turned to the side, one knee bent, the left arm flopped out; the right arm is still raised,
    /// trembling weakly, and then falls to the ground (upper arm first, forearm and hand following late, with a small bounce).
    /// Root space: she lies along Z with her head towards -Z, her face up.
    /// </summary>
    public class DeathRig : MonoBehaviour
    {
        public CaptiveRig rig;
        public float hipsHeight = 0.17f;       // pelvis height above the ground while lying
        public float fallT = -1f;              // < 0: arm still raised; >= 0: seconds since the arm let go
        [Range(0f, 1f)] public float tremble = 1f;
        [Range(0f, 1f)] public float breath = 1f;
        public float fallDuration = 0.85f;

        // bone indices (the same order as CaptiveRig.B)
        const int Spine = 0, Chest = 1, Neck = 2, Head = 3, ShoulderP = 4, ShoulderM = 5, UpperArmP = 6, UpperArmM = 7, LowerArmP = 8, LowerArmM = 9,
                  HandP = 10, HandM = 11, UpperLegP = 12, UpperLegM = 13, LowerLegP = 14, LowerLegM = 15, FootP = 16, FootM = 17;
        static readonly int[] parent = { -1, 0, 1, 2, 1, 1, 4, 5, 6, 7, 8, 9, -1, -1, 12, 13, 14, 15 };

        public void Setup(CaptiveRig r)
        {
            rig = r;
            if (rig != null) { rig.live = false; rig.enabled = false; }
        }

        public Vector3 HandWorld() { return rig != null && rig.bones[HandP] != null ? rig.bones[HandP].position : transform.position; }
        public Vector3 HeadWorld() { return rig != null && rig.bones[Head] != null ? rig.bones[Head].position : transform.position; }
        public Vector3 ChestWorld() { return rig != null && rig.bones[Chest] != null ? rig.bones[Chest].position : transform.position; }

        static Vector3 Mix(Vector3 a, Vector3 b, float k) { return Vector3.Slerp(a.normalized, b.normalized, Mathf.Clamp01(k)); }

        // how far along its fall a limb is (0 = raised, 1 = on the ground), with a small bounce at the end; delay = it lags behind
        float Fall(float delay, float bounce)
        {
            if (fallT < 0f) return 0f;
            float u = (fallT - delay) / fallDuration;
            if (u <= 0f) return 0f;
            if (u < 1f) return u * u;
            return 1f - bounce * Mathf.Sin(Mathf.Clamp01((u - 1f) / 0.45f) * Mathf.PI);
        }

        Vector3 Dir(int b, float t)
        {
            float shake = tremble * (fallT < 0f ? 1f : 0f);
            switch (b)
            {
                case Spine: return new Vector3(0f, 0.02f, -1f);
                case Chest: return new Vector3(0f, 0.05f + 0.012f * breath * Mathf.Sin(t * 1.4f), -1f);
                case Neck:  return new Vector3(-0.04f, 0.20f, -1f);
                case Head:  return new Vector3(-0.30f, 0.22f, -0.93f);          // head turned to the side, resting

                case ShoulderP: return new Vector3(1f, 0.06f, 0f);
                case ShoulderM: return new Vector3(-1f, 0.04f, 0f);

                // the right arm: reaching up, then falling
                case UpperArmP:
                {
                    var up = new Vector3(0.30f + 0.04f * shake * Mathf.Sin(t * 19f), 0.95f, 0.14f + 0.04f * shake * Mathf.Sin(t * 23f + 1f));
                    return Mix(up, new Vector3(0.97f, -0.12f, 0.20f), Fall(0f, 0.05f));
                }
                case LowerArmP:
                {
                    var up = new Vector3(0.16f + 0.06f * shake * Mathf.Sin(t * 21f), 0.98f, 0.12f + 0.05f * shake * Mathf.Sin(t * 17f));
                    return Mix(up, new Vector3(0.93f, -0.06f, 0.36f), Fall(0.12f, 0.08f));
                }
                case HandP:
                {
                    var up = new Vector3(0.05f + 0.10f * shake * Mathf.Sin(t * 25f), 0.99f, 0.14f + 0.08f * shake * Mathf.Sin(t * 20f + 2f));
                    return Mix(up, new Vector3(0.86f, -0.30f, 0.36f), Fall(0.24f, 0.16f));
                }

                // the left arm: already lying, loosely out to the side
                case UpperArmM: return new Vector3(-0.95f, -0.10f, 0.30f);
                case LowerArmM: return new Vector3(-0.80f, -0.02f, 0.60f);
                case HandM:     return new Vector3(-0.50f, -0.10f, 0.85f);

                // legs: right leg straight and a little open, left knee bent and fallen outwards
                case UpperLegP: return new Vector3(0.14f, 0.0f, 1f);
                case LowerLegP: return new Vector3(0.20f, 0.0f, 1f);
                case FootP:     return new Vector3(0.35f, 0.88f, 0.20f);
                case UpperLegM: return new Vector3(-0.25f, 0.55f, 0.80f);
                case LowerLegM: return new Vector3(-0.28f, -0.75f, 0.60f);
                case FootM:     return new Vector3(-0.35f, -0.10f, 0.93f);
            }
            return Vector3.up;
        }

        public void Apply()
        {
            if (rig == null || rig.hips == null || rig.bones == null || rig.bones.Length < 18) return;
            float t = Time.unscaledTime;

            var hips = rig.hips;
            Quaternion lie = Quaternion.Euler(-90f, 0f, 0f);                      // up -> -Z (head), forward -> up (face up)
            hips.localPosition = new Vector3(rig.hipsBindLocalPos.x, hipsHeight, rig.hipsBindLocalPos.z);
            hips.localRotation = lie;

            var rotRoot = new Quaternion[18];
            for (int i = 0; i < 18; i++)
            {
                var tr = rig.bones[i];
                if (tr == null) continue;
                Vector3 d = Dir(i, t).normalized;
                Quaternion r = Quaternion.FromToRotation(rig.bindDir[i].normalized, d);
                int p = parent[i];
                Quaternion pr = p >= 0 ? rotRoot[p] : lie;
                tr.localRotation = Quaternion.Inverse(pr) * r;
                rotRoot[i] = r;
            }
        }
    }
}
