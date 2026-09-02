using System.Collections.Generic;
using UnityEngine;

namespace Pivot.Utils
{
    /// <summary>Named hooks the rest of the app fires. Never a raw clip reference.</summary>
    public enum Sfx
    {
        Pop,
        Snap,
        Error,
        Whoosh,
        Chime,
        UIHover,
        UIPress,
        Step
    }

    /// <summary>
    /// Pooled one-shots behind named hooks. Ships with procedurally generated blips
    /// so the app is never silent, and every one of them is overridable from the
    /// Inspector: drop a clip into the matching slot and it wins. The README lists
    /// which file replaces which hook.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class AudioService : MonoBehaviour
    {
        [System.Serializable]
        public struct Slot
        {
            public Sfx Hook;
            public AudioClip Clip;
            [Range(0f, 1f)] public float Volume;
        }

        [Header("Drop replacement clips here; empty slots use a generated blip")]
        [SerializeField] Slot[] _overrides = new Slot[0];

        [Header("Mix")]
        [SerializeField, Range(0f, 1f)] float _masterVolume = 0.8f;
        [SerializeField] int _voiceCount = 12;

        static AudioService _instance;

        readonly Dictionary<Sfx, AudioClip> _clips = new Dictionary<Sfx, AudioClip>(8);
        readonly Dictionary<Sfx, float> _volumes = new Dictionary<Sfx, float>(8);
        AudioSource[] _voices;
        int _nextVoice;

        public static AudioService Instance
        {
            get { return _instance; }
        }

        public float MasterVolume
        {
            get { return _masterVolume; }
            set { _masterVolume = Mathf.Clamp01(value); }
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            BuildVoices();
            BuildClips();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void BuildVoices()
        {
            _voices = new AudioSource[Mathf.Max(2, _voiceCount)];
            for (int i = 0; i < _voices.Length; i++)
            {
                GameObject voice = new GameObject("Voice " + i);
                voice.transform.SetParent(transform, false);
                AudioSource source = voice.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.dopplerLevel = 0f;
                _voices[i] = source;
            }
        }

        void BuildClips()
        {
            Register(Sfx.Pop, ProceduralAudio.Pop(), 0.85f);
            Register(Sfx.Snap, ProceduralAudio.Snap(), 0.7f);
            Register(Sfx.Error, ProceduralAudio.Error(), 0.55f);
            Register(Sfx.Whoosh, ProceduralAudio.Whoosh(), 0.5f);
            Register(Sfx.Chime, ProceduralAudio.Chime(), 0.6f);
            Register(Sfx.UIHover, ProceduralAudio.Blip(1180f, 0.05f), 0.3f);
            Register(Sfx.UIPress, ProceduralAudio.Blip(760f, 0.09f), 0.5f);
            Register(Sfx.Step, ProceduralAudio.Blip(520f, 0.06f), 0.35f);

            for (int i = 0; i < _overrides.Length; i++)
            {
                if (_overrides[i].Clip == null) continue;
                _clips[_overrides[i].Hook] = _overrides[i].Clip;
                _volumes[_overrides[i].Hook] = _overrides[i].Volume <= 0f ? 1f : _overrides[i].Volume;
            }
        }

        void Register(Sfx hook, AudioClip clip, float volume)
        {
            _clips[hook] = clip;
            _volumes[hook] = volume;
        }

        public void Play(Sfx hook, float pitch = 1f)
        {
            if (_voices == null) return;

            AudioClip clip;
            if (!_clips.TryGetValue(hook, out clip) || clip == null) return;

            float volume;
            if (!_volumes.TryGetValue(hook, out volume)) volume = 1f;

            AudioSource source = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _voices.Length;

            source.pitch = pitch;
            source.PlayOneShot(clip, volume * _masterVolume);
        }

        /// <summary>Fires a hook with a small random pitch spread so repeats do not grate.</summary>
        public void PlayVaried(Sfx hook, float spread = 0.08f)
        {
            Play(hook, 1f + Random.Range(-spread, spread));
        }

        public static void Fire(Sfx hook, float pitch = 1f)
        {
            if (_instance != null) _instance.Play(hook, pitch);
        }
    }

    /// <summary>
    /// Placeholder blips built in memory at startup, so a fresh clone of the repo
    /// makes noise without shipping any audio files. These are meant to be replaced.
    /// </summary>
    public static class ProceduralAudio
    {
        const int SampleRate = 44100;

        public static AudioClip Pop()
        {
            return Build("sfx_pop", 0.13f, delegate(float t, float u)
            {
                // A quick upward chirp with a fast body, the bubble-burst shape.
                float frequency = Mathf.Lerp(420f, 1500f, Mathf.Sqrt(u));
                float envelope = Mathf.Exp(-16f * u);
                float body = Mathf.Sin(2f * Mathf.PI * frequency * t);
                float click = Mathf.Exp(-90f * u) * (Random.value * 2f - 1f) * 0.4f;
                return (body * 0.8f + click) * envelope;
            });
        }

        public static AudioClip Snap()
        {
            return Build("sfx_snap", 0.11f, delegate(float t, float u)
            {
                float frequency = Mathf.Lerp(900f, 560f, u);
                float envelope = Mathf.Exp(-22f * u);
                return Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope;
            });
        }

        public static AudioClip Error()
        {
            return Build("sfx_error", 0.2f, delegate(float t, float u)
            {
                // Two soft descending tones: a nudge, never a buzzer.
                float frequency = u < 0.5f ? 330f : 262f;
                float envelope = Mathf.Exp(-9f * u) * Mathf.Min(1f, u * 40f);
                return Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.7f;
            });
        }

        public static AudioClip Whoosh()
        {
            float state = 0f;
            return Build("sfx_whoosh", 0.26f, delegate(float t, float u)
            {
                // Filtered noise swelling then fading.
                float noise = Random.value * 2f - 1f;
                state = Mathf.Lerp(state, noise, 0.06f + 0.12f * u);
                float envelope = Mathf.Sin(Mathf.PI * u);
                return state * envelope * 0.6f;
            });
        }

        public static AudioClip Chime()
        {
            return Build("sfx_chime", 0.55f, delegate(float t, float u)
            {
                // A major triad, which is what "you got it" sounds like.
                float envelope = Mathf.Exp(-5f * u);
                float a = Mathf.Sin(2f * Mathf.PI * 523.25f * t);
                float b = Mathf.Sin(2f * Mathf.PI * 659.25f * t) * 0.7f;
                float c = Mathf.Sin(2f * Mathf.PI * 783.99f * t) * 0.5f;
                return (a + b + c) * envelope * 0.3f;
            });
        }

        public static AudioClip Blip(float frequency, float duration)
        {
            return Build("sfx_blip", duration, delegate(float t, float u)
            {
                float envelope = Mathf.Sin(Mathf.PI * u);
                return Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.6f;
            });
        }

        delegate float Shape(float time, float normalised);

        static AudioClip Build(string name, float seconds, Shape shape)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            float[] samples = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float u = i / (float)(count - 1 < 1 ? 1 : count - 1);
                samples[i] = Mathf.Clamp(shape(t, u), -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
