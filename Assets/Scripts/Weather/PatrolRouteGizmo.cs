using UnityEngine;

namespace GAMECON.Weather
{
    /// <summary>Draws a patrol loop in the Scene view (child waypoints joined by lines).</summary>
    public class PatrolRouteGizmo : MonoBehaviour
    {
        void OnDrawGizmos()
        {
            int n = transform.childCount;
            if (n == 0) return;
            Gizmos.color = new Color(1f, 0.8f, 0.2f);
            for (int i = 0; i < n; i++)
            {
                Vector3 a = transform.GetChild(i).position;
                Vector3 b = transform.GetChild((i + 1) % n).position;
                Gizmos.DrawSphere(a, 0.25f);
                Gizmos.DrawLine(a + Vector3.up * 0.2f, b + Vector3.up * 0.2f);
            }
        }
    }
}
