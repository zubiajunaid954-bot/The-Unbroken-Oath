using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using GAMECON.Mission;

namespace GAMECON.Menu
{
    /// <summary>
    /// A developer check for the standalone build: start the game with  -selftest "C:\some\folder"  and it takes screenshots of the
    /// MISSION COMPLETE / MISSION FAILED screens and a font sample, then quits. Does nothing in a normal start.
    /// </summary>
    public static class BuildSelfTest
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-selftest");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("BuildSelfTest");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>().dir = args[i + 1];
        }

        class Runner : MonoBehaviour
        {
            public string dir;

            IEnumerator Start()
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "info.txt"),
                    "screen " + Screen.width + "x" + Screen.height + "\nfont: " + (FailScreen.GetFont() != null ? FailScreen.GetFont().name : "null") +
                    "\nfontIsDynamic: " + (FailScreen.GetFont() != null && FailScreen.GetFont().dynamic) + "\n");
                yield return new WaitForSeconds(6f);                       // the main menu cut scene runs meanwhile

                yield return Shot("1_menu");

                FailScreen.Show(null, KeyCode.Return, null);
                yield return new WaitForSeconds(4f);
                yield return Shot("2_failed");
                foreach (var f in FindObjectsOfType<FailScreen>()) Destroy(f.gameObject);

                FailScreen.ShowVictory(KeyCode.Return);
                yield return new WaitForSeconds(4f);
                yield return Shot("3_complete");
                foreach (var f in FindObjectsOfType<FailScreen>()) Destroy(f.gameObject);

                // a font sample (the same sizes as the victory cut scene)
                var cgo = new GameObject("Sample", typeof(Canvas), typeof(CanvasScaler));
                var canvas = cgo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 900;
                var sc = cgo.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;           // the whole 1920x1080 layout always fits, on any screen shape
                var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image)); bg.transform.SetParent(cgo.transform, false);
                bg.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 1f); var brt = bg.GetComponent<RectTransform>(); brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
                string[] lines = { "WE ARE BACK AGAIN  TOGETHER, STRONGER AND BETTER!", "MISSION COMPLETE  The squad is reunited.", "OPERATION SUN TEMPLE   PRESS [ENTER] TO PLAY AGAIN" };
                Font[] fonts = { FailScreen.GetFont(), FailScreen.GetSerifFont(), Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") };
                for (int k = 0; k < 9; k++)
                {
                    var t = new GameObject("T" + k, typeof(RectTransform), typeof(Text)); t.transform.SetParent(cgo.transform, false);
                    var tx = t.GetComponent<Text>(); tx.font = fonts[k / 3]; tx.text = lines[k % 3]; tx.fontSize = k % 3 == 1 ? 80 : 44; tx.fontStyle = k % 2 == 0 ? FontStyle.Bold : FontStyle.Normal; tx.color = Color.white;
                    tx.horizontalOverflow = HorizontalWrapMode.Overflow; tx.verticalOverflow = VerticalWrapMode.Overflow; tx.alignment = TextAnchor.UpperLeft;
                    var rt = t.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f); rt.anchoredPosition = new Vector2(40f, -30f - k * 112f); rt.sizeDelta = new Vector2(1800f, 100f);
                }
                yield return new WaitForSeconds(1f);
                yield return Shot("4_fontsample");
                Destroy(cgo);

                // the same two screens inside the real game scene
                var args = System.Environment.GetCommandLineArgs();
                int si = System.Array.IndexOf(args, "-selftestscene");
                if (si >= 0 && si + 1 < args.Length)
                {
                    UnityEngine.SceneManagement.SceneManager.LoadScene(args[si + 1]);
                    yield return new WaitForSeconds(10f);
                    FailScreen.Show(null, KeyCode.Return, "Samrah has fallen. The mission cannot continue.");
                    yield return new WaitForSeconds(4f);
                    yield return Shot("5_game_failed");
                    foreach (var f in FindObjectsOfType<FailScreen>()) Destroy(f.gameObject);
                    FailScreen.ShowVictory(KeyCode.Return);
                    yield return new WaitForSeconds(4f);
                    yield return Shot("6_game_complete");
                }
                Application.Quit();
            }

            IEnumerator Shot(string name)
            {
                yield return new WaitForEndOfFrame();
                try
                {
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                    Destroy(tex);
                }
                catch (System.Exception e) { File.AppendAllText(Path.Combine(dir, "info.txt"), name + ": " + e + "\n"); }
                yield return new WaitForSeconds(0.5f);
            }
        }
    }
}
