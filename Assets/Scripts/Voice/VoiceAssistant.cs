using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GAMECON.Voice
{
    public enum VoiceLine { Reloading, Unlock, Rescue, Freed, AllRescued, Health, LowHealth, EnemySpotted, EnemyDown, MissionStart, MissionFailed, GrenadeThrow, GrenadeEmpty, NightFalls }

    /// <summary>
    /// The tactical AI voice (a calm female radio voice, like the callouts in PUBG): "Reloading", "Unlocking door",
    /// "Rescuing teammate", "Enemy spotted"... Clips live in Resources/Audio/Voice/&lt;Name&gt;_&lt;n&gt;.wav; the matching
    /// subtitle is shown on screen. Lines have a cooldown so the voice never spams.
    /// </summary>
    public static class VoiceAssistant
    {
        // name used in the file names, variants' subtitles, cooldown (s), may interrupt the current line
        class Def { public string name; public string[] text; public float cooldown; public bool interrupt; public string speaker = "Operator"; public string folder = "Voice"; }

        static readonly Dictionary<VoiceLine, Def> defs = new Dictionary<VoiceLine, Def>
        {
            { VoiceLine.Reloading, new Def { name = "Reloading", text = new[] { "Reloading.", "Changing magazine.", "Reloading, cover me." }, cooldown = 3f, speaker = "Samrah", folder = "VoiceSamrah" } },
            { VoiceLine.Unlock, new Def { name = "Unlock", text = new[] { "Unlocking the door.", "Door's open.", "Opening the door." }, cooldown = 2.5f, speaker = "Samrah", folder = "VoiceSamrah" } },
            { VoiceLine.Rescue, new Def { name = "Rescue", text = new[] { "Hold on, I'm cutting the ropes.", "I've got you. Cutting the ropes." }, cooldown = 1f, interrupt = true, speaker = "Samrah", folder = "VoiceSamrah" } },
            { VoiceLine.Freed,          new Def { name = "Freed", text = new[] { "Teammate freed.", "Teammate secured.", "Good work. Teammate is safe." }, cooldown = 1f, interrupt = true } },
            { VoiceLine.AllRescued,     new Def { name = "AllRescued", text = new[] { "All teammates rescued. Mission complete." }, cooldown = 0f, interrupt = true } },
            { VoiceLine.Health, new Def { name = "Health", text = new[] { "Medical kit. That's better.", "Patched up. Let's keep moving." }, cooldown = 2f, interrupt = true, speaker = "Samrah", folder = "VoiceSamrah" } },
            { VoiceLine.LowHealth,      new Def { name = "LowHealth", text = new[] { "Warning. Health low.", "Critical. Find a medical kit." }, cooldown = 25f, interrupt = true } },
            { VoiceLine.EnemySpotted,   new Def { name = "EnemySpotted", text = new[] { "Enemy spotted!", "Contact! Enemy ahead.", "You have been seen. Take cover." }, cooldown = 12f } },
            { VoiceLine.EnemyDown,      new Def { name = "EnemyDown", text = new[] { "Enemy down.", "Target eliminated." }, cooldown = 10f } },
            { VoiceLine.MissionStart, new Def { name = "MissionStart", text = new[] { "Mission started. Find the marked room and rescue your teammates." }, cooldown = 0f, interrupt = true } },
            { VoiceLine.MissionFailed,  new Def { name = "MissionFailed", text = new[] { "Mission failed." }, cooldown = 0f, interrupt = true } },
            { VoiceLine.GrenadeThrow, new Def { name = "GrenadeThrow", text = new[] { "Grenade out!", "Frag out!", "Fire in the hole!" }, cooldown = 4f, speaker = "Samrah", folder = "VoiceSamrah" } },
            { VoiceLine.NightFalls,     new Def { name = "NightFalls", text = new[] { "Night is falling. Use your torch." }, cooldown = 0f, interrupt = true } },
            { VoiceLine.GrenadeEmpty, new Def { name = "GrenadeEmpty", text = new[] { "No grenades left." }, cooldown = 3f, interrupt = true, speaker = "Samrah", folder = "VoiceSamrah" } },
        };

        static readonly Dictionary<VoiceLine, AudioClip[]> clips = new Dictionary<VoiceLine, AudioClip[]>();
        static readonly Dictionary<VoiceLine, float> lastPlayed = new Dictionary<VoiceLine, float>();
        static VoiceHost host;

        /// <summary>Subtitle text on screen (assigned by the MissionManager).</summary>
        public static Text Subtitle;

        /// <summary>The speech-bubble box with the operator's portrait (preferred over the plain subtitle).</summary>
        public static DialogueBox Dialogue;
        public static float Volume = 1.0f;

        public static void Say(VoiceLine line)
        {
            Def d;
            if (!defs.TryGetValue(line, out d)) return;
            EnsureHost();

            float now = Time.unscaledTime;
            float last;
            if (lastPlayed.TryGetValue(line, out last) && now - last < d.cooldown) return;
            if (!d.interrupt && host.Busy) return;                     // never talk over another line (unless important)

            AudioClip[] set;
            if (!clips.TryGetValue(line, out set) || set == null || (set.Length > 0 && set[0] == null))
            {
                var list = new List<AudioClip>();
                for (int i = 0; i < d.text.Length; i++)
                {
                    var c = Resources.Load<AudioClip>("Audio/" + d.folder + "/" + d.name + "_" + i);
                    list.Add(c);
                }
                set = list.ToArray();
                clips[line] = set;
            }
            int pick = Random.Range(0, set.Length);
            if (set[pick] == null) return;

            lastPlayed[line] = now;
            host.Play(set[pick], d.text[pick], Volume, d.speaker);
        }

        static void EnsureHost()
        {
            if (host != null) return;
            var go = new GameObject("VoiceAssistant");
            Object.DontDestroyOnLoad(go);
            host = go.AddComponent<VoiceHost>();
        }

        public class VoiceHost : MonoBehaviour
        {
            AudioSource src;
            float busyUntil;
            float subtitleUntil;

            public bool Busy { get { return Time.unscaledTime < busyUntil; } }

            void Awake()
            {
                src = gameObject.AddComponent<AudioSource>();
                src.spatialBlend = 0f;      // 2D: the AI speaks inside your head
                src.playOnAwake = false;
            }

            public void Play(AudioClip clip, string text, float volume, string speaker = "Operator")
            {
                src.Stop();
                src.PlayOneShot(clip, volume);
                busyUntil = Time.unscaledTime + clip.length;
                subtitleUntil = busyUntil + 0.8f;
                if (Dialogue != null) Dialogue.Show(speaker, text, clip.length + 0.9f);
                else if (Subtitle != null) Subtitle.text = "<color=#ffc83d>" + speaker + "</color>  " + text;
            }

            void Update()
            {
                if (Subtitle != null && !string.IsNullOrEmpty(Subtitle.text) && Time.unscaledTime > subtitleUntil) Subtitle.text = "";
            }
        }
    }
}
