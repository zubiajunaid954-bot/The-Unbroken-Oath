using UnityEngine;

namespace GAMECON.Mission
{
    /// <summary>Keeps a world-space marker turned toward the player camera and gently bobbing.</summary>
    public class FaceCamera : MonoBehaviour
    {
        public float bobHeight = 0.12f;
        public float bobSpeed = 2f;
        Vector3 basePos;

        void Start() { basePos = transform.localPosition; }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;
            transform.localPosition = basePos + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobHeight);
        }
    }
}
