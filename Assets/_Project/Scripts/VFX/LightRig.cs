using Pivot.Utils;
using UnityEngine;

namespace Pivot.VFX
{
    /// <summary>
    /// The lighting the whole look is tuned against: one soft key from above and
    /// behind, a dim cool fill from the opposite side, and ambient from the sky
    /// gradient. Deliberately only two lights, because the design gets its glow from
    /// emission in the bubble material rather than from light sources — which is what
    /// lets the renderer stay on plain Forward.
    ///
    /// The fill is a second directional rather than a point light so it costs nothing
    /// extra per object and never falls off across the workbench.
    /// </summary>
    [ExecuteAlways]
    public sealed class LightRig : MonoBehaviour
    {
        [Header("Bindings")]
        [SerializeField] ThemeSO _theme;
        [SerializeField] Light _key;
        [SerializeField] Light _fill;

        [Header("Key")]
        [SerializeField] Vector3 _keyAngles = new Vector3(48f, 152f, 0f);
        [SerializeField, Range(0f, 3f)] float _keyIntensity = 1.15f;
        [SerializeField] Color _keyColour = new Color(1f, 0.94f, 0.86f);
        [SerializeField, Range(0f, 1f)] float _keyShadowStrength = 0.55f;

        [Header("Fill")]
        [SerializeField] Vector3 _fillAngles = new Vector3(18f, -40f, 0f);
        [SerializeField, Range(0f, 2f)] float _fillIntensity = 0.35f;
        [SerializeField] Color _fillColour = new Color(0.62f, 0.70f, 1f);

        void OnEnable()
        {
            Apply();
        }

        void OnValidate()
        {
            Apply();
        }

#if UNITY_EDITOR
        /// <summary>Used by the scene authoring tool. Editor only, never at run time.</summary>
        public void EditorBind(ThemeSO theme, Light key, Light fill)
        {
            _theme = theme;
            _key = key;
            _fill = fill;
        }
#endif

        public void Apply()
        {
            if (_key != null)
            {
                _key.type = LightType.Directional;
                _key.transform.localRotation = Quaternion.Euler(_keyAngles);
                _key.color = _keyColour;
                _key.intensity = _keyIntensity;
                _key.shadows = LightShadows.Soft;
                _key.shadowStrength = _keyShadowStrength;

                // A single cascade over a short distance: the lab is one room, and a
                // soft, cheap contact shadow is all the grounding the scene needs.
                _key.shadowBias = 0.05f;
                _key.shadowNormalBias = 0.4f;
                _key.shadowNearPlane = 0.1f;
            }

            if (_fill != null)
            {
                _fill.type = LightType.Directional;
                _fill.transform.localRotation = Quaternion.Euler(_fillAngles);
                _fill.color = _theme != null
                    ? Color.Lerp(_fillColour, _theme.SkyHorizon, 0.35f)
                    : _fillColour;
                _fill.intensity = _fillIntensity;

                // Only one directional light may cast shadows without extra cost, and
                // that budget belongs to the key.
                _fill.shadows = LightShadows.None;
            }
        }
    }
}
