using UnityEngine;

namespace Pivot.Utils
{
    public enum RigMode
    {
        /// <summary>Use VR when an XR loader actually started, otherwise desktop.</summary>
        Auto,
        ForceVr,
        ForceDesktop
    }

    public enum EnvironmentMode
    {
        Skybox,
        Passthrough
    }

    public enum ParticleDensity
    {
        Low,
        Medium,
        High
    }

    /// <summary>
    /// Boot-time choices that live in the project rather than in a player's saved
    /// settings. The title screen can override the rig at runtime, and the saved
    /// settings override this again; this asset is only the starting point.
    /// </summary>
    [CreateAssetMenu(menuName = "Pivot/Startup Config", fileName = "StartupConfig")]
    public sealed class StartupConfig : ScriptableObject
    {
        [Header("Rig")]
        public RigMode Rig = RigMode.Auto;

        [Tooltip("Editor only. Turns on the XR Device Simulator so VR can be exercised without a headset.")]
        public bool UseDeviceSimulatorInEditor = true;

        [Header("Environment")]
        public EnvironmentMode Environment = EnvironmentMode.Skybox;

        [Header("Performance")]
        [Tooltip("Quest holds 72. Desktop uses the display rate instead.")]
        public int QuestTargetFrameRate = 72;

        public bool EnableFoveatedRendering = true;

        [Header("Defaults for a player who has never opened Settings")]
        [Range(0.25f, 3f)] public float AnimationSpeed = 1f;
        public ParticleDensity Particles = ParticleDensity.Medium;
        [Range(0f, 1f)] public float MasterVolume = 0.8f;
        [Range(0.1f, 5f)] public float MouseSensitivity = 1f;
        public bool LeftHanded;

        [Header("Tree Lab")]
        [Tooltip("The tree the lab scene starts with. Also what Reset rebuilds.")]
        public int[] SeedValues = { 50, 30, 70, 20, 40, 60, 80 };

        [Tooltip("Values available as grabbable orbs in the bin.")]
        public int[] BinValues = { 10, 25, 35, 45, 55, 65, 75, 90 };

        [Tooltip("The set used by order-matters mode, where the tree starts empty.")]
        public int[] OrderLessonValues = { 20, 30, 40, 50, 60 };
    }
}
