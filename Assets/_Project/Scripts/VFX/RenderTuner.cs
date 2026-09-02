using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Pivot.VFX
{
    /// <summary>
    /// Runtime switches for the three settings whose cost has to be measured rather
    /// than guessed. All of them are live: the same build answers every question, so
    /// an A/B on the headset never needs a rebuild.
    ///
    ///   F4  post-processing: off  ->  mobile profile  ->  desktop profile
    ///   F5  camera depth texture on / off
    ///   F6  MSAA 1x / 2x / 4x
    ///
    /// The default on every platform is <b>post off with the mobile profile queued</b>,
    /// because that is the read the Quest actually gets: HDR is off on the mobile tier,
    /// so there is no bloom there and the bubble material has to carry the gloss on its
    /// own. Desktop bloom is polish layered on afterwards, never the thing the material
    /// was tuned against.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public sealed class RenderTuner : MonoBehaviour
    {
        public enum PostMode
        {
            Off,
            Mobile,
            Desktop
        }

        [Header("Bindings")]
        [SerializeField] Volume _postVolume;

        [Tooltip("What the Quest gets. No bloom: the mobile tier renders without HDR.")]
        [SerializeField] VolumeProfile _mobileProfile;

        [Tooltip("Desktop polish. Subtle bloom on top of a look that already works without it.")]
        [SerializeField] VolumeProfile _desktopProfile;

        [Header("Starting state")]
        [Tooltip("Leave off. The material has to read as glass with no post at all.")]
        [SerializeField] PostMode _startingPost = PostMode.Off;

        static RenderTuner _instance;

        readonly StringBuilder _state = new StringBuilder(96);

        Camera _camera;
        UniversalAdditionalCameraData _cameraData;
        PostMode _post;

        public static RenderTuner Instance
        {
            get { return _instance; }
        }

        public PostMode Post
        {
            get { return _post; }
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void Start()
        {
            if (_camera == null) Bind(Camera.main);
            ApplyPost(_startingPost);
        }

        public void Bind(Camera camera)
        {
            if (camera == null) return;
            _camera = camera;
            _cameraData = camera.GetUniversalAdditionalCameraData();
            ApplyPost(_post);
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.f4Key.wasPressedThisFrame) CyclePost();
            if (keyboard.f5Key.wasPressedThisFrame) ToggleDepthTexture();
            if (keyboard.f6Key.wasPressedThisFrame) CycleMsaa();
        }

        // ------------------------------------------------------------------- post

        public void CyclePost()
        {
            switch (_post)
            {
                case PostMode.Off:
                    ApplyPost(PostMode.Mobile);
                    break;
                case PostMode.Mobile:
                    ApplyPost(PostMode.Desktop);
                    break;
                default:
                    ApplyPost(PostMode.Off);
                    break;
            }
        }

        public void ApplyPost(PostMode mode)
        {
            _post = mode;

            if (_postVolume != null)
            {
                _postVolume.enabled = mode != PostMode.Off;

                VolumeProfile profile = mode == PostMode.Desktop ? _desktopProfile : _mobileProfile;
                if (profile != null) _postVolume.sharedProfile = profile;
            }

            if (_cameraData != null) _cameraData.renderPostProcessing = mode != PostMode.Off;
        }

        // ------------------------------------------------------- pipeline switches

        static UniversalRenderPipelineAsset Pipeline
        {
            get { return QualitySettings.renderPipeline as UniversalRenderPipelineAsset
                         ?? GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset; }
        }

        public void ToggleDepthTexture()
        {
            UniversalRenderPipelineAsset asset = Pipeline;
            if (asset == null) return;
            asset.supportsCameraDepthTexture = !asset.supportsCameraDepthTexture;
        }

        public void CycleMsaa()
        {
            UniversalRenderPipelineAsset asset = Pipeline;
            if (asset == null) return;

            switch (asset.msaaSampleCount)
            {
                case 1:
                    asset.msaaSampleCount = 2;
                    break;
                case 2:
                    asset.msaaSampleCount = 4;
                    break;
                default:
                    asset.msaaSampleCount = 1;
                    break;
            }
        }

        public bool DepthTextureEnabled
        {
            get
            {
                UniversalRenderPipelineAsset asset = Pipeline;
                return asset != null && asset.supportsCameraDepthTexture;
            }
        }

        public int MsaaSamples
        {
            get
            {
                UniversalRenderPipelineAsset asset = Pipeline;
                return asset != null ? asset.msaaSampleCount : 1;
            }
        }

        public float RenderScale
        {
            get
            {
                UniversalRenderPipelineAsset asset = Pipeline;
                return asset != null ? asset.renderScale : 1f;
            }
        }

        /// <summary>
        /// One line naming every switch, so a screenshot of the overlay is enough to
        /// know which configuration produced the number underneath it.
        /// </summary>
        public string DescribeState()
        {
            _state.Length = 0;
            _state.Append("post ").Append(_post.ToString().ToLowerInvariant());
            _state.Append("   depth ").Append(DepthTextureEnabled ? "on" : "off");
            _state.Append("   msaa ").Append(MsaaSamples).Append('x');
            _state.Append("   scale ").Append(RenderScale.ToString("0.##"));
            return _state.ToString();
        }
    }
}
