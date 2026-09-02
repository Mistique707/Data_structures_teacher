using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Pivot.Utils
{
    /// <summary>
    /// Runs once at startup: loads settings, pins the frame rate, and turns on fixed
    /// foveated rendering if a headset is actually present. Lives on a small object
    /// in the title scene and survives into the lab.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class AppBootstrap : MonoBehaviour
    {
        [SerializeField] StartupConfig _config;
        [SerializeField] ThemeSO _theme;

        static AppBootstrap _instance;
        static readonly List<XRDisplaySubsystem> DisplayScratch = new List<XRDisplaySubsystem>(2);

        public static AppBootstrap Instance
        {
            get { return _instance; }
        }

        public StartupConfig Config
        {
            get { return _config; }
        }

        public ThemeSO Theme
        {
            get { return _theme; }
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            Settings.Load(_config);
            ApplyFrameRate();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void Start()
        {
            // The display subsystem only exists once a loader has started, which is
            // after Awake, so foveation is set here rather than above.
            ApplyFoveation();
        }

        void ApplyFrameRate()
        {
            QualitySettings.vSyncCount = 0;

            if (IsHeadsetPresent())
            {
                Application.targetFrameRate = _config != null ? _config.QuestTargetFrameRate : 72;
                return;
            }

            // On desktop, match the display rather than burning frames the monitor
            // will never show.
            int refresh = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);
            Application.targetFrameRate = refresh > 0 ? refresh : 60;
        }

        void ApplyFoveation()
        {
            if (_config != null && !_config.EnableFoveatedRendering) return;

            DisplayScratch.Clear();
            SubsystemManager.GetSubsystems(DisplayScratch);

            for (int i = 0; i < DisplayScratch.Count; i++)
            {
                XRDisplaySubsystem display = DisplayScratch[i];
                if (display == null || !display.running) continue;

                // 1 is full strength. Anything the runtime cannot do is ignored rather
                // than thrown, so this is safe on a runtime without foveation.
                display.foveatedRenderingLevel = 1f;
                display.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.GazeAllowed;
            }
        }

        public static bool IsHeadsetPresent()
        {
            DisplayScratch.Clear();
            SubsystemManager.GetSubsystems(DisplayScratch);

            for (int i = 0; i < DisplayScratch.Count; i++)
            {
                if (DisplayScratch[i] != null && DisplayScratch[i].running) return true;
            }

            return false;
        }
    }
}
