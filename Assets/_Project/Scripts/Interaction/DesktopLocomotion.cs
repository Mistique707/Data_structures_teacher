using Pivot.Utils;
using UnityEngine;

namespace Pivot.Interaction
{
    /// <summary>
    /// Walking and looking on desktop.
    ///
    /// Three things here were wrong in the first version and are worth stating, because
    /// each was a wrong model rather than a wrong number:
    ///
    /// - Vertical movement was a free-fly axis, so Space climbed and stayed climbed and
    ///   Ctrl sank into the floor. It is now a crouch and a stretch: an offset from
    ///   standing eye height that springs back the moment the key is released. There is
    ///   no way to end up hovering, because height is not integrated, it is a target.
    /// - The cursor started unlocked and mouse look needed a modifier held. That is not
    ///   what anyone expects on pressing Play. The cursor is captured at start, the mouse
    ///   always looks, Esc gives it back, and a click takes it again.
    /// - Movement wrote straight to the transform, so there was nothing to collide with.
    ///   It goes through a CharacterController now.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(-10)]
    public sealed class DesktopLocomotion : MonoBehaviour
    {
        [Header("Rig")]
        [Tooltip("Yaw is applied to this object; pitch to the head, so the body never tilts.")]
        [SerializeField] Transform _head;

        [Header("Movement")]
        [Tooltip("Metres per second at a walk.")]
        [SerializeField] float _walkSpeed = 3.2f;

        [Tooltip("Multiplier while Sprint is held.")]
        [SerializeField] float _sprintMultiplier = 2f;

        [Tooltip("Seconds to reach full speed. Small, or it feels like ice.")]
        [SerializeField, Range(0.01f, 0.5f)] float _accelerationTime = 0.09f;

        [Tooltip("Seconds to stop. Shorter than acceleration, which reads as planted.")]
        [SerializeField, Range(0.01f, 0.5f)] float _brakingTime = 0.06f;

        [Tooltip("Downward speed applied when unsupported. Keeps the rig on the floor.")]
        [SerializeField] float _gravity = 9.81f;

        [Header("Stance")]
        [Tooltip("Eye height when standing normally.")]
        [SerializeField] float _standingEyeHeight = 1.6f;

        [Tooltip("How far the eyes drop while crouch is held.")]
        [SerializeField] float _crouchDrop = 0.55f;

        [Tooltip("How far the eyes rise while the raise key is held.")]
        [SerializeField] float _stretchRise = 0.28f;

        [Tooltip("Seconds for the stance to settle. This is the spring back.")]
        [SerializeField, Range(0.02f, 0.6f)] float _stanceSettle = 0.12f;

        [Header("Look")]
        [Tooltip("Degrees per mouse count. The Settings slider scales this.")]
        [SerializeField] float _lookSensitivity = 0.12f;

        [SerializeField] float _minPitch = -85f;
        [SerializeField] float _maxPitch = 85f;

        CharacterController _controller;
        Vector3 _velocity;
        float _fallSpeed;
        float _yaw;
        float _pitch;
        float _eyeHeight;
        float _eyeVelocity;

        Vector2? _forcedMove;
        float _forcedLift;

        // Owned, never read back from Cursor.lockState. The OS value is written as a
        // side effect but is not the source of truth: it never reads Locked in batch
        // mode and the Editor Game view can override it at any time.
        bool _captured;
        bool _suppressGrabUntilRelease;

        /// <summary>True while the mouse is steering the view.</summary>
        public bool Looking
        {
            get { return _captured; }
        }

        /// <summary>Whether the cursor is captured. The interactor aims from screen centre when it is.</summary>
        public bool Captured
        {
            get { return _captured; }
        }

        /// <summary>
        /// True from the click that recaptured the cursor until that button is released.
        /// A click spent on recapture must not also pick something up, and the only way
        /// to make that true is for the interactor to ask.
        /// </summary>
        public bool GrabSuppressed
        {
            get { return _suppressGrabUntilRelease; }
        }

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (_head == null) _head = transform;
            _eyeHeight = _standingEyeHeight;
        }

        void Start()
        {
            _yaw = transform.eulerAngles.y;
            _pitch = NormalisePitch(_head.localEulerAngles.x);

            // Captured on Play, because that is what pressing Play into a first person
            // view is expected to do.
            SetCursor(true);
        }

        void OnDisable()
        {
            SetCursor(false);
        }

        void Update()
        {
            PivotActions actions = PivotActions.Instance;
            if (actions == null) return;

            HandleCursor(actions);
            HandleLook(actions);
            HandleStance(actions);
            HandleMove(actions);
        }

        // ----------------------------------------------------------------- cursor

        void HandleCursor(PivotActions actions)
        {
            // Esc is the one key that must never be ambiguous: it always gives the
            // cursor back, whatever else is happening.
            if (actions.Menu != null && actions.Menu.WasPressedThisFrame())
            {
                SetCursor(false);
                return;
            }

            if (actions.Grab == null) return;

            // The suppression lasts exactly as long as the recapturing press.
            if (_suppressGrabUntilRelease && !actions.Grab.IsPressed())
            {
                _suppressGrabUntilRelease = false;
            }

            // Clicking back into the view recaptures it. Grab is on the same button, so
            // the press is flagged and the interactor declines to select on it.
            if (!_captured && actions.Grab.WasPressedThisFrame())
            {
                _suppressGrabUntilRelease = true;
                SetCursor(true);
            }
        }

        void SetCursor(bool locked)
        {
            _captured = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        // ------------------------------------------------------------------- look

        void HandleLook(PivotActions actions)
        {
            if (!Looking || actions.Look == null) return;

            Vector2 delta = actions.Look.ReadValue<Vector2>();
            if (delta.sqrMagnitude < 1e-8f) return;

            float sensitivity = _lookSensitivity * Settings.MouseSensitivity;

            // No deltaTime: mouse delta is already a per-frame displacement, and scaling
            // it by frame time is the usual reason look speed drifts with frame rate.
            _yaw += delta.x * sensitivity;
            _pitch = Mathf.Clamp(_pitch - delta.y * sensitivity, _minPitch, _maxPitch);

            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            _head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        static float NormalisePitch(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }

        // ----------------------------------------------------------------- stance

        /// <summary>
        /// Eye height is a target that is smoothed towards, never an accumulated value.
        /// Release the key and it returns to standing on its own; there is no state to
        /// get stuck in.
        /// </summary>
        void HandleStance(PivotActions actions)
        {
            float lift = _forcedMove.HasValue
                ? _forcedLift
                : (actions.Elevate != null ? actions.Elevate.ReadValue<float>() : 0f);

            float target = _standingEyeHeight;
            if (lift > 0.01f) target += _stretchRise * lift;
            else if (lift < -0.01f) target += _crouchDrop * lift;

            _eyeHeight = Mathf.SmoothDamp(_eyeHeight, target, ref _eyeVelocity, _stanceSettle);

            Vector3 local = _head.localPosition;
            local.y = _eyeHeight;
            _head.localPosition = local;
        }

        // ------------------------------------------------------------------- move

        /// <summary>Feeds movement input directly. Used by tests; ignored in normal play.</summary>
        public void DriveForTest(Vector2 move, float lift)
        {
            _forcedMove = move;
            _forcedLift = lift;
        }

        void HandleMove(PivotActions actions)
        {
            // Moving a disabled controller logs an error rather than doing nothing, and
            // the controller is legitimately off for a frame whenever Frame() teleports.
            if (_controller == null || !_controller.enabled) return;

            Vector2 input = _forcedMove ?? (actions.Move != null
                ? actions.Move.ReadValue<Vector2>()
                : Vector2.zero);

            bool sprinting = actions.Sprint != null && actions.Sprint.IsPressed();
            float speed = _walkSpeed * (sprinting ? _sprintMultiplier : 1f);

            // Movement follows where you are looking horizontally, not where the camera
            // is pitched: looking at the floor should not walk you into it.
            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward.Normalize();

            Vector3 right = transform.right;
            right.y = 0f;
            right.Normalize();

            Vector3 wanted = forward * input.y + right * input.x;
            if (wanted.sqrMagnitude > 1f) wanted.Normalize();
            wanted *= speed;

            float smoothing = wanted.sqrMagnitude > 0.0001f ? _accelerationTime : _brakingTime;
            float rate = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(smoothing, 0.001f));
            _velocity = Vector3.Lerp(_velocity, wanted, rate);

            // Just enough gravity to stay on the floor and walk down a step. The lab is
            // flat, so this is about never floating rather than about falling.
            if (_controller.isGrounded && _fallSpeed < 0f) _fallSpeed = -1f;
            else _fallSpeed -= _gravity * Time.deltaTime;

            Vector3 motion = _velocity;
            motion.y = _fallSpeed;

            _controller.Move(motion * Time.deltaTime);
        }

        /// <summary>
        /// Pulls back and centres on a bounds. Frame Tree uses this so the user can
        /// always recover a sensible view without hunting for it.
        /// </summary>
        public void Frame(Bounds bounds, float fieldOfView)
        {
            float radius = Mathf.Max(bounds.extents.magnitude, 0.25f);
            float distance = radius / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            distance = Mathf.Clamp(distance * 1.3f, 1.2f, 6f);

            Vector3 target = bounds.center;
            Vector3 stand = target + new Vector3(0f, 0f, -1f) * distance;
            stand.y = 0f;

            // CharacterController owns the position, so it has to be disabled for a
            // teleport or it fights the move and lands somewhere else.
            bool wasEnabled = _controller != null && _controller.enabled;
            if (wasEnabled) _controller.enabled = false;
            transform.position = stand;
            if (wasEnabled) _controller.enabled = true;

            Vector3 eye = stand + Vector3.up * _eyeHeight;
            Vector3 toTarget = target - eye;

            _yaw = Quaternion.LookRotation(new Vector3(toTarget.x, 0f, toTarget.z)).eulerAngles.y;
            _pitch = Mathf.Clamp(
                -Mathf.Atan2(toTarget.y, new Vector2(toTarget.x, toTarget.z).magnitude) * Mathf.Rad2Deg,
                _minPitch, _maxPitch);

            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            _head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            _velocity = Vector3.zero;
            _fallSpeed = 0f;
        }

#if UNITY_EDITOR
        public void EditorBind(Transform head)
        {
            _head = head;
        }
#endif
    }
}
