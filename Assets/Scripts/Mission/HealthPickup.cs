using UnityEngine;
using scgFullBodyController;
using GAMECON.Voice;

namespace GAMECON.Mission
{
    /// <summary>A medical kit: walk over it to restore life (only when you are hurt). Shown as a green + on the map.</summary>
    public class HealthPickup : MonoBehaviour
    {
        public float healAmount = 150f;
        public float radius = 1.7f;
        public Transform visual;                 // spins and bobs
        public AudioClip pickupSound;

        float baseY;
        bool taken;

        void Start() { if (visual != null) baseY = visual.localPosition.y; }

        void Update()
        {
            if (taken) return;

            if (visual != null)
            {
                visual.Rotate(0f, 90f * Time.deltaTime, 0f, Space.World);
                var p = visual.localPosition; p.y = baseY + Mathf.Sin(Time.time * 2.2f) * 0.1f; visual.localPosition = p;
            }

            var m = MissionManager.Instance;
            if (m == null || m.Player == null) return;
            if (Vector3.Distance(m.Player.position, transform.position) > radius) return;

            var hc = m.Player.GetComponent<HealthController>();
            if (hc == null) return;
            float max = Mathf.Max(1f, hc.MaxHealth);
            if (hc.health >= max - 1f) { m.RequestPrompt("Health is full"); return; }     // keep it for later

            float before = hc.health;
            hc.health = Mathf.Min(max, hc.health + healAmount);
            taken = true;
            if (pickupSound != null) AudioSource.PlayClipAtPoint(pickupSound, transform.position, 0.9f);
            VoiceAssistant.Say(VoiceLine.Health);
            m.ShowBanner("+" + Mathf.RoundToInt(hc.health - before) + " health", 2f);
            Destroy(gameObject);
        }
    }
}
