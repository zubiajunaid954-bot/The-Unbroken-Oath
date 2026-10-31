using UnityEngine;

namespace GAMECON.Mission
{
    /// <summary>
    /// Victory animation for the generated skeletons: a confident walk cycle and a set of "hero squad" poses (hands on hips, wide stance,
    /// fist raised, kneeling with a hand on the knee). Works on the bones of the CaptiveRig (character faces +Z, +X is her right).
    /// Every pose is a table of world directions per bone (the same method as the rescue animation); the walk and the pose are blended.
    /// </summary>
    public class HeroRig : MonoBehaviour
    {
        public enum Pose { Stand, Hips, HipsWide, FistUp, Ready, Kneel, Stride, StrideBack }

        public CaptiveRig rig;
        public Pose pose = Pose.Stand;
        [Range(0f, 1f)] public float poseWeight;       // 0 = walking / standing, 1 = the hero pose
        [Range(0f, 1f)] public float walkWeight;       // 1 = walking
        public float phase;                            // walk cycle phase (radians)
        public float seed;                             // small per-character differences in the idle motion
        public bool live;                              // keep animating in Update (after the cut scene)
        [System.NonSerialized] public Vector3?[] lastDir = new Vector3?[18];         // the pose direction of each bone before an override
        [System.NonSerialized] public Vector3?[] overrideDir = new Vector3?[18];     // (editor tools) root-space directions that replace the pose of a bone

        // bone indices (same order as CaptiveRig.B)
        const int Spine = 0, Chest = 1, Neck = 2, Head = 3, ShoulderP = 4, ShoulderM = 5, UpperArmP = 6, UpperArmM = 7, LowerArmP = 8, LowerArmM = 9,
                  HandP = 10, HandM = 11, UpperLegP = 12, UpperLegM = 13, LowerLegP = 14, LowerLegM = 15, FootP = 16, FootM = 17;
        static readonly int[] parent = { -1, 0, 1, 2, 1, 1, 4, 5, 6, 7, 8, 9, -1, -1, 12, 13, 14, 15 };

        const float WalkAmp = 26f;                     // degrees of leg swing
        const float LegLength = 0.79f;                 // hip joint to ankle

        public void Setup(CaptiveRig r)
        {
            rig = r;
            if (rig != null) { rig.live = false; rig.enabled = false; }
        }

        void Update() { if (live) Apply(); }

        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
        static int SideOf(int b) { return (b == ShoulderP || b == UpperArmP || b == LowerArmP || b == HandP || b == UpperLegP || b == LowerLegP || b == FootP) ? 1 : -1; }

        // ---------------------------------------------------------------- pose tables
        enum ArmKind { Down, Hip, Up, Ready, Knee, FistThigh }

        struct PoseDef { public float chestBack, lean, legOpen, toeOut, drop, fwd; public ArmKind armP, armM; public bool kneel; public int stride; }

        static PoseDef Def(Pose p)
        {
            switch (p)
            {
                case Pose.Hips:     return new PoseDef { chestBack = 0.05f, lean = 0f, legOpen = 0.17f, toeOut = 0.25f, drop = 0f,     fwd = 0f, armP = ArmKind.Hip, armM = ArmKind.Hip };
                case Pose.HipsWide: return new PoseDef { chestBack = 0.06f, lean = 0f, legOpen = 0.26f, toeOut = 0.35f, drop = 0.012f, fwd = 0f, armP = ArmKind.Hip, armM = ArmKind.Hip };
                case Pose.FistUp:   return new PoseDef { chestBack = 0.06f, lean = 0f, legOpen = 0.20f, toeOut = 0.30f, drop = 0.008f, fwd = 0f, armP = ArmKind.Up,  armM = ArmKind.Hip };
                case Pose.Ready:    return new PoseDef { chestBack = 0.05f, lean = 0f, legOpen = 0.24f, toeOut = 0.30f, drop = 0.01f,  fwd = 0f, armP = ArmKind.Ready, armM = ArmKind.Ready };
                case Pose.Stride:     return new PoseDef { chestBack = 0.04f, lean = 0.02f, legOpen = 0.10f, toeOut = 0.15f, drop = 0.10f, fwd = 0f, armP = ArmKind.Ready, armM = ArmKind.Ready, stride = 1 };
                case Pose.StrideBack: return new PoseDef { chestBack = 0.04f, lean = 0.02f, legOpen = 0.10f, toeOut = 0.15f, drop = 0.10f, fwd = 0f, armP = ArmKind.Ready, armM = ArmKind.Ready, stride = -1 };
                case Pose.Kneel:    return new PoseDef { chestBack = 0f,    lean = 0.10f, legOpen = 0f, toeOut = 0f, drop = 0.95f, fwd = 0.05f, armP = ArmKind.Knee, armM = ArmKind.FistThigh, kneel = true };
                default:            return new PoseDef { chestBack = 0f,    lean = 0f, legOpen = 0.04f, toeOut = 0.05f, drop = 0f,     fwd = 0f, armP = ArmKind.Down, armM = ArmKind.Down };
            }
        }

        static Vector3 ArmDir(ArmKind k, int part, float s)   // part: 0 shoulder, 1 upper arm, 2 forearm, 3 hand
        {
            switch (k)
            {
                case ArmKind.Hip:
                    switch (part) { case 0: return V(s, 0.04f, -0.06f); case 1: return V(s * 0.70f, -0.68f, -0.18f); case 2: return V(-s * 0.35f, -0.80f, 0.50f); default: return V(-s * 0.55f, -0.55f, 0.62f); }
                case ArmKind.Up:
                    switch (part) { case 0: return V(s, 0.10f, 0f); case 1: return V(s * 0.45f, 0.88f, 0.10f); case 2: return V(s * 0.12f, 0.99f, 0.08f); default: return V(0f, 1f, 0.10f); }
                case ArmKind.Ready:
                    switch (part) { case 0: return V(s, 0.03f, -0.04f); case 1: return V(s * 0.35f, -0.93f, -0.05f); case 2: return V(s * 0.25f, -0.90f, 0.35f); default: return V(s * 0.15f, -0.85f, 0.50f); }
                case ArmKind.Knee:
                    switch (part) { case 0: return V(s, 0.03f, 0.02f); case 1: return V(s * 0.12f, -0.88f, 0.46f); case 2: return V(s * 0.02f, -0.55f, 0.83f); default: return V(0f, -0.45f, 0.89f); }
                case ArmKind.FistThigh:
                    switch (part) { case 0: return V(s, 0.03f, 0f); case 1: return V(s * 0.20f, -0.95f, 0.24f); case 2: return V(-s * 0.10f, -0.75f, 0.65f); default: return V(-s * 0.15f, -0.65f, 0.74f); }
                default:
                    switch (part) { case 0: return V(s, 0.04f, 0f); case 1: return V(s * 0.18f, -0.98f, 0.04f); case 2: return V(s * 0.126f, -0.97f, 0.18f); default: return V(s * 0.09f, -0.97f, 0.20f); }
            }
        }

        static Vector3 PoseDir(Pose p, int b)
        {
            var d = Def(p);
            float s = SideOf(b);
            switch (b)
            {
                case Spine: return V(0f, 1f, d.lean * 0.5f);
                case Chest: return V(0f, 1f, -d.chestBack + d.lean);
                case Neck:  return V(0f, 1f, 0.02f + d.lean * 0.4f);
                case Head:  return V(0f, 1f, 0.05f);
                case ShoulderP: case ShoulderM: return ArmDir(s > 0 ? d.armP : d.armM, 0, s);
                case UpperArmP: case UpperArmM: return ArmDir(s > 0 ? d.armP : d.armM, 1, s);
                case LowerArmP: case LowerArmM: return ArmDir(s > 0 ? d.armP : d.armM, 2, s);
                case HandP: case HandM:         return ArmDir(s > 0 ? d.armP : d.armM, 3, s);
            }
            if (d.kneel)
            {
                switch (b)
                {
                    case UpperLegP: return V(0.04f, -0.42f, 0.91f);
                    case LowerLegP: return V(0.02f, -0.86f, -0.50f);
                    case FootP:     return V(0f, -0.06f, 1f);
                    case UpperLegM: return V(-0.05f, -1f, 0f);
                    case LowerLegM: return V(-0.06f, -0.04f, -1f);
                    case FootM:     return V(0f, -0.62f, -0.78f);
                }
            }
            if (d.stride != 0)
            {
                // one foot forward, the other back (stride > 0: the right leg is in front)
                bool front = (s > 0) == (d.stride > 0);
                switch (b)
                {
                    case UpperLegP: case UpperLegM: return front ? V(s * 0.12f, -0.90f, 0.42f) : V(s * 0.16f, -0.97f, -0.18f);
                    case LowerLegP: case LowerLegM: return front ? V(s * 0.05f, -0.99f, -0.15f) : V(s * 0.10f, -0.93f, -0.35f);
                    default:                        return front ? V(s * 0.20f, -0.10f, 0.97f) : V(s * 0.25f, -0.12f, 0.96f);
                }
            }
            switch (b)
            {
                case UpperLegP: case UpperLegM: return V(s * d.legOpen, -0.987f, 0f);
                case LowerLegP: case LowerLegM: return V(s * d.legOpen * 0.45f, -0.997f, 0f);
                default:                        return V(s * d.toeOut, -0.1f, 0.96f);        // feet
            }
        }

        // ---------------------------------------------------------------- the walk
        static Vector3 WalkDir(int b, float phi)
        {
            float s = SideOf(b);
            float ph = s > 0 ? phi : phi + Mathf.PI;
            float th = WalkAmp * Mathf.Sin(ph);                                       // leg angle, + = forward
            float swing = Mathf.Max(0f, Mathf.Cos(ph));
            float flex = 46f * Mathf.Pow(swing, 1.3f);
            float al = th - flex;
            float g = -0.9f * WalkAmp * Mathf.Sin(ph);                                 // the arm swings against the leg
            float f = 26f + 10f * Mathf.Max(0f, Mathf.Sin(ph + Mathf.PI));
            const float R = Mathf.Deg2Rad;
            switch (b)
            {
                case Spine: return V(0f, 1f, 0.03f);
                case Chest: return V(0f, 1f, 0.03f);
                case Neck:  return V(0f, 1f, 0.04f);
                case Head:  return V(0f, 1f, 0.06f);
                case ShoulderP: case ShoulderM: return V(s, 0.04f, 0f);
                case UpperArmP: case UpperArmM: return V(s * 0.12f, -Mathf.Cos(g * R), Mathf.Sin(g * R));
                case LowerArmP: case LowerArmM: return V(s * 0.09f, -Mathf.Cos((g + f) * R), Mathf.Sin((g + f) * R));
                case HandP: case HandM:         return V(s * 0.08f, -Mathf.Cos((g + f + 6f) * R), Mathf.Sin((g + f + 6f) * R));
                case UpperLegP: case UpperLegM: return V(s * 0.02f, -Mathf.Cos(th * R), Mathf.Sin(th * R));
                case LowerLegP: case LowerLegM: return V(s * 0.02f, -Mathf.Cos(al * R), Mathf.Sin(al * R));
                default:
                {
                    float dl = 6f - 20f * Mathf.Sin(ph) * (swing > 0f ? 1f : 0f);
                    return V(s * 0.04f, -Mathf.Sin(dl * R), Mathf.Cos(dl * R));
                }
            }
        }

        static float WalkDrop(float phi) { return LegLength * (1f - Mathf.Cos(WalkAmp * Mathf.Abs(Mathf.Sin(phi)) * Mathf.Deg2Rad)); }

        static float Ease(float k) { k = Mathf.Clamp01(k); return k * k * (3f - 2f * k); }

        // ---------------------------------------------------------------- apply
        public void Apply()
        {
            if (rig == null || rig.hips == null || rig.bones == null || rig.bones.Length < 18) return;
            float t = Time.unscaledTime + seed;
            float pw = Ease(poseWeight), ww = Mathf.Clamp01(walkWeight);
            var def = Def(pose);

            // hips: the walk bobs, the pose lowers (kneeling) and shifts
            float H = rig.height;
            Vector3 hp = rig.hipsBindLocalPos;
            float walkDrop = WalkDrop(phase) * ww * (1f - pw);
            float breath = 0.004f * Mathf.Sin(t * 1.7f) * pw;
            hp += Vector3.down * (H * 0.245f * def.drop * pw + walkDrop) + Vector3.up * breath + Vector3.forward * (H * def.fwd * pw);
            hp.x += 0.008f * Mathf.Sin(t * 0.6f) * pw * (def.kneel ? 0f : 1f);
            rig.hips.localPosition = hp;

            var rotRoot = new Quaternion[18];
            for (int i = 0; i < 18; i++)
            {
                var tr = rig.bones[i];
                if (tr == null) continue;
                Vector3 stand = PoseDir(Pose.Stand, i).normalized;
                Vector3 d = stand;
                if (ww > 0f) d = Vector3.Slerp(d, WalkDir(i, phase).normalized, ww);
                if (pw > 0f) d = Vector3.Slerp(d, PoseDir(pose, i).normalized, pw);

                // alive: breathing and a gentle look-around while posing
                if (i == Chest) d.y += 0.010f * Mathf.Sin(t * 1.7f) * pw;
                if (i == Head) { d.x += 0.035f * Mathf.Sin(t * 0.55f + 1f) * pw; d.z += 0.02f * Mathf.Sin(t * 0.9f) * pw; }

                lastDir[i] = d;
                if (overrideDir != null && overrideDir[i].HasValue) d = overrideDir[i].Value;

                Quaternion r = Quaternion.FromToRotation(rig.bindDir[i].normalized, d.normalized);
                int p = parent[i];
                Quaternion pr = p >= 0 ? rotRoot[p] : Quaternion.identity;
                tr.localRotation = Quaternion.Inverse(pr) * r;
                rotRoot[i] = r;
            }
        }

        /// <summary>(Editor tools) Two-bone arm IK: puts the hand's grip point on a world position. The elbow bends towards poleWorld (a direction).</summary>
        public void SolveArm(bool right, Vector3 gripWorld, Vector3 poleWorld, float handHalf = 0.07f, float weight = 1f)
        {
            int up = right ? UpperArmP : UpperArmM, lo = right ? LowerArmP : LowerArmM, hand = right ? HandP : HandM;
            Vector3 sh = rig.bones[up].position;
            float l1 = Vector3.Distance(rig.bones[up].position, rig.bones[lo].position);
            float l2 = Vector3.Distance(rig.bones[lo].position, rig.bones[hand].position);
            Vector3 guess = (gripWorld - sh).normalized;
            Vector3 wrist = gripWorld - guess * handHalf;
            Vector3 to = wrist - sh;
            float dist = Mathf.Clamp(to.magnitude, 0.12f, l1 + l2 - 0.004f);
            Vector3 a = to.normalized;
            float x = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - x * x));
            Vector3 pp = poleWorld - a * Vector3.Dot(poleWorld, a);
            if (pp.sqrMagnitude < 1e-6f) pp = Vector3.down;
            pp.Normalize();
            Vector3 elbow = sh + a * x + pp * h;
            Vector3 wristPos = sh + a * dist;
            Vector3 ud = elbow - sh, ld = wristPos - elbow, hd = gripWorld - wristPos;
            if (hd.sqrMagnitude < 1e-6f) hd = ld;
            Vector3 u1 = transform.InverseTransformDirection(ud.normalized);
            Vector3 l1d = transform.InverseTransformDirection(ld.normalized);
            Vector3 h1 = transform.InverseTransformDirection(Vector3.Slerp(ld.normalized, hd.normalized, 0.6f));
            weight = Mathf.Clamp01(weight);
            if (weight < 0.999f)
            {
                // blend with the animated pose (the walk, for instance)
                if (lastDir[up].HasValue) u1 = Vector3.Slerp(lastDir[up].Value.normalized, u1, weight);
                if (lastDir[lo].HasValue) l1d = Vector3.Slerp(lastDir[lo].Value.normalized, l1d, weight);
                if (lastDir[hand].HasValue) h1 = Vector3.Slerp(lastDir[hand].Value.normalized, h1, weight);
            }
            overrideDir[up] = u1; overrideDir[lo] = l1d; overrideDir[hand] = h1;
        }

        public void ClearOverrides() { for (int i = 0; i < overrideDir.Length; i++) overrideDir[i] = null; }
    }
}
