using UnityEngine;

namespace GAMECON.Menu
{
    /// <summary>Volume settings saved between sessions (PlayerPrefs). The master volume is applied to the whole game.</summary>
    public static class GameSettings
    {
        const string MasterKey = "gc_master_volume", MusicKey = "gc_music_volume";

        public static float Master
        {
            get { return Mathf.Clamp01(PlayerPrefs.GetFloat(MasterKey, 1f)); }
            set { PlayerPrefs.SetFloat(MasterKey, Mathf.Clamp01(value)); Apply(); }
        }

        public static float Music
        {
            get { return Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, 0.85f)); }
            set { PlayerPrefs.SetFloat(MusicKey, Mathf.Clamp01(value)); }
        }

        public static void Apply() { AudioListener.volume = Master; }

        public static void Save() { PlayerPrefs.Save(); }
    }
}
