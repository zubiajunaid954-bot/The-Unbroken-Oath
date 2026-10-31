using UnityEngine;

namespace GAMECON.FX
{
    /// <summary>Lives on the BloodHit prefab: leaves splatters on nearby surfaces.</summary>
    public class BloodHitFX : MonoBehaviour
    {
        public Texture2D[] splatTextures;
        public Material decalMaterial;
        public int floorDecals = 2;
        public float reach = 3.2f;

        static int liveDecals;

        void Start()
        {
            if (decalMaterial == null || splatTextures == null || splatTextures.Length == 0) return;
            // floor
            for (int i = 0; i < floorDecals; i++)
            {
                Vector3 o = transform.position + Random.insideUnitSphere * 0.25f; o.y = transform.position.y + 0.1f;
                Vector3 dir = (Vector3.down + new Vector3(Random.Range(-0.35f, 0.35f), 0, Random.Range(-0.35f, 0.35f))).normalized;
                Place(o, dir, Random.Range(0.35f, 0.8f));
            }
            // wall / object behind the target, in the direction of the spray
            Place(transform.position, (transform.forward + Random.insideUnitSphere * 0.12f).normalized, Random.Range(0.3f, 0.6f));
        }

        void Place(Vector3 origin, Vector3 dir, float size)
        {
            if (liveDecals > 140) return;
            if (!Physics.Raycast(origin, dir, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Ignore)) return;
            if (hit.collider.GetComponentInParent<scgFullBodyController.HealthController>() != null) return;   // not on characters
            BloodDecal.Create(decalMaterial, splatTextures[Random.Range(0, splatTextures.Length)], hit.point, hit.normal, size, 0.35f, 28f);
        }

        public static void AddLive(int d) { liveDecals += d; }
    }
}
