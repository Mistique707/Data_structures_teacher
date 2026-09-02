using UnityEngine;

namespace Pivot.Utils
{
    /// <summary>
    /// Every colour and every feel curve in the app, in one asset. Nothing anywhere
    /// else hardcodes a colour: if a shade appears twice on screen it is because two
    /// things read the same field here.
    /// </summary>
    [CreateAssetMenu(menuName = "Pivot/Theme", fileName = "Theme")]
    public sealed class ThemeSO : ScriptableObject
    {
        [Header("Node bubbles by depth")]
        [Tooltip("Depth 0 is the root. Deeper levels wrap around once the list runs out.")]
        public Color[] DepthColours =
        {
            new Color(1.00f, 0.30f, 0.37f), // coral
            new Color(1.00f, 0.77f, 0.24f), // amber
            new Color(0.24f, 0.84f, 0.55f), // mint
            new Color(0.22f, 0.74f, 0.97f), // sky
            new Color(0.66f, 0.33f, 0.97f), // violet
            new Color(0.93f, 0.28f, 0.60f)  // magenta
        };

        [Header("Bubble surface")]
        [ColorUsage(false, true)] public Color SpecularTint = new Color(1f, 1f, 1f);
        [Range(0f, 1f)] public float Gloss = 0.86f;
        [Range(0f, 1f)] public float RimStrength = 0.55f;
        [Range(0.5f, 8f)] public float RimPower = 3.2f;
        [Range(0f, 1f)] public float InnerGlow = 0.22f;

        [Header("States")]
        public Color ValidDrop = new Color(0.24f, 0.84f, 0.55f);
        public Color InvalidDrop = new Color(1.00f, 0.30f, 0.37f);
        public Color Held = new Color(1.00f, 0.90f, 0.40f);
        public Color TraversalPulse = new Color(1.00f, 0.88f, 0.40f);
        public Color ComparisonPath = new Color(1.00f, 0.77f, 0.24f);

        [Header("Edges")]
        public float EdgeRadius = 0.045f;
        [Range(0f, 1f)] public float EdgeGradientBlend = 0.85f;

        [Header("Environment")]
        public Color SkyTop = new Color(0.106f, 0.118f, 0.294f);
        public Color SkyHorizon = new Color(0.231f, 0.165f, 0.420f);
        public Color SkyGround = new Color(0.071f, 0.078f, 0.180f);
        public Color FloorTint = new Color(0.129f, 0.141f, 0.310f);
        public Color GridLine = new Color(0.35f, 0.33f, 0.62f, 0.35f);

        [Header("Text")]
        public Color Label = Color.white;
        public Color LabelOutline = new Color(0.106f, 0.118f, 0.294f);
        [Range(0f, 1f)] public float LabelOutlineWidth = 0.22f;

        [Header("Feel curves")]
        [Tooltip("Drives every reflow of the tree. Ease out, never linear.")]
        public AnimationCurve Reflow = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 2.2f), new Keyframe(1f, 1f, 0f, 0f));

        [Tooltip("Spawn and snap. Overshoots past one, then settles.")]
        public AnimationCurve Pop = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.45f, 1.22f), new Keyframe(0.72f, 0.94f),
            new Keyframe(1f, 1f));

        [Tooltip("Squash on grab and on button press.")]
        public AnimationCurve Squash = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f));

        [Tooltip("Refused drop. Shakes, then returns.")]
        public AnimationCurve Bounce = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.25f, 1f), new Keyframe(0.5f, -0.5f),
            new Keyframe(0.75f, 0.2f), new Keyframe(1f, 0f));

        [Header("Timings (seconds, before the speed multiplier)")]
        public float ReflowDuration = 0.42f;
        public float PopDuration = 0.34f;
        public float StepBeat = 0.55f;
        public float RotationDuration = 0.7f;

        [Header("Idle life")]
        [Tooltip("Nothing is ever perfectly still.")]
        public float IdleBobAmplitude = 0.014f;
        public float IdleBobSpeed = 1.1f;
        public float BreatheAmplitude = 0.018f;
        public float BreatheSpeed = 0.75f;

        public Color ColourForDepth(int depth)
        {
            if (DepthColours == null || DepthColours.Length == 0) return Color.white;
            int index = depth % DepthColours.Length;
            if (index < 0) index += DepthColours.Length;
            return DepthColours[index];
        }
    }
}
