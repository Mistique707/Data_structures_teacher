using System;
using UnityEngine;

namespace Pivot.Utils
{
    /// <summary>
    /// Player-facing settings, persisted with PlayerPrefs behind a small service so
    /// nothing else in the app ever touches a preference key. Changing a value fires
    /// <see cref="Changed"/>, and everything that cares reacts to that rather than
    /// polling in Update.
    /// </summary>
    public static class Settings
    {
        const string KeyRig = "pivot.rig";
        const string KeyEnvironment = "pivot.environment";
        const string KeyAnimationSpeed = "pivot.animspeed";
        const string KeyParticles = "pivot.particles";
        const string KeyVolume = "pivot.volume";
        const string KeySensitivity = "pivot.sensitivity";
        const string KeyLeftHanded = "pivot.lefthanded";

        static bool _loaded;

        // Initialised to the same fallbacks Load() uses, so a scene that runs before
        // anything has called Load() gets sane values rather than zeros. A zero mouse
        // sensitivity here once disabled mouse look entirely while every other control
        // kept working, which is exactly the kind of failure that hides.
        static RigMode _rig = RigMode.Auto;
        static EnvironmentMode _environment = EnvironmentMode.Skybox;
        static float _animationSpeed = 1f;
        static ParticleDensity _particles = ParticleDensity.Medium;
        static float _masterVolume = 0.8f;
        static float _mouseSensitivity = 1f;
        static bool _leftHanded;

        /// <summary>Fired whenever any value changes, including on the initial load.</summary>
        public static event Action Changed;

        /// <summary>
        /// Seeds the defaults from the project's startup asset, then lets anything the
        /// player has previously saved win. Safe to call more than once.
        /// </summary>
        public static void Load(StartupConfig config)
        {
            _rig = (RigMode)PlayerPrefs.GetInt(KeyRig, (int)(config != null ? config.Rig : RigMode.Auto));
            _environment = (EnvironmentMode)PlayerPrefs.GetInt(KeyEnvironment,
                (int)(config != null ? config.Environment : EnvironmentMode.Skybox));
            _animationSpeed = PlayerPrefs.GetFloat(KeyAnimationSpeed,
                config != null ? config.AnimationSpeed : 1f);
            _particles = (ParticleDensity)PlayerPrefs.GetInt(KeyParticles,
                (int)(config != null ? config.Particles : ParticleDensity.Medium));
            _masterVolume = PlayerPrefs.GetFloat(KeyVolume, config != null ? config.MasterVolume : 0.8f);
            _mouseSensitivity = PlayerPrefs.GetFloat(KeySensitivity,
                config != null ? config.MouseSensitivity : 1f);
            _leftHanded = PlayerPrefs.GetInt(KeyLeftHanded,
                config != null && config.LeftHanded ? 1 : 0) != 0;

            _loaded = true;
            Apply();
            Raise();
        }

        public static bool IsLoaded
        {
            get { return _loaded; }
        }

        public static RigMode Rig
        {
            get { return _rig; }
            set
            {
                if (_rig == value) return;
                _rig = value;
                PlayerPrefs.SetInt(KeyRig, (int)value);
                Save();
            }
        }

        public static EnvironmentMode Environment
        {
            get { return _environment; }
            set
            {
                if (_environment == value) return;
                _environment = value;
                PlayerPrefs.SetInt(KeyEnvironment, (int)value);
                Save();
            }
        }

        public static float AnimationSpeed
        {
            get { return _animationSpeed; }
            set
            {
                float clamped = Mathf.Clamp(value, 0.25f, 3f);
                if (Mathf.Approximately(_animationSpeed, clamped)) return;
                _animationSpeed = clamped;
                PlayerPrefs.SetFloat(KeyAnimationSpeed, clamped);
                Save();
            }
        }

        public static ParticleDensity Particles
        {
            get { return _particles; }
            set
            {
                if (_particles == value) return;
                _particles = value;
                PlayerPrefs.SetInt(KeyParticles, (int)value);
                Save();
            }
        }

        public static float MasterVolume
        {
            get { return _masterVolume; }
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(_masterVolume, clamped)) return;
                _masterVolume = clamped;
                PlayerPrefs.SetFloat(KeyVolume, clamped);
                Save();
            }
        }

        public static float MouseSensitivity
        {
            get { return _mouseSensitivity; }
            set
            {
                float clamped = Mathf.Clamp(value, 0.1f, 5f);
                if (Mathf.Approximately(_mouseSensitivity, clamped)) return;
                _mouseSensitivity = clamped;
                PlayerPrefs.SetFloat(KeySensitivity, clamped);
                Save();
            }
        }

        public static bool LeftHanded
        {
            get { return _leftHanded; }
            set
            {
                if (_leftHanded == value) return;
                _leftHanded = value;
                PlayerPrefs.SetInt(KeyLeftHanded, value ? 1 : 0);
                Save();
            }
        }

        /// <summary>How many particles a burst should emit, as a fraction of its authored count.</summary>
        public static float ParticleScale
        {
            get
            {
                switch (_particles)
                {
                    case ParticleDensity.Low: return 0.35f;
                    case ParticleDensity.High: return 1f;
                    default: return 0.65f;
                }
            }
        }

        static void Save()
        {
            PlayerPrefs.Save();
            Apply();
            Raise();
        }

        /// <summary>Pushes the values that other systems read once rather than per frame.</summary>
        static void Apply()
        {
            TweenRunner.SpeedMultiplier = _animationSpeed;
            if (AudioService.Instance != null) AudioService.Instance.MasterVolume = _masterVolume;
        }

        static void Raise()
        {
            Action handler = Changed;
            if (handler != null) handler();
        }

        /// <summary>Wipes saved preferences and reloads the project defaults.</summary>
        public static void ResetToDefaults(StartupConfig config)
        {
            PlayerPrefs.DeleteKey(KeyRig);
            PlayerPrefs.DeleteKey(KeyEnvironment);
            PlayerPrefs.DeleteKey(KeyAnimationSpeed);
            PlayerPrefs.DeleteKey(KeyParticles);
            PlayerPrefs.DeleteKey(KeyVolume);
            PlayerPrefs.DeleteKey(KeySensitivity);
            PlayerPrefs.DeleteKey(KeyLeftHanded);
            PlayerPrefs.Save();
            Load(config);
        }
    }
}
