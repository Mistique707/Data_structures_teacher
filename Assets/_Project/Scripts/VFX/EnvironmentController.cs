using Pivot.Utils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Pivot.VFX
{
    /// <summary>
    /// Owns everything that changes between the authored dusk environment and
    /// passthrough, and switches cleanly between them at runtime.
    ///
    /// Passthrough is not just "hide the sky". The compositor blends on the camera's
    /// alpha channel, so the camera has to clear to solid black with alpha zero, and
    /// nothing in the frame may overwrite that alpha. URP's post stack does exactly
    /// that, which is why post-processing is switched off with the sky rather than
    /// left running. Bloom therefore belongs to the skybox mode only, and the glossy
    /// read on Quest comes from the bubble material itself.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public sealed class EnvironmentController : MonoBehaviour
    {
        [Header("Bindings")]
        [SerializeField] ThemeSO _theme;
        [SerializeField] Material _skyboxMaterial;

        [Tooltip("Floor, backdrop props, anything that only makes sense with a sky behind it.")]
        [SerializeField] GameObject[] _skyboxOnly = new GameObject[0];

        [Tooltip("Bloom and grading. Skybox mode only; passthrough needs the alpha channel intact.")]
        [SerializeField] Volume _postVolume;

        [Header("Ambient")]
        [SerializeField, Range(0f, 2f)] float _ambientIntensity = 1f;
        [SerializeField, Range(0f, 2f)] float _passthroughAmbient = 1.15f;

        static readonly int TopColour = Shader.PropertyToID("_TopColour");
        static readonly int HorizonColour = Shader.PropertyToID("_HorizonColour");
        static readonly int GroundColour = Shader.PropertyToID("_GroundColour");

        Camera _camera;
        UniversalAdditionalCameraData _cameraData;
        EnvironmentMode _applied = (EnvironmentMode)(-1);

        public EnvironmentMode Current
        {
            get { return _applied; }
        }

#if UNITY_EDITOR
        /// <summary>Used by the scene authoring tool. Editor only, never at run time.</summary>
        public void EditorBind(ThemeSO theme, Material skybox, GameObject[] skyboxOnly)
        {
            _theme = theme;
            _skyboxMaterial = skybox;
            _skyboxOnly = skyboxOnly;
        }
#endif

        void OnEnable()
        {
            Settings.Changed += OnSettingsChanged;
        }

        void OnDisable()
        {
            Settings.Changed -= OnSettingsChanged;
        }

        void Start()
        {
            PushThemeToSkybox();
            if (_camera == null) Bind(Camera.main);
            Apply(Settings.IsLoaded ? Settings.Environment : EnvironmentMode.Skybox, true);
        }

        /// <summary>
        /// Called by the rig manager once it knows which camera won. Doing it this way
        /// means nothing here ever reaches for Camera.main at runtime.
        /// </summary>
        public void Bind(Camera camera)
        {
            if (camera == null) return;

            _camera = camera;
            _cameraData = camera.GetUniversalAdditionalCameraData();
            Apply(_applied == (EnvironmentMode)(-1) ? EnvironmentMode.Skybox : _applied, true);
        }

        void OnSettingsChanged()
        {
            Apply(Settings.Environment, false);
        }

        public void Apply(EnvironmentMode mode, bool force)
        {
            if (!force && mode == _applied) return;
            _applied = mode;

            bool sky = mode == EnvironmentMode.Skybox;

            if (sky) ApplySkybox();
            else ApplyPassthrough();

            for (int i = 0; i < _skyboxOnly.Length; i++)
            {
                if (_skyboxOnly[i] != null) _skyboxOnly[i].SetActive(sky);
            }

            if (_postVolume != null) _postVolume.enabled = sky;
            if (_cameraData != null) _cameraData.renderPostProcessing = sky;
        }

        void ApplySkybox()
        {
            if (_skyboxMaterial != null) RenderSettings.skybox = _skyboxMaterial;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientIntensity = _ambientIntensity;

            if (_theme != null)
            {
                RenderSettings.ambientSkyColor = _theme.SkyTop;
                RenderSettings.ambientEquatorColor = _theme.SkyHorizon;
                RenderSettings.ambientGroundColor = _theme.SkyGround;
            }

            RenderSettings.fog = false;

            if (_camera != null)
            {
                _camera.clearFlags = CameraClearFlags.Skybox;
                _camera.backgroundColor = _theme != null ? _theme.SkyHorizon : Color.black;
            }

            DynamicGI.UpdateEnvironment();
        }

        void ApplyPassthrough()
        {
            // No sky at all, and a fully transparent clear: the runtime composites the
            // real room in wherever alpha is zero.
            RenderSettings.skybox = null;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = _theme != null
                ? Color.Lerp(_theme.SkyHorizon, Color.white, 0.35f)
                : new Color(0.5f, 0.5f, 0.55f);
            RenderSettings.ambientIntensity = _passthroughAmbient;
            RenderSettings.fog = false;

            if (_camera != null)
            {
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            }

            DynamicGI.UpdateEnvironment();
        }

        /// <summary>Copies the theme's sky colours onto the skybox material.</summary>
        public void PushThemeToSkybox()
        {
            if (_theme == null || _skyboxMaterial == null) return;

            _skyboxMaterial.SetColor(TopColour, _theme.SkyTop);
            _skyboxMaterial.SetColor(HorizonColour, _theme.SkyHorizon);
            _skyboxMaterial.SetColor(GroundColour, _theme.SkyGround);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!Application.isPlaying) PushThemeToSkybox();
        }
#endif
    }
}
