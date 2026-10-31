using UnityEngine;

namespace GAMECON.Menu
{
    /// <summary>A pop-up panel (settings / controls): fades and scales in and out, and blocks the buttons behind it while open.</summary>
    public class MenuPanel : MonoBehaviour
    {
        public CanvasGroup group;
        public RectTransform window;
        public bool IsOpen { get; private set; }
        float k;

        void Awake() { Apply(0f); }

        public void Open() { IsOpen = true; gameObject.SetActive(true); }
        public void Close() { IsOpen = false; }

        static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        void Update()
        {
            k = Mathf.MoveTowards(k, IsOpen ? 1f : 0f, Time.unscaledDeltaTime / 0.28f);
            Apply(k);
            if (!IsOpen && k <= 0f) gameObject.SetActive(false);
        }

        void Apply(float v)
        {
            float e = Ease(v);
            if (group != null) { group.alpha = e; group.blocksRaycasts = IsOpen && v > 0.5f; group.interactable = IsOpen; }
            if (window != null) window.localScale = Vector3.one * Mathf.Lerp(0.93f, 1f, e);
        }

        public void SetOpenInstantly(bool open) { IsOpen = open; k = open ? 1f : 0f; Apply(k); gameObject.SetActive(open); }
    }
}
