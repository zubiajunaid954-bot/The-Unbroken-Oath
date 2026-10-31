using System.Collections;
using System.Linq;
using UnityEngine;
using scgFullBodyController;
using GAMECON.FX;

namespace GAMECON.Combat
{
    /// <summary>Lives on the explosion prefab: sound, light flash, shock push on the dead and a scorch mark.</summary>
    public class ExplosionFX : MonoBehaviour
    {
        public AudioClip sound;
        public Light flash;
        public float pushForce = 26f;
        public float pushRadius = 13f;
        public Material scorchMaterial;
        public Texture2D[] scorchTextures;

        float t;

        void Start()
        {
            // the sound plays on its own object, so it is not cut off when the effect is removed
            if (sound != null) AudioSource.PlayClipAtPoint(sound, transform.position, 1f);
            StartCoroutine(Aftermath());
            Destroy(gameObject, 5.0f);                      // fire, smoke, embers and light are all gone after 5 seconds
        }

        IEnumerator Aftermath()
        {
            // scorch mark under the blast
            if (scorchMaterial != null && scorchTextures != null && scorchTextures.Length > 0 &&
                Physics.Raycast(transform.position + Vector3.up * 0.4f, Vector3.down, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore))
                BloodDecal.Create(scorchMaterial, scorchTextures[UnityEngine.Random.Range(0, scorchTextures.Length)], hit.point, hit.normal, 3.6f, 0.12f, 3.6f, 1.3f);       // scorch mark: gone together with the blast

            // soldiers killed by the blast become ragdolls a moment later: throw them away from the centre
            yield return new WaitForSeconds(0.12f);
            foreach (var rb in Physics.OverlapSphere(transform.position, pushRadius, ~0, QueryTriggerInteraction.Ignore)
                         .Select(c => c.attachedRigidbody).Where(r => r != null).Distinct())
            {
                if (rb.isKinematic) continue;
                if (rb.GetComponentInParent<ragdollCamera>() == null) continue;      // only ragdolls
                rb.AddExplosionForce(pushForce, transform.position, pushRadius, 0.6f, ForceMode.Impulse);
            }
        }

        void Update()
        {
            t += Time.deltaTime;
            // a bright flash for the first moments, then the embers keep a dimming orange glow until the effect ends
            if (flash != null)
                flash.intensity = t < 0.4f ? Mathf.Lerp(8f, 1.8f, t / 0.4f) : Mathf.Lerp(1.8f, 0f, Mathf.Clamp01((t - 0.4f) / 4.4f));
        }
    }
}
