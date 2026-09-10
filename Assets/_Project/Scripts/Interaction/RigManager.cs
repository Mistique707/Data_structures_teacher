using Pivot.Structures;
using Pivot.Utils;
using Pivot.VFX;
using UnityEngine;
using UnityEngine.XR.Management;

namespace Pivot.Interaction
{
    /// <summary>
    /// Decides once, at boot, whether this session is VR or desktop, enables exactly one
    /// rig, and tells everything that needs a camera which camera won.
    ///
    /// After this runs, nothing else in the project asks which mode is active. Grabbables
    /// are plain XRGrabInteractable and both rigs drive them through the same interactor
    /// protocol, so there is no branch to write. See ARCHITECTURE.md, rule 3.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class RigManager : MonoBehaviour
    {
        [Header("Rigs")]
        [SerializeField] GameObject _vrRig;
        [SerializeField] GameObject _desktopRig;

        [Header("Config")]
        [Tooltip("Optional override. Auto follows whether an XR loader actually started.")]
        [SerializeField] StartupConfig _config;

        [Header("Consumers")]
        [SerializeField] EnvironmentController _environment;
        [SerializeField] TreeView _treeView;

        static RigManager _instance;

        public static RigManager Instance
        {
            get { return _instance; }
        }

        public ActiveRig Mode { get; private set; }

        public Camera ActiveCamera { get; private set; }

        /// <summary>The transform labels turn to face and the tree frames against.</summary>
        public Transform Viewer { get; private set; }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            Mode = Choose();
            Activate(Mode);
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>
        /// An XR loader that actually started is the only reliable evidence that a
        /// headset is present. A build with OpenXR installed but no device connected
        /// leaves activeLoader null, which is exactly the desktop case.
        /// </summary>
        ActiveRig Choose()
        {
            if (_config != null && _config.Rig != RigMode.Auto)
            {
                ActiveRig forced = _config.Rig == RigMode.ForceVr ? ActiveRig.VR : ActiveRig.Desktop;
                Debug.Log("[Pivot] Rig: " + forced + " (forced by StartupConfig).");
                return forced;
            }

            bool xrRunning = XRGeneralSettings.Instance != null &&
                             XRGeneralSettings.Instance.Manager != null &&
                             XRGeneralSettings.Instance.Manager.activeLoader != null;

            ActiveRig chosen = xrRunning ? ActiveRig.VR : ActiveRig.Desktop;

            Debug.Log("[Pivot] Rig: " + chosen + " (auto; XR loader " +
                      (xrRunning ? "active" : "not active") + ").");

            return chosen;
        }

        void Activate(ActiveRig mode)
        {
            bool vr = mode == ActiveRig.VR;

            if (_vrRig != null) _vrRig.SetActive(vr);
            if (_desktopRig != null) _desktopRig.SetActive(!vr);

            GameObject active = vr ? _vrRig : _desktopRig;
            if (active == null)
            {
                Debug.LogError("[Pivot] Rig: " + mode + " selected but no rig is assigned.");
                return;
            }

            ActiveCamera = active.GetComponentInChildren<Camera>(true);
            Viewer = ActiveCamera != null ? ActiveCamera.transform : active.transform;

            // Push the camera to everything that needs one, so nothing has to reach for
            // Camera.main at runtime.
            if (_environment != null) _environment.Bind(ActiveCamera);
            if (_treeView != null) _treeView.SetViewer(Viewer);

            MouseRayInteractor mouse = active.GetComponentInChildren<MouseRayInteractor>(true);
            if (mouse != null) mouse.SetCamera(ActiveCamera);
        }

#if UNITY_EDITOR
        public void EditorBind(GameObject vrRig, GameObject desktopRig, StartupConfig config,
            EnvironmentController environment, TreeView treeView)
        {
            _vrRig = vrRig;
            _desktopRig = desktopRig;
            _config = config;
            _environment = environment;
            _treeView = treeView;
        }
#endif
    }
}
