using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using scgFullBodyController;
using GAMECON.FX;

namespace GAMECON.Combat
{
    /// <summary>
    /// A fragmentation grenade. Mechanics:
    ///   - thrown along an arc, it bounces and rolls (physics), and explodes when the fuse runs out (it can be "cooked" first)
    ///   - everything inside the LETHAL radius dies (soldiers are killed outright, unless a solid wall is in between)
    ///   - between the lethal radius and the outer blast radius the damage falls off with distance
    ///   - the thrower is hurt too when too close; surviving soldiers nearby are alerted by the noise
    ///   - ragdolls are blown away, the camera shakes and the screen flashes for anyone close
    /// </summary>
    public class Grenade : MonoBehaviour
    {
        public static event Action<Vector3, float, float> Exploded;      // position, lethal radius, blast radius

        [Header("Blast")]
        public float lethalRadius = 6.5f;       // everyone inside is killed
        public float blastRadius = 10f;         // damage falls off up to here
        public float soldierHealthGuess = 60f;  // soldiers have 60 HP
        public float playerDamageRaw = 1100f;   // before the player's own damage reduction (x0.2)
        public float alertRadius = 32f;         // surviving soldiers this close come to look

        [Header("Fuse")]
        public float fuse = 3.5f;

        [Header("Assets")]
        public GameObject explosionPrefab;
        public AudioClip bounceClip, beepClip;

        Rigidbody rb;
        Collider col;
        AudioSource src;
        bool live;                  // the fuse is running
        float explodeAt;
        float nextBeep;
        float lastBounce;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();
            src = GetComponent<AudioSource>();
            if (src == null) { src = gameObject.AddComponent<AudioSource>(); src.spatialBlend = 1f; src.maxDistance = 40f; }
        }

        /// <summary>In the thrower's hand: no physics yet.</summary>
        public void Hold()
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            if (col == null) col = GetComponent<Collider>();
            rb.isKinematic = true;
            col.enabled = false;
        }

        /// <summary>Let go: physics on, fuse runs.</summary>
        public void Launch(Vector3 velocity, float fuseSeconds)
        {
            transform.SetParent(null, true);
            col.enabled = true;
            rb.isKinematic = false;
            rb.velocity = velocity;
            rb.angularVelocity = UnityEngine.Random.onUnitSphere * 12f;
            fuse = fuseSeconds;
            live = true;
            explodeAt = Time.time + fuseSeconds;
            nextBeep = Time.time + Mathf.Max(0.05f, fuseSeconds - 1.6f);
        }

        void Update()
        {
            if (!live) return;
            float left = explodeAt - Time.time;

            // the beeping speeds up as the fuse burns down
            if (left < 1.6f && Time.time >= nextBeep)
            {
                if (beepClip != null) src.PlayOneShot(beepClip, 0.5f);
                nextBeep = Time.time + Mathf.Lerp(0.07f, 0.4f, Mathf.Clamp01(left / 1.6f));
            }
            if (left <= 0f) Explode();
            else if (transform.position.y < -200f) Destroy(gameObject);        // fell out of the world
        }

        void OnCollisionEnter(Collision c)
        {
            if (!live) return;
            float speed = c.relativeVelocity.magnitude;
            if (speed > 1.2f && bounceClip != null && Time.time - lastBounce > 0.08f)
            {
                src.PlayOneShot(bounceClip, Mathf.Clamp01(speed / 8f));
                lastBounce = Time.time;
            }
            // rolling friction: take the energy out of a grenade that has already bounced a few times
            rb.angularVelocity *= 0.85f;
        }

        void Explode()
        {
            live = false;
            Vector3 p = transform.position;
            if (explosionPrefab != null) Instantiate(explosionPrefab, p, Quaternion.identity);
            GrenadeBlast.Apply(p, lethalRadius, blastRadius, soldierHealthGuess, playerDamageRaw, alertRadius);
            if (Exploded != null) Exploded(p, lethalRadius, blastRadius);
            Destroy(gameObject);
        }
    }

    /// <summary>The damage model of an explosion.</summary>
    public static class GrenadeBlast
    {
        public static void Apply(Vector3 p, float lethal, float blast, float soldierHp, float playerRaw, float alertRadius)
        {
            var cols = Physics.OverlapSphere(p, blast, ~0, QueryTriggerInteraction.Ignore);
            var done = new HashSet<HealthController>();
            foreach (var c in cols)
            {
                var hc = c.GetComponentInParent<HealthController>();
                if (hc == null || !done.Add(hc)) continue;

                // distance to the body (centre of the chest), not to the feet
                Vector3 chest = hc.transform.position + Vector3.up * 1.0f;
                float d = Vector3.Distance(p, chest);

                // shrapnel is stopped by solid cover between the explosion and the body
                bool covered = false;
                if (Physics.Linecast(p + Vector3.up * 0.2f, chest, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
                    covered = hit.collider.GetComponentInParent<HealthController>() == null;

                if (hc.isAiOrDummy)
                {
                    if (d <= lethal && (!covered || d <= 2.2f)) hc.Damage(100000f);               // dead
                    else if (d <= blast)
                    {
                        float f = 1f - Mathf.Clamp01((d - lethal) / Mathf.Max(0.1f, blast - lethal));
                        float dmg = soldierHp * 1.5f * f * (covered ? 0.35f : 1f);                     // inside the falloff zone: often lethal, never certain
                        if (dmg > 0.5f) hc.Damage(dmg);
                    }
                }
                else    // the player (self damage)
                {
                    if (d <= blast)
                    {
                        float f = 1f - Mathf.Clamp01(d / blast);
                        hc.Damage(playerRaw * f * f * (covered ? 0.3f : 1f));
                    }
                }
            }

            // the noise alerts the soldiers that survived
            foreach (var ai in UnityEngine.Object.FindObjectsOfType<AiController>())
            {
                if (ai == null || !ai.enabled) continue;
                if (Vector3.Distance(ai.transform.position, p) <= alertRadius) ai.overrideAttack = true;
            }
        }
    }

}
