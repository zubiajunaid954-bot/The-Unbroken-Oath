using UnityEngine;
using UnityEngine.SceneManagement;

namespace GAMECON
{
    /// <summary>
    /// Smoothness settings applied automatically when the game starts (no scene setup needed):
    /// VSync on, a sensible shadow distance, and lighter terrain detail (grass / trees) so the frame rate stays steady.
    /// </summary>
    public static class GamePerformance
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            QualitySettings.vSyncCount = 1;                 // locks to the monitor's refresh rate: no tearing / uneven frame times
            Application.targetFrameRate = -1;
            QualitySettings.shadowDistance = Mathf.Min(QualitySettings.shadowDistance, 55f);
            QualitySettings.shadowCascades = Mathf.Min(QualitySettings.shadowCascades, 2);
            QualitySettings.asyncUploadTimeSlice = 4;
            QualitySettings.asyncUploadBufferSize = 32;
            Time.maximumDeltaTime = 0.05f;                  // a long frame never makes the player jump

            SceneManager.sceneLoaded += (s, m) => TuneTerrain();
            TuneTerrain();
        }

        static void TuneTerrain()
        {
            foreach (var t in Terrain.activeTerrains)
            {
                if (t == null) continue;
                t.detailObjectDistance = Mathf.Min(t.detailObjectDistance, 70f);
                t.detailObjectDensity = Mathf.Min(t.detailObjectDensity, 0.75f);
                t.treeBillboardDistance = Mathf.Min(t.treeBillboardDistance, 140f);
                t.treeMaximumFullLODCount = Mathf.Min(t.treeMaximumFullLODCount, 60);
                t.basemapDistance = Mathf.Min(t.basemapDistance, 120f);
            }
        }
    }
}
