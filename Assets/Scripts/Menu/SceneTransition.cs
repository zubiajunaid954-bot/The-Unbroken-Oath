using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GAMECON.Menu
{
    /// <summary>Fades to black, loads a scene and fades back in. Lives across scene loads.</summary>
    public class SceneTransition : MonoBehaviour
    {
        static SceneTransition instance;
        Image fade;
        bool busy;

        static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("SceneTransition", typeof(Canvas), typeof(CanvasScaler));
            DontDestroyOnLoad(go);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 5000;
            var img = new GameObject("Fade", typeof(RectTransform), typeof(Image));
            img.transform.SetParent(go.transform, false);
            var f = img.GetComponent<Image>(); f.color = new Color(0, 0, 0, 0); f.raycastTarget = false;
            var rt = f.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            instance = go.AddComponent<SceneTransition>();
            instance.fade = f;
        }

        /// <summary>Is a scene transition running?</summary>
        public static bool Busy { get { return instance != null && instance.busy; } }

        public static void Load(string sceneName, float fadeOut = 0.8f, float fadeIn = 1.0f)
        {
            Ensure();
            if (instance.busy) return;
            instance.StartCoroutine(instance.Run(sceneName, fadeOut, fadeIn));
        }

        IEnumerator Run(string sceneName, float fadeOut, float fadeIn)
        {
            busy = true;
            fade.raycastTarget = true;                      // no clicks while it fades
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError("[SceneTransition] scene '" + sceneName + "' is not in the Build Settings (run GAMECON > Build Main Menu + Story Scene).");
                fade.raycastTarget = false; busy = false;
                yield break;
            }
            float t = 0f;
            while (t < fadeOut) { t += Time.unscaledDeltaTime; SetA(Mathf.Clamp01(t / fadeOut)); yield return null; }
            SetA(1f);
            Time.timeScale = 1f;
            var op = SceneManager.LoadSceneAsync(sceneName);
            while (!op.isDone) yield return null;
            yield return null;
            t = 0f;
            while (t < fadeIn) { t += Time.unscaledDeltaTime; SetA(1f - Mathf.Clamp01(t / fadeIn)); yield return null; }
            SetA(0f);
            fade.raycastTarget = false;
            busy = false;
        }

        void SetA(float a) { var c = fade.color; c.a = a; fade.color = c; }
    }
}
