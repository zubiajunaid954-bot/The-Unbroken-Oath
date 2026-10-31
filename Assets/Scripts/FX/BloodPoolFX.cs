using UnityEngine;

namespace GAMECON.FX
{
    /// <summary>Lives on the BloodPool prefab: a big, slowly spreading pool under a fallen soldier.</summary>
    public class BloodPoolFX : MonoBehaviour
    {
        public Texture2D[] poolTextures;
        public Material decalMaterial;
        public float size = 1.5f;

        void Start()
        {
            if (decalMaterial == null || poolTextures == null || poolTextures.Length == 0) return;
            if (Physics.Raycast(transform.position + Vector3.up * 0.6f, Vector3.down, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore))
                BloodDecal.Create(decalMaterial, poolTextures[Random.Range(0, poolTextures.Length)], hit.point, hit.normal,
                                  size * Random.Range(0.85f, 1.25f), 7f, 55f);
        }
    }
}
