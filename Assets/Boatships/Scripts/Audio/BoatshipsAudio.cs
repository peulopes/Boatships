using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Boatships
{
    public enum Sfx { CannonFire, HullHit, Splash, Pistol, Swing, SwordHit, Clang, CannonLoad, Hammer, Bail, Bell, Click }

    /// <summary>
    /// All of the game's sound, synthesised at start-up (no audio files): one-shot effects placed in the world,
    /// ambience that follows what the player is doing (open sea, wind with the ship's speed, deck creaks, the
    /// bustle of a harbour town) and two original music loops, a sailing tune and a battle theme, crossfaded
    /// by the game mode. Volumes follow the menu's Audio Volume and Music Volume settings.
    /// </summary>
    public sealed class BoatshipsAudio : MonoBehaviour
    {
        private const int Rate = 22050;
        private const string MusicKey = "Boatships.MusicVolume";
        private const int Voices = 24;

        private static BoatshipsAudio instance;
        private static float master = 0.6f;
        private static float music = -1f;

        /// <summary>Overall volume (the menu's Audio Volume).</summary>
        public static float MasterVolume
        {
            get => master;
            set => master = Mathf.Clamp01(value);
        }

        /// <summary>Music volume, saved between sessions.</summary>
        public static float MusicVolume
        {
            get
            {
                if (music < 0f) music = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, 0.5f));
                return music;
            }
            set
            {
                music = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(MusicKey, music);
                PlayerPrefs.Save();
            }
        }

        private readonly Dictionary<Sfx, AudioClip[]> clips = new Dictionary<Sfx, AudioClip[]>();
        private readonly Dictionary<Sfx, float> lastPlayed = new Dictionary<Sfx, float>();
        private readonly List<AudioClip> owned = new List<AudioClip>();
        private AudioSource[] voices;
        private int nextVoice;
        private AudioSource sea, wind, creak, town, calmMusic, battleMusic;
        private Task<float[][]> musicTask; // ambience and music loops
        private float battleWeight;
        private BoatshipsTravelController travel;
        private BoatshipsShipBattle shipBattle;
        private ShipArcadeController flagship;
        private float lookupAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; music = -1f; quitting = false; Application.quitting += () => quitting = true; }
        private static bool quitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => Ensure();

        private static BoatshipsAudio Ensure()
        {
            if (instance != null) return instance;
            if (!Application.isPlaying || quitting) return null;
            var host = new GameObject("Boatships Audio");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<BoatshipsAudio>();
            return instance;
        }

        // ───────────────────────────── Playing ─────────────────────────────

        /// <summary>Plays an effect at a point in the world.</summary>
        public static void Play(Sfx sound, Vector3 position, float volume = 1f) => Ensure()?.Emit(sound, position, volume, true);

        /// <summary>Plays an effect with no position (the player's own pistol, the menu).</summary>
        public static void Play2D(Sfx sound, float volume = 1f) => Ensure()?.Emit(sound, Vector3.zero, volume, false);

        private void Emit(Sfx sound, Vector3 position, float volume, bool spatial)
        {
            if (voices == null || !clips.TryGetValue(sound, out AudioClip[] variants) || variants.Length == 0) return;
            // A broadside fires many guns in the same frame: let a few through, not dozens on top of each other.
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(sound, out float last) && now - last < MinGap(sound)) volume *= 0.35f;
            lastPlayed[sound] = now;
            AudioSource voice = null;
            for (int i = 0; i < voices.Length && voice == null; i++)
            {
                AudioSource candidate = voices[(nextVoice + i) % voices.Length];
                if (!candidate.isPlaying) voice = candidate;
            }
            if (voice == null) voice = voices[nextVoice];
            nextVoice = (Array.IndexOf(voices, voice) + 1) % voices.Length;
            voice.transform.position = position;
            voice.spatialBlend = spatial ? 0.85f : 0f;
            voice.minDistance = Reach(sound);
            voice.maxDistance = voice.minDistance * 30f;
            voice.pitch = UnityEngine.Random.Range(0.92f, 1.08f);
            voice.clip = variants[UnityEngine.Random.Range(0, variants.Length)];
            voice.volume = Mathf.Clamp01(volume * Loudness(sound)) * master;
            voice.Play();
        }

        private static float MinGap(Sfx sound) => sound == Sfx.CannonFire || sound == Sfx.HullHit || sound == Sfx.Splash ? 0.06f : 0.02f;

        /// <summary>Distance within which an effect plays at full volume (cannon fire carries; a sword stroke doesn't).</summary>
        private static float Reach(Sfx sound)
        {
            switch (sound)
            {
                case Sfx.CannonFire: return 45f;
                case Sfx.HullHit: case Sfx.Splash: case Sfx.Bell: return 30f;
                case Sfx.Pistol: return 20f;
                default: return 6f;
            }
        }

        private static float Loudness(Sfx sound)
        {
            switch (sound)
            {
                case Sfx.CannonFire: return 0.9f;
                case Sfx.HullHit: return 0.8f;
                case Sfx.Splash: return 0.55f;
                case Sfx.Pistol: return 0.7f;
                case Sfx.Swing: return 0.35f;
                case Sfx.SwordHit: return 0.6f;
                case Sfx.Clang: return 0.5f;
                case Sfx.Bell: return 0.5f;
                case Sfx.Click: return 0.3f;
                default: return 0.55f;
            }
        }

        // ───────────────────────────── Set-up ─────────────────────────────

        private void Awake()
        {
            master = Mathf.Clamp01(PlayerPrefs.GetFloat("Boatships.Volume", 0.6f));
            voices = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var voice = new GameObject("Voice " + i).AddComponent<AudioSource>();
                voice.transform.SetParent(transform, false);
                voice.playOnAwake = false;
                voice.rolloffMode = AudioRolloffMode.Logarithmic;
                voice.dopplerLevel = 0f;
                voices[i] = voice;
            }
            Register(Sfx.CannonFire, 3, Synth.CannonFire);
            Register(Sfx.HullHit, 3, Synth.HullHit);
            Register(Sfx.Splash, 2, Synth.Splash);
            Register(Sfx.Pistol, 2, Synth.Pistol);
            Register(Sfx.Swing, 3, Synth.Swing);
            Register(Sfx.SwordHit, 3, Synth.SwordHit);
            Register(Sfx.Clang, 3, Synth.Clang);
            Register(Sfx.CannonLoad, 2, Synth.CannonLoad);
            Register(Sfx.Hammer, 2, Synth.Hammer);
            Register(Sfx.Bail, 2, Synth.Bail);
            Register(Sfx.Bell, 1, Synth.Bell);
            Register(Sfx.Click, 1, Synth.Click);
            // The long loops (ambience and music) take a moment to render: do it off the main thread.
            musicTask = Task.Run(() => new[] { Synth.Sea(1), Synth.Wind(2), Synth.Creaks(3), Synth.Town(4), Music.Calm(), Music.Battle() });
        }

        private void Register(Sfx sound, int variants, Func<int, float[]> make)
        {
            var list = new AudioClip[variants];
            for (int i = 0; i < variants; i++) list[i] = Clip(sound + " " + i, FadeOut(make(i * 7919 + (int)sound * 104729 + 17)));
            clips[sound] = list;
        }

        /// <summary>Tapers the last few milliseconds so an effect never stops mid-wave (which clicks).</summary>
        private static float[] FadeOut(float[] data)
        {
            int fade = Mathf.Min(data.Length / 4, Rate / 50);
            for (int i = 0; i < fade; i++) data[data.Length - 1 - i] *= i / (float)fade;
            return data;
        }

        private AudioClip Clip(string name, float[] data)
        {
            AudioClip clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            owned.Add(clip);
            return clip;
        }

        private AudioSource Loop(string name, float[] data)
        {
            var source = new GameObject(name).AddComponent<AudioSource>();
            source.transform.SetParent(transform, false);
            source.clip = Clip(name, data);
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.playOnAwake = false;
            // Start each loop somewhere different so they never line up.
            source.timeSamples = UnityEngine.Random.Range(0, data.Length);
            source.Play();
            return source;
        }

        // ───────────────────────────── Mixing ─────────────────────────────

        private void Update()
        {
            if (musicTask != null && musicTask.IsCompleted)
            {
                if (musicTask.Status == TaskStatus.RanToCompletion)
                {
                    float[][] loops = musicTask.Result;
                    sea = Loop("Open sea", loops[0]);
                    wind = Loop("Wind", loops[1]);
                    creak = Loop("Deck creaks", loops[2]);
                    town = Loop("Harbour town", loops[3]);
                    calmMusic = Loop("Music — Sailing", loops[4]);
                    battleMusic = Loop("Music — Battle", loops[5]);
                    calmMusic.timeSamples = battleMusic.timeSamples = 0;
                }
                else Debug.LogWarning("Boatships audio: ambience and music could not be rendered. " + musicTask.Exception);
                musicTask = null;
            }
            if (Time.unscaledTime >= lookupAt)
            {
                lookupAt = Time.unscaledTime + 2f;
                if (travel == null) travel = FindAnyObjectByType<BoatshipsTravelController>();
                if (shipBattle == null) shipBattle = FindAnyObjectByType<BoatshipsShipBattle>();
                if (flagship == null) flagship = FindAnyObjectByType<ShipArcadeController>();
            }
            TravelMode mode = travel != null ? travel.Mode : TravelMode.Battle;
            bool fighting = (shipBattle != null && shipBattle.Active) ||
                (travel != null && (mode == TravelMode.Battle || mode == TravelMode.CrewBattle) && BoatshipsDeckFighter.All.Count > 0) ||
                HostilesAshore();
            bool ashore = travel != null && (mode == TravelMode.Town || mode == TravelMode.Port);
            bool onDeck = BoatshipsFirstPerson.OnFoot && !ashore;
            float speed = flagship != null ? Mathf.Abs(flagship.CurrentSpeed) : 0f;
            float dt = Time.unscaledDeltaTime;

            Fade(sea, ashore ? 0.18f : 0.32f, dt, 0.5f);
            Fade(wind, ashore ? 0.03f : 0.05f + Mathf.Clamp01(speed / 9f) * 0.18f, dt, 0.5f);
            Fade(creak, onDeck ? 0.32f : speed > 0.5f && !ashore ? 0.1f : 0f, dt, 0.8f);
            Fade(town, mode == TravelMode.Town ? 0.4f : mode == TravelMode.Port ? 0.2f : 0f, dt, 0.6f);

            battleWeight = Mathf.MoveTowards(battleWeight, fighting ? 1f : 0f, dt / 2.5f);
            float musicLevel = MusicVolume * 0.45f;
            if (calmMusic != null) calmMusic.volume = musicLevel * (1f - battleWeight) * master;
            if (battleMusic != null) battleMusic.volume = musicLevel * battleWeight * master;
            // Begin the battle theme from its first bar whenever a fight starts.
            if (battleMusic != null && fighting && battleWeight < 0.02f) battleMusic.timeSamples = 0;
        }

        private static void Fade(AudioSource source, float target, float dt, float seconds)
        {
            if (source == null) return;
            source.volume = Mathf.MoveTowards(source.volume, target * master, dt / seconds * Mathf.Max(0.05f, master));
        }

        /// <summary>A town assault: enemy soldiers on their feet in the streets.</summary>
        private static bool HostilesAshore()
        {
            foreach (BoatshipsGroundFighter fighter in BoatshipsGroundFighter.All)
                if (fighter != null && fighter.Team == 1 && fighter.Alive) return true;
            return false;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            foreach (AudioClip clip in owned) if (clip != null) Destroy(clip);
            owned.Clear();
        }

        // ───────────────────────────── Synthesis ─────────────────────────────

        /// <summary>Small DSP toolkit: noise, one-pole filters, envelopes. Pure maths, safe off the main thread.</summary>
        private sealed class Dsp
        {
            private uint state;
            public Dsp(int seed) { state = (uint)seed * 2654435761u + 1u; }
            public float Noise()
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                return (state & 0xFFFFFF) / 8388608f - 1f;
            }
            public float Range(float a, float b) => a + (Noise() * 0.5f + 0.5f) * (b - a);
            public static float Coef(float cutoff) => 1f - Mathf.Exp(-2f * Mathf.PI * Mathf.Max(1f, cutoff) / Rate);
        }

        private struct Lowpass
        {
            public float y;
            public float Run(float x, float cutoff) { y += Dsp.Coef(cutoff) * (x - y); return y; }
        }

        /// <summary>Band-pass from two one-pole low-passes: keeps what is above <c>low</c> and below <c>high</c>.</summary>
        private struct Band
        {
            private Lowpass a, b;
            public float Run(float x, float low, float high) { float h = a.Run(x, high); return h - b.Run(h, low); }
        }

        private static float[] Buffer(float seconds) => new float[Mathf.Max(1, Mathf.RoundToInt(seconds * Rate))];

        private static float[] Normalize(float[] data, float peak = 0.9f)
        {
            float max = 0.0001f;
            foreach (float v in data) max = Mathf.Max(max, Mathf.Abs(v));
            float gain = peak / max;
            for (int i = 0; i < data.Length; i++) data[i] *= gain;
            return data;
        }

        /// <summary>Makes a buffer loop seamlessly: its last <paramref name="fadeSeconds"/> are crossfaded into its start.</summary>
        private static float[] Loopify(float[] data, float fadeSeconds)
        {
            int fade = Mathf.Min(data.Length / 3, Mathf.RoundToInt(fadeSeconds * Rate));
            var result = new float[data.Length - fade];
            for (int i = 0; i < result.Length; i++) result[i] = data[i];
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                result[i] = data[i] * Mathf.Sqrt(t) + data[result.Length + i] * Mathf.Sqrt(1f - t);
            }
            return result;
        }

        private static class Synth
        {
            public static float[] CannonFire(int seed)
            {
                var d = new Dsp(seed);
                float[] o = Buffer(1.8f);
                Lowpass blast = default, rumble = default, rumble2 = default;
                float phase = 0f, f0 = d.Range(70f, 95f);
                for (int i = 0; i < o.Length; i++)
                {
                    float t = i / (float)Rate;
                    float n = d.Noise();
                    phase += 2f * Mathf.PI * Mathf.Lerp(f0, 38f, Mathf.Clamp01(t * 3f)) / Rate;
                    float boom = Mathf.Sin(phase) * Mathf.Exp(-t * 4f);
                    float attack = Mathf.Clamp01(t / 0.003f);
                    float body = blast.Run(n, Mathf.Lerp(3200f, 180f, Mathf.Clamp01(t * 2.2f))) * Mathf.Exp(-t * 5.5f) * 2.2f;
                    float crack = t < 0.03f ? n * Mathf.Exp(-t * 180f) : 0f;
                    float tail = rumble2.Run(rumble.Run(n, 260f), 120f) * Mathf.Exp(-t * 1.7f) * 6f;
                    o[i] = (boom * 0.9f + body + crack * 0.7f + tail) * attack;
                }
                return Normalize(o);
            }

            public static float[] HullHit(int seed)
            {
                var d = new Dsp(seed);
                float[] o = Buffer(0.8f);
                Lowpass body = default;
                var splinter = new Band();
                float phase = 0f;
                // Splinters: a spray of short cracks in the first third of a second.
                var cracks = new List<(int at, float amp)>();
                for (int k = 0; k < 28; k++) cracks.Add((Mathf.RoundToInt(d.Range(0f, 0.35f) * d.Range(0.2f, 1f) * Rate), d.Range(0.3f, 1f)));
                float crackle = 0f;
                for (int i = 0; i < o.Length; i++)
                {
                    float t = i / (float)Rate;
                    float n = d.Noise();
                    phase += 2f * Mathf.PI * Mathf.Lerp(120f, 65f, Mathf.Clamp01(t * 6f)) / Rate;
                    foreach (var c in cracks) if (c.at == i) crackle = c.amp;
                    crackle *= 0.993f;
                    float thud = Mathf.Sin(phase) * Mathf.Exp(-t * 13f);
                    float wood = body.Run(n, 1400f) * Mathf.Exp(-t * 9f) * 2f;
                    float split = splinter.Run(n, 900f, 5000f) * crackle * 1.6f;
                    o[i] = thud * 0.9f + wood + split;
                }
                return Normalize(o);
            }

            public static float[] Splash(int seed)
            {
                var d = new Dsp(seed);
                float[] o = Buffer(1.3f);
                Lowpass water = default;
                float bubble = 0f, bubbleFreq = 0f, bubbleAge = 1f;
                for (int i = 0; i < o.Length; i++)
                {
                    float t = i / (float)Rate;
                    float cutoff = t < 0.05f ? Mathf.Lerp(600f, 3500f, t / 0.05f) : Mathf.Lerp(3500f, 500f, Mathf.Clamp01((t - 0.05f) * 1.4f));
                    float env = Mathf.Clamp01(t / 0.015f) * Mathf.Exp(-t * 3f);
                    float s = water.Run(d.Noise(), cutoff) * env * 2.5f;
                    // Bubbles rising after the plunge.
                    if (t > 0.15f && bubbleAge > 0.05f && d.Noise() > 0.9993f) { bubbleFreq = d.Range(450f, 1100f); bubbleAge = 0f; }
                    if (bubbleAge < 0.05f)
                    {
                        bubbleAge += 1f / Rate;
                        bubble += 2f * Mathf.PI * bubbleFreq * (1f + bubbleAge * 8f) / Rate;
                        s += Mathf.Sin(bubble) * Mathf.Exp(-bubbleAge * 60f) * 0.25f * Mathf.Exp(-t * 2f);
                    }
                    o[i] = s;
                }
                return Normalize(o, 0.8f);
            }

            public static float[] Pistol(int seed)
            {
                var d = new Dsp(seed);
                float[] o = Buffer(0.7f);
                Lowpass smoke = default;
                float phase = 0f;
                for (int i = 0; i < o.Length; i++)
                {
                    float t = i / (float)Rate;
                    float n = d.Noise();
                    phase += 2f * Mathf.PI * Mathf.Lerp(200f, 90f, Mathf.Clamp01(t * 10f)) / Rate;
                    float crack = n * Mathf.Exp(-t * 85f);
                    float pop = Mathf.Sin(phase) * Mathf.Exp(-t * 16f) * 0.6f;
                    float tail = smoke.Run(n, 1300f) * Mathf.Exp(-t * 7f) * 1.4f;
                    o[i] = crack + pop + tail;
                }
                return Normalize(o);
            }

            public static float[] Swing(int seed)
            {
                var d = new Dsp(seed);
                float len = d.Range(0.22f, 0.3f);
                float[] o = Buffer(len);
                var air = new Band();
                for (int i = 0; i < o.Length; i++)
                {
                    float u = i / (float)o.Length;
                    float centre = Mathf.Lerp(500f, 1800f, Mathf.Sin(u * Mathf.PI));
                    float env = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 2f);
                    o[i] = air.Run(d.Noise(), centre * 0.6f, centre * 1.5f) * env;
                }
                return Normalize(o, 0.7f);
            }

            public static float[] SwordHit(int seed)
            {
                var d = new Dsp(seed);
                float[] o = Buffer(0.3f);
                Lowpass cloth = default;
                float phase = 0f;
                for (int i = 0; i < o.Length; i++)
                {
                    float t = i / (float)Rate;
                    phase += 2f * Mathf.PI * 95f / Rate;
                    o[i] = cloth.Run(d.Noise(), 700f) * Mathf.Exp(-t * 28f) * 2.5f + Mathf.Sin(phase) * Mathf.Exp(-t * 24f) * 0.7f;
                }
                return Normalize(o, 0.85f);
            }

            /// <summary>Steel on steel: a few inharmonic partials, like a struck blade.</summary>
            public static float[] Clang(int seed)
            {
                var d = new Dsp(seed);
                float[] o = Buffer(1f);
                float f0 = d.Range(780f, 1020f);
                float[] ratios = { 1f, 2.76f, 5.4f, 8.93f };
                float[] amps = { 1f, 0.6f, 0.35f, 0.2f };
                float[] decays = { 4f, 6f, 9f, 13f };
                var phases = new float[4];
                for (int i = 0; i < o.Length; i++)
                {
                    float t = i / (float)Rate;
                    float s = t < 0.004f ? d.Noise() * 0.8f : 0f;
                    for (int p = 0; p < 4; p++)
                    {
                        phases[p] += 2f * Mathf.PI * f0 * ratios[p] / Rate;
                        s += Mathf.Sin(phases[p]) * amps[p] * Mathf.Exp(-t * decays[p]);
                    }
                    o[i] = s;
                }
                return Normalize(o, 0.75f);
            }

            /// <summary>Ball and wadding rammed home: an iron clunk, a scrape, a second clunk.</summary>
            public static float[] CannonLoad(int seed)
            {
                var d = new Dsp(seed);
                float[] o = Buffer(0.75f);
                var scrape = new Band();
                for (int i = 0; i < o.Length; i++)
                {
                    float t = i / (float)Rate;
                    float s = Clunk(t, 0f, 310f) + Clunk(t, 0.5f, 270f) * 0.8f;
                    if (t > 0.08f && t < 0.45f) s += scrape.Run(d.Noise(), 1500f, 4500f) * Mathf.Sin((t - 0.08f) / 0.37f * Mathf.PI) * 0.25f;
                    o[i] = s;
                }
                return Normalize(o, 0.8f);
            }

            private static float Clunk(float t, float at, float f)
            {
                float u = t - at;
                if (u < 0f) return 0f;
                float e = Mathf.Exp(-u * 26f);
                return (Mathf.Sin(2f * Mathf.PI * f * u) + Mathf.Sin(2f * Mathf.PI * f * 2.31f * u) * 0.5f +
                    Mathf.Sin(2f * Mathf.PI * f * 4.2f * u) * 0.25f) * e;
            }

            /// <summary>Three hammer blows nailing a plank.</summary>
            public static float[] Hammer(int seed)
            {
                var d = new Dsp(seed);
                float[] o = Buffer(1f);
                Lowpass knock = default;
                for (int i = 0; i < o.Length; i++)
                {
                    float t = i / (float)Rate;
                    float s = 0f;
                    for (int k = 0; k < 3; k++)
                    {
                        float u = t - k * 0.3f;
                        if (u < 0f) continue;
                        s += (Mathf.Sin(2f * Mathf.PI * 430f * u) * 0.6f + Mathf.Sin(2f * Mathf.PI * 1010f * u) * 0.3f) * Mathf.Exp(-u * 38f);
                        if (u < 0.02f) s += knock.Run(d.Noise(), 2200f) * Mathf.Exp(-u * 120f) * 2f;
                    }
                    o[i] = s;
                }
                return Normalize(o, 0.8f);
            }

            /// <summary>A bucket of water thrown over the side.</summary>
            public static float[] Bail(int seed)
            {
                var d = new Dsp(seed);
                float[] o = Buffer(0.9f);
                Lowpass water = default;
                for (int i = 0; i < o.Length; i++)
                {
                    float t = i / (float)Rate;
                    float env = Mathf.Exp(-Mathf.Pow((t - 0.3f) / 0.15f, 2f)) + Mathf.Exp(-Mathf.Pow((t - 0.55f) / 0.12f, 2f)) * 0.5f;
                    o[i] = water.Run(d.Noise(), 1400f + 800f * Mathf.Sin(t * 20f)) * env;
                }
                return Normalize(o, 0.7f);
            }

            /// <summary>The ship's bell, struck twice: all hands to quarters.</summary>
            public static float[] Bell(int seed)
            {
                float[] o = Buffer(3.2f);
                float f = 640f;
                float[] ratios = { 0.5f, 1f, 1.19f, 1.5f, 2f, 2.61f };
                float[] amps = { 0.5f, 1f, 0.6f, 0.45f, 0.35f, 0.2f };
                float[] decays = { 0.9f, 1.4f, 1.8f, 2.4f, 3f, 4.5f };
                for (int i = 0; i < o.Length; i++)
                {
                    float t = i / (float)Rate;
                    float s = 0f;
                    for (int k = 0; k < 2; k++)
                    {
                        float u = t - k * 0.42f;
                        if (u < 0f) continue;
                        for (int p = 0; p < ratios.Length; p++)
                            s += Mathf.Sin(2f * Mathf.PI * f * ratios[p] * u) * amps[p] * Mathf.Exp(-u * decays[p]);
                    }
                    o[i] = s;
                }
                return Normalize(o, 0.8f);
            }

            public static float[] Click(int seed)
            {
                float[] o = Buffer(0.05f);
                for (int i = 0; i < o.Length; i++)
                {
                    float t = i / (float)Rate;
                    o[i] = Mathf.Sin(2f * Mathf.PI * 1250f * t) * Mathf.Exp(-t * 95f);
                }
                return Normalize(o, 0.6f);
            }

            // ── Ambience loops ──

            /// <summary>Open sea: a deep wash of swell with foam hissing as each wave breaks.</summary>
            public static float[] Sea(int seed)
            {
                var d = new Dsp(seed);
                float length = 12f, fade = 2f;
                float[] o = Buffer(length + fade);
                Lowpass deep = default, deep2 = default, foam = default;
                var hiss = new Band();
                for (int i = 0; i < o.Length; i++)
                {
                    float t = i / (float)Rate;
                    float n = d.Noise();
                    // Three overlapping swells of different lengths.
                    float swell = 0.5f + 0.25f * Mathf.Sin(t * 2f * Mathf.PI / 4f) + 0.15f * Mathf.Sin(t * 2f * Mathf.PI / 6.5f + 1f) +
                        0.1f * Mathf.Sin(t * 2f * Mathf.PI / 2.6f + 2f);
                    float wash = deep2.Run(deep.Run(n, 420f), 300f) * 5f * swell;
                    float crest = Mathf.Pow(Mathf.Clamp01(swell - 0.45f) * 2f, 3f);
                    float spray = foam.Run(hiss.Run(n, 1200f, 5000f), 4000f) * crest * 0.9f;
                    o[i] = wash + spray;
                }
                return Normalize(Loopify(o, fade), 0.8f);
            }

            /// <summary>Wind in the rigging: band-limited noise drifting in pitch, in slow gusts.</summary>
            public static float[] Wind(int seed)
            {
                var d = new Dsp(seed);
                float length = 10f, fade = 2f;
                float[] o = Buffer(length + fade);
                var air = new Band();
                var whistle = new Band();
                for (int i = 0; i < o.Length; i++)
                {
                    float t = i / (float)Rate;
                    float n = d.Noise();
                    float gust = 0.55f + 0.3f * Mathf.Sin(t * 2f * Mathf.PI / 5f) + 0.15f * Mathf.Sin(t * 2f * Mathf.PI / 2.3f + 0.7f);
                    float centre = 500f + 450f * gust;
                    o[i] = air.Run(n, centre * 0.5f, centre * 1.6f) * gust + whistle.Run(n, centre * 2.6f, centre * 2.9f) * gust * gust * 0.8f;
                }
                return Normalize(Loopify(o, fade), 0.7f);
            }

            /// <summary>Timbers and lines working as the ship rolls, with water lapping the hull.</summary>
            public static float[] Creaks(int seed)
            {
                var d = new Dsp(seed);
                float length = 11f, fade = 1.5f;
                float[] o = Buffer(length + fade);
                var timber = new Band();
                Lowpass lap = default;
                // Each creak: a stick-slip friction pulse train whose rate rises and falls.
                var creaks = new List<(float at, float dur, float rate, float centre)>();
                for (float at = d.Range(0.3f, 1.2f); at < length; at += d.Range(1.4f, 3.2f))
                    creaks.Add((at, d.Range(0.35f, 0.9f), d.Range(35f, 90f), d.Range(350f, 750f)));
                float pulsePhase = 0f;
                for (int i = 0; i < o.Length; i++)
                {
                    float t = i / (float)Rate;
                    float n = d.Noise();
                    float excite = 0f, centre = 500f;
                    foreach (var c in creaks)
                    {
                        float u = (t - c.at) / c.dur;
                        if (u < 0f || u > 1f) continue;
                        pulsePhase += c.rate * (0.7f + 0.6f * Mathf.Sin(u * Mathf.PI)) / Rate;
                        if (pulsePhase >= 1f) { pulsePhase -= 1f; excite = Mathf.Sin(u * Mathf.PI) * (0.8f + 0.2f * n); }
                        centre = c.centre;
                    }
                    float wood = timber.Run(excite * 4f + n * 0.02f, centre * 0.6f, centre * 1.8f);
                    float water = lap.Run(n, 350f) * (0.5f + 0.5f * Mathf.Sin(t * 2f * Mathf.PI / 1.7f)) * 1.2f;
                    o[i] = wood + water;
                }
                return Normalize(Loopify(o, fade), 0.7f);
            }

            /// <summary>A harbour town: the murmur of voices, a hammer at the boatyard and gulls overhead.</summary>
            public static float[] Town(int seed)
            {
                var d = new Dsp(seed);
                float length = 14f, fade = 2f;
                float[] o = Buffer(length + fade);
                var voices = new Band[5];
                var syllable = new float[5];
                var rates = new float[5];
                for (int v = 0; v < 5; v++) rates[v] = d.Range(3.5f, 6f);
                var gulls = new List<float>();
                for (float at = d.Range(0.5f, 2f); at < length - 1f; at += d.Range(2.5f, 5f)) gulls.Add(at);
                var knocks = new List<float>();
                for (float at = d.Range(1f, 3f); at < length; at += d.Range(4f, 7f))
                    for (int k = 0; k < 4; k++) knocks.Add(at + k * 0.45f);
                float gullPhase = 0f;
                for (int i = 0; i < o.Length; i++)
                {
                    float t = i / (float)Rate;
                    float n = d.Noise();
                    float crowd = 0f;
                    for (int v = 0; v < 5; v++)
                    {
                        syllable[v] = 0.5f + 0.5f * Mathf.Sin(t * rates[v] * 2f * Mathf.PI + v * 1.3f + Mathf.Sin(t * 0.7f + v));
                        float formant = 450f + v * 140f;
                        crowd += voices[v].Run(n, formant * 0.7f, formant * 1.5f) * syllable[v] * syllable[v];
                    }
                    float s = crowd * 0.9f;
                    foreach (float at in gulls)
                    {
                        float u = t - at;
                        if (u < 0f || u > 0.9f) continue;
                        // Two cries per gull: a falling "kee-ow".
                        float local = u % 0.45f;
                        if (local > 0.32f) continue;
                        float f = Mathf.Lerp(1500f, 950f, local / 0.32f);
                        gullPhase += 2f * Mathf.PI * f / Rate;
                        float env = Mathf.Sin(local / 0.32f * Mathf.PI);
                        s += (Mathf.Sin(gullPhase) + 0.4f * Mathf.Sin(gullPhase * 2f) + 0.2f * Mathf.Sin(gullPhase * 3f)) * env * 0.35f;
                    }
                    foreach (float at in knocks)
                    {
                        float u = t - at;
                        if (u < 0f || u > 0.15f) continue;
                        s += Mathf.Sin(2f * Mathf.PI * 380f * u) * Mathf.Exp(-u * 45f) * 0.25f;
                    }
                    o[i] = s;
                }
                return Normalize(Loopify(o, fade), 0.7f);
            }
        }

        // ───────────────────────────── Music ─────────────────────────────

        /// <summary>
        /// Two original loops, composed by a seeded generator and rendered with simple instruments:
        /// a plucked string (Karplus-Strong), a reedy pad (detuned saws, filtered), a soft whistle and a frame drum.
        /// </summary>
        private static class Music
        {
            private static float Freq(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

            private static int Mod(int a, int m) => ((a % m) + m) % m;

            /// <summary>Midi note of a scale step (0 = tonic) in a scale, from a base note.</summary>
            private static int Note(int step, int[] scale, int tonic) =>
                tonic + 12 * Mathf.FloorToInt(step / (float)scale.Length) + scale[Mod(step, scale.Length)];

            private sealed class Score
            {
                public readonly float[] Mix;
                public readonly int Length;
                public Score(float seconds, float tail) { Length = Mathf.RoundToInt(seconds * Rate); Mix = new float[Length + Mathf.RoundToInt(tail * Rate)]; }

                /// <summary>Folds the ringing tail past the loop point back onto the start, so the loop is seamless.</summary>
                public float[] Finish(float peak)
                {
                    var o = new float[Length];
                    for (int i = 0; i < Mix.Length; i++) o[i % Length] += Mix[i];
                    for (int i = 0; i < o.Length; i++) o[i] = (float)Math.Tanh(o[i] * 1.2f);
                    return Normalize(o, peak);
                }
            }

            private static void Pluck(Score score, Dsp d, float at, float seconds, int midi, float amp, float brightness)
            {
                float f = Freq(midi);
                int period = Mathf.Max(2, Mathf.RoundToInt(Rate / f));
                var ring = new float[period];
                Lowpass shape = default;
                for (int i = 0; i < period; i++) ring[i] = shape.Run(d.Noise(), 1500f + brightness * 5000f);
                int start = Mathf.RoundToInt(at * Rate);
                int length = Mathf.RoundToInt((seconds + 0.6f) * Rate);
                int stop = Mathf.RoundToInt(seconds * Rate);
                float damping = 0.996f;
                for (int i = 0, k = 0; i < length && start + i < score.Mix.Length; i++)
                {
                    int next = (k + 1) % period;
                    float s = ring[k];
                    // Let the string ring, then damp it quickly once the note ends.
                    float loss = i < stop ? damping : 0.97f;
                    ring[k] = (ring[k] + ring[next]) * 0.5f * loss;
                    k = next;
                    score.Mix[start + i] += s * amp;
                }
            }

            private static void Pad(Score score, float at, float seconds, int[] chord, float amp, float cutoff)
            {
                int start = Mathf.RoundToInt(at * Rate), length = Mathf.RoundToInt(seconds * Rate);
                foreach (int midi in chord)
                {
                    float f = Freq(midi);
                    float p1 = 0f, p2 = 0f;
                    Lowpass filter = default, filter2 = default;
                    for (int i = 0; i < length && start + i < score.Mix.Length; i++)
                    {
                        float t = i / (float)Rate;
                        p1 += f * 1.003f / Rate; p2 += f * 0.997f / Rate;
                        p1 -= Mathf.Floor(p1); p2 -= Mathf.Floor(p2);
                        float saw = (p1 * 2f - 1f) + (p2 * 2f - 1f);
                        float env = Mathf.Clamp01(t / 0.25f) * Mathf.Clamp01((seconds - t) / 0.3f);
                        // A slow swell, like a squeezebox's bellows.
                        float bellows = 0.85f + 0.15f * Mathf.Sin(t * 2f * Mathf.PI * 0.5f);
                        score.Mix[start + i] += filter2.Run(filter.Run(saw, cutoff), cutoff * 1.5f) * env * bellows * amp;
                    }
                }
            }

            private static void Whistle(Score score, float at, float seconds, int midi, float amp)
            {
                float f = Freq(midi);
                int start = Mathf.RoundToInt(at * Rate), length = Mathf.RoundToInt(seconds * Rate);
                float phase = 0f;
                for (int i = 0; i < length && start + i < score.Mix.Length; i++)
                {
                    float t = i / (float)Rate;
                    float vibrato = 1f + 0.006f * Mathf.Sin(t * 2f * Mathf.PI * 5.5f) * Mathf.Clamp01(t / 0.2f);
                    phase += 2f * Mathf.PI * f * vibrato / Rate;
                    float env = Mathf.Clamp01(t / 0.04f) * Mathf.Clamp01((seconds - t) / 0.06f);
                    score.Mix[start + i] += (Mathf.Sin(phase) + 0.15f * Mathf.Sin(phase * 2f)) * env * amp;
                }
            }

            private static void Drum(Score score, Dsp d, float at, float amp, bool high)
            {
                int start = Mathf.RoundToInt(at * Rate), length = Mathf.RoundToInt(0.35f * Rate);
                float phase = 0f;
                Lowpass skin = default;
                for (int i = 0; i < length && start + i < score.Mix.Length; i++)
                {
                    float t = i / (float)Rate;
                    float s;
                    if (high) s = skin.Run(d.Noise(), 2600f) * Mathf.Exp(-t * 30f) * 2f + Mathf.Sin(phase += 2f * Mathf.PI * 190f / Rate) * Mathf.Exp(-t * 25f) * 0.4f;
                    else s = Mathf.Sin(phase += 2f * Mathf.PI * Mathf.Lerp(110f, 52f, Mathf.Clamp01(t * 12f)) / Rate) * Mathf.Exp(-t * 9f) +
                        skin.Run(d.Noise(), 900f) * Mathf.Exp(-t * 40f) * 0.6f;
                    score.Mix[start + i] += s * amp;
                }
            }

            /// <summary>Composes a melody over a chord progression: chord tones on the strong beats, stepwise motion between.</summary>
            private static List<(int position, int length, int step)> Melody(Dsp d, int[] chords, int beatsPerBar, int[][] rhythms,
                int[] strong, int low, int high, int startStep)
            {
                var notes = new List<(int, int, int)>();
                int step = startStep;
                for (int bar = 0; bar < chords.Length; bar++)
                {
                    bool last = bar == chords.Length - 1;
                    int[] rhythm = last ? new[] { beatsPerBar } : rhythms[Mathf.Abs((int)(d.Noise() * 1000f)) % rhythms.Length];
                    int position = 0;
                    foreach (int length in rhythm)
                    {
                        bool onStrong = Array.IndexOf(strong, position) >= 0;
                        if (last) step = Mathf.RoundToInt(step / 7f) * 7; // home to the tonic
                        else if (onStrong)
                        {
                            // Nearest chord tone, sometimes the next one up or down.
                            int best = step, bestGap = 99;
                            for (int c = chords[bar] - 14; c <= chords[bar] + 14; c += 2)
                            {
                                if (Mod(c - chords[bar], 7) != 0 && Mod(c - chords[bar], 7) != 2 && Mod(c - chords[bar], 7) != 4) continue;
                                int gap = Mathf.Abs(c - step) + (d.Noise() > 0.4f ? 0 : 1);
                                if (gap < bestGap && c >= low && c <= high) { best = c; bestGap = gap; }
                            }
                            step = best;
                        }
                        else
                        {
                            float roll = d.Noise();
                            step += roll > 0.5f ? 1 : roll > 0f ? -1 : roll > -0.5f ? 2 : -2;
                            if (step > high) step -= 2;
                            if (step < low) step += 2;
                        }
                        notes.Add((bar * beatsPerBar + position, length, step));
                        position += length;
                    }
                }
                return notes;
            }

            /// <summary>The sailing tune: D dorian, a lilting 6/8.</summary>
            public static float[] Calm()
            {
                var d = new Dsp(1803);
                int[] scale = { 0, 2, 3, 5, 7, 9, 10 };
                const int tonic = 62; // D4
                float eighth = 60f / 92f / 3f * 1f;
                int[] chords = { 0, -1, 0, 4, 2, -1, 4, 0, 0, -1, 0, 4, 2, 3, -1, 0 };
                float bar = eighth * 6f;
                var score = new Score(bar * chords.Length, 2f);
                int[][] rhythms = { new[] { 3, 3 }, new[] { 2, 1, 2, 1 }, new[] { 1, 1, 1, 3 }, new[] { 2, 1, 3 }, new[] { 3, 1, 1, 1 } };
                var first = Melody(d, Slice(chords, 0, 8), 6, rhythms, new[] { 0, 3 }, 3, 11, 7);
                var second = Melody(d, Slice(chords, 8, 8), 6, rhythms, new[] { 0, 3 }, 3, 11, 9);
                foreach (var n in first) Pluck(score, d, n.position * eighth, n.length * eighth * 0.95f, Note(n.step, scale, tonic), 0.5f, 0.45f);
                foreach (var n in second)
                {
                    float at = (n.position + 48) * eighth;
                    Pluck(score, d, at, n.length * eighth * 0.95f, Note(n.step, scale, tonic), 0.42f, 0.45f);
                    Whistle(score, at, n.length * eighth * 0.92f, Note(n.step, scale, tonic) + 12, 0.07f);
                }
                for (int b = 0; b < chords.Length; b++)
                {
                    int root = chords[b];
                    Pluck(score, d, b * bar, eighth * 2.8f, Note(root, scale, tonic - 24), 0.55f, 0.15f);
                    Pluck(score, d, b * bar + eighth * 3f, eighth * 2.8f, Note(root + 4, scale, tonic - 24), 0.45f, 0.15f);
                    Pad(score, b * bar, bar + 0.05f, new[] { Note(root, scale, tonic - 12), Note(root + 2, scale, tonic - 12), Note(root + 4, scale, tonic - 12) }, 0.028f, 900f);
                }
                return score.Finish(0.8f);
            }

            /// <summary>The battle theme: D minor with a raised leading tone, a driving 4/4 with frame drum.</summary>
            public static float[] Battle()
            {
                var d = new Dsp(4271);
                int[] minor = { 0, 2, 3, 5, 7, 8, 10 };
                int[] harmonic = { 0, 2, 3, 5, 7, 8, 11 };
                const int tonic = 62;
                float eighth = 60f / 132f / 2f;
                int[] chords = { 0, 5, 6, 4, 0, 5, 3, 4, 0, 5, 6, 4, 0, 3, 4, 0 };
                float bar = eighth * 8f;
                var score = new Score(bar * chords.Length, 2f);
                int[][] rhythms = { new[] { 2, 2, 2, 2 }, new[] { 1, 1, 2, 1, 1, 2 }, new[] { 3, 1, 2, 2 }, new[] { 1, 1, 1, 1, 2, 2 }, new[] { 2, 1, 1, 4 } };
                var melody = Melody(d, chords, 8, rhythms, new[] { 0, 4 }, 2, 11, 7);
                foreach (var n in melody)
                {
                    int bars = n.position / 8;
                    int[] scale = chords[Mathf.Min(bars, chords.Length - 1)] == 4 ? harmonic : minor;
                    Pluck(score, d, n.position * eighth, n.length * eighth * 0.7f, Note(n.step, scale, tonic), 0.5f, 0.7f);
                    if (bars >= 8) Whistle(score, n.position * eighth, n.length * eighth * 0.7f, Note(n.step, scale, tonic) + 12, 0.05f);
                }
                for (int b = 0; b < chords.Length; b++)
                {
                    int root = chords[b];
                    int[] scale = root == 4 ? harmonic : minor;
                    for (int e = 0; e < 8; e++)
                    {
                        // Galloping bass: root on the beat, octave on the off-beat.
                        int midi = Note(root, scale, tonic - 24) + (e % 2 == 1 ? 12 : 0);
                        Pluck(score, d, (b * 8 + e) * eighth, eighth * 0.8f, midi, e % 2 == 0 ? 0.5f : 0.3f, 0.25f);
                    }
                    Pad(score, b * bar, bar + 0.05f, new[] { Note(root, scale, tonic - 12), Note(root + 2, scale, tonic - 12), Note(root + 4, scale, tonic - 12) }, 0.022f, 1200f);
                    bool fill = b % 4 == 3;
                    for (int e = 0; e < 8; e++)
                    {
                        float at = (b * 8 + e) * eighth;
                        if (e == 0 || e == 4) Drum(score, d, at, 0.8f, false);
                        else if (e == 2 || e == 6) Drum(score, d, at, 0.45f, true);
                        else if (fill && e >= 5) Drum(score, d, at, 0.35f, true);
                        else if (e == 7) Drum(score, d, at, 0.15f, true);
                    }
                }
                return score.Finish(0.8f);
            }

            private static int[] Slice(int[] source, int start, int count)
            {
                var result = new int[count];
                Array.Copy(source, start, result, 0, count);
                return result;
            }
        }
    }
}
