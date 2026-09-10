using Pivot.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pivot.Interaction
{
    /// <summary>
    /// Walking and looking on desktop. Tuned to feel like standing in a room rather than
    /// flying: acceleration and braking are short but not instant, so a tap of W nudges
    /// and a held W settles at a walking pace, and stopping does not skid.
    ///
    /// Mouse look is frame-rate independent by construction. Mouse delta is already a
    /// per-frame displacement, so it must NOT be multiplied by deltaTime — doing that is
    /// the usual reason look speed changes with frame rate.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class DesktopLocomotion : MonoBehaviour
    {
        [Header("Rig")]
        [Tooltip("Yaw is applied here; pitch is applied to the camera so the body never tilts.")]
        [SerializeField] Transform _body;
        [SerializeField] Transform _head;

        [Header("Movement")]
        [Tooltip("Metres per second at a walk.")]
        [SerializeField] float _walkSpeed = 2.6f;

        [Tooltip("Multiplier while Sprint is held.")]
        [SerializeField] float _sprintMultiplier = 2.1f;

        [Tooltip("Vertical metres per second on Space and Ctrl.")]
        [SerializeField] float _elevateSpeed = 1.8f;

        [Tooltip("Seconds to reach full speed. Small, or it feels like ice.")]
        [SerializeField, Range(0.01f, 0.5f)] float _accelerationTime = 0.09f;

        [Tooltip("Seconds to stop. Slightly shorter than acceleration so it feels planted.")]
        [SerializeField, Range(0.01f, 0.5f)] float _brakingTime = 0.06f;

        [Header("Look")]
        [Tooltip("Degrees per mouse count. The Settings slider scales this.")]
        [SerializeField] float _lookSensitivity = 0.12f;

        [SerializeField] float _minPitch = -85f;
        [SerializeField] float _maxPitch = 85f;

        [Header("Bounds")]
        [Tooltip("Keeps the user inside the lab. Generous; it is a nudge, not a cage.")]
        [SerializeField] Vector3 _roomHalfExtents = new Vector3(4.5f, 0f, 4.5f);

        [SerializeField] float _minHeight = 0.6f;
        [SerializeField] float _maxHeight = 2.6f;

        Vector3 _velocity;
        float _yaw;
        float _pitch;
        bool _mouseLookLatched;

        // Test-driven input. Null in normal play, in which case the actions asset is
        // read as usual. Exists so a PlayMode test can prove the movement path works
        // end to end without synthesising device events.
        Vector2? _forcedMove;
        float _forcedLift;

        /// <summary>True while the mouse is actually steering the view.</summary>
        public bool Looking { get; private set; }

        void Start()
        {
            if (_body == null) _body = transform;
            if (_head == null) _head = transform;

            Vector3 angles = _body.eulerAngles;
            _yaw = angles.y;
            _pitch = NormalisePitch(_head.localEulerAngles.x);

            SetCursor(false);
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
            HandleMove(actions);
        }

        // ----------------------------------------------------------------- cursor

        void HandleCursor(PivotActions actions)
        {
            // Two ways in, because both are habits people already have: hold the right
            // button for a quick glance, or latch with Tab for a long look.
            if (actions.ToggleLook != null && actions.ToggleLook.WasPressedThisFrame())
            {
                _mouseLookLatched = !_mouseLookLatched;
                SetCursor(_mouseLookLatched);
            }

            // Esc always gives the cursor back. It is the one key that must never be
            // ambiguous, so it is checked before anything else can claim it.
            if (actions.Menu != null && actions.Menu.WasPressedThisFrame())
            {
                _mouseLookLatched = false;
                SetCursor(false);
            }

            bool holding = actions.HoldLook != null && actions.HoldLook.IsPressed();
            Looking = _mouseLookLatched || holding;

            if (holding && Cursor.lockState != CursorLockMode.Locked) SetCursor(true);
            else if (!Looking && !_mouseLookLatched && Cursor.lockState == CursorLockMode.Locked)
                SetCursor(false);
        }

        static void SetCursor(bool locked)
        {
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

            // No deltaTime here on purpose: mouse delta is already per-frame movement.
            _yaw += delta.x * sensitivity;
            _pitch = Mathf.Clamp(_pitch - delta.y * sensitivity, _minPitch, _maxPitch);

            _body.rotation = Quaternion.Euler(0f, _yaw, 0f);
            _head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        static float NormalisePitch(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
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
            Vector2 input = _forcedMove ?? (actions.Move != null
                ? actions.Move.ReadValue<Vector2>()
                : Vector2.zero);

            float lift = _forcedMove.HasValue
                ? _forcedLift
                : (actions.Elevate != null ? actions.Elevate.ReadValue<float>() : 0f);
            bool sprinting = actions.Sprint != null && actions.Sprint.IsPressed();

            float speed = _walkSpeed * (sprinting ? _sprintMultiplier : 1f);

            // Movement follows where you are looking horizontally, not where the camera
            // is pitched: looking at the floor should not walk you into it.
            Vector3 forward = _body.forward;
            forward.y = 0f;
            forward.Normalize();

            Vector3 right = _body.right;
            right.y = 0f;
            right.Normalize();

            Vector3 wanted = (forward * input.y + right * input.x);
            if (wanted.sqrMagnitude > 1f) wanted.Normalize();
            wanted *= speed;
            wanted.y = lift * _elevateSpeed;

            // Braking is quicker than acceleration, which is what reads as "planted"
            // rather than floaty. Both are short enough to feel direct.
            float smoothing = wanted.sqrMagnitude > 0.0001f ? _accelerationTime : _brakingTime;
            float rate = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(smoothing, 0.001f));
            _velocity = Vector3.Lerp(_velocity, wanted, rate);

            if (_velocity.sqrMagnitude < 1e-6f) return;

            Vector3 position = _body.position + _velocity * Time.deltaTime;

            position.x = Mathf.Clamp(position.x, -_roomHalfExtents.x, _roomHalfExtents.x);
            position.z = Mathf.Clamp(position.z, -_roomHalfExtents.z, _roomHalfExtents.z);
            position.y = Mathf.Clamp(position.y, _minHeight, _maxHeight);

            _body.position = position;
        }

        /// <summary>
        /// Pulls back and centres on a bounds. Used by Frame Tree so the user can always
        /// recover a sensible view without hunting for it.
        /// </summary>
        public void Frame(Bounds bounds, float fieldOfView)
        {
            float radius = Mathf.Max(bounds.extents.magnitude, 0.25f);
            float distance = radius / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            distance = Mathf.Clamp(distance * 1.25f, 1.1f, 6f);

            Vector3 target = bounds.center;
            Vector3 direction = new Vector3(0f, 0f, -1f);

            Vector3 position = target + direction * distance;
            position.y = Mathf.Clamp(target.y + 0.12f, _minHeight, _maxHeight);

            _body.position = position;

            Vector3 toTarget = target - (_body.position + Vector3.up * (_head.localPosition.y));
            _yaw = Quaternion.LookRotation(new Vector3(toTarget.x, 0f, toTarget.z)).eulerAngles.y;
            _pitch = Mathf.Clamp(
                -Mathf.Atan2(toTarget.y, new Vector2(toTarget.x, toTarget.z).magnitude) * Mathf.Rad2Deg,
                _minPitch, _maxPitch);

            _body.rotation = Quaternion.Euler(0f, _yaw, 0f);
            _head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            _velocity = Vector3.zero;
        }
    }
}
