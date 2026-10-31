using System.Collections.Generic;
using UnityEngine;

namespace GAMECON.FX
{
    /// <summary>
    /// Realistic bullet-hit blood: a directional spray of droplets + a soft mist at the impact point, and
    /// blood splatters left on the nearby floor / wall. Soldiers also leave a growing blood pool when they die.
    /// The prefabs live in Resources/FX (built by the editor menu GAMECON > Build Combat Polish).
    /// </summary>
    public static class BloodFX
    {
        static GameObject hitPrefab, poolPrefab;
        static bool loaded;

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            hitPrefab = Resources.Load<GameObject>("FX/BloodHit");
            poolPrefab = Resources.Load<GameObject>("FX/BloodPool");
        }

        /// <returns>false when the blood assets are missing (caller can fall back to the old effect)</returns>
        public static bool Hit(Vector3 point, Vector3 normal, Vector3 sprayDir, float damage)
        {
            Load();
            if (hitPrefab == null) return false;
            if (sprayDir.sqrMagnitude < 0.001f) sprayDir = -normal;
            var go = Object.Instantiate(hitPrefab, point, Quaternion.LookRotation(sprayDir.normalized, Vector3.up));
            float s = Mathf.Clamp(0.85f + damage / 80f, 0.85f, 1.5f);   // harder hits look bigger
            go.transform.localScale = Vector3.one * s;
            Object.Destroy(go, 5f);
            return true;
        }

        public static void Pool(Vector3 position)
        {
            Load();
            if (poolPrefab == null) return;
            var go = Object.Instantiate(poolPrefab, position, Quaternion.identity);
            Object.Destroy(go, 70f);
        }
    }


    /// <summary>A flat blood splat on a surface that pops in, stays a while, then fades away.</summary>
    public class BloodDecal : MonoBehaviour
    {
        Material mat;
        float born, growTime, life, fade = 8f, targetSize;
        float startSize;
        static readonly int Tint = Shader.PropertyToID("_TintColor");
        Color baseColor;

        public static BloodDecal Create(Material template, Texture2D tex, Vector3 point, Vector3 normal, float size, float growSeconds, float lifeSeconds, float fadeSeconds = 8f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "BloodDecal";
            Object.Destroy(go.GetComponent<Collider>());
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var m = new Material(template);
            m.mainTexture = tex;
            mr.sharedMaterial = m;

            // quad front must face away from the surface
            go.transform.position = point + normal * 0.012f;
            go.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up) * Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            go.transform.localScale = Vector3.one * 0.05f;

            var d = go.AddComponent<BloodDecal>();
            d.fade = Mathf.Max(0.05f, fadeSeconds); d.mat = m; d.born = Time.time; d.growTime = Mathf.Max(0.05f, growSeconds); d.life = lifeSeconds; d.targetSize = size;
            d.startSize = size * 0.35f;
            d.baseColor = m.HasProperty(Tint) ? m.GetColor(Tint) : Color.white;
            BloodHitFX.AddLive(1);
            return d;
        }

        void Update()
        {
            float age = Time.time - born;
            float k = Mathf.Clamp01(age / growTime);
            float s = Mathf.Lerp(startSize, targetSize, 1f - (1f - k) * (1f - k));
            transform.localScale = new Vector3(s, s, 1f);

            if (age > life)
            {
                float f = 1f - Mathf.Clamp01((age - life) / fade);
                if (mat != null && mat.HasProperty(Tint)) { var c = baseColor; c.a *= f; mat.SetColor(Tint, c); }
                if (f <= 0f) Destroy(gameObject);
            }
        }

        void OnDestroy()
        {
            BloodHitFX.AddLive(-1);
            if (mat != null) Destroy(mat);
        }
    }

}
