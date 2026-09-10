using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Pivot.Interaction
{
    /// <summary>
    /// A ray interactor aimed by the mouse instead of by a controller.
    ///
    /// It is a thin subclass on purpose. Everything below it — hover, select, the attach
    /// transform, UI — is stock XR Interaction Toolkit, so a grabbable never learns which
    /// rig is driving it. All this does is put its own transform on the ray under the
    /// cursor before the base class reads it, and hand the base class a select signal
    /// that came from the actions asset.
    ///
    /// If gameplay ever needs to know whether the mouse or a controller is driving, that
    /// is a design smell rather than something to branch on. See ARCHITECTURE.md, rule 3.
    /// </summary>
    [AddComponentMenu("Pivot/Mouse Ray Interactor")]
    public sealed class MouseRayInteractor : XRRayInteractor
    {
        [Header("Mouse aiming")]
        [Tooltip("Camera the cursor ray is cast from. Defaults to the rig camera.")]
        [SerializeField] Camera _camera;

        [Tooltip("How far in front of the camera a held object rests, in metres.")]
        [SerializeField] float _defaultHoldDistance = 0.85f;

        [SerializeField] float _minHoldDistance = 0.3f;
        [SerializeField] float _maxHoldDistance = 2.4f;

        [Tooltip("Metres per scroll notch.")]
        [SerializeField] float _pushSpeed = 0.12f;

        float _holdDistance;

        public float HoldDistance
        {
            get { return _holdDistance; }
        }

        protected override void Awake()
        {
            base.Awake();

            if (_camera == null) _camera = GetComponentInParent<Camera>();
            _holdDistance = _defaultHoldDistance;

            // The base interactor reads its select signal from this reader. Driving it
            // manually keeps the binding in the actions asset, where the help overlay
            // reads it and a rebind reaches it, rather than duplicating a key here.
            selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
        }

        public void SetCamera(Camera camera)
        {
            _camera = camera;
        }

        /// <summary>
        /// Runs before the base class computes its ray, which is what makes the mouse the
        /// aim source without touching any of the interaction logic underneath.
        /// </summary>
        public override void PreprocessInteractor(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            if (updatePhase == XRInteractionUpdateOrder.UpdatePhase.Dynamic) AimAtCursor();

            base.PreprocessInteractor(updatePhase);
        }

        void AimAtCursor()
        {
            PivotActions actions = PivotActions.Instance;

            if (actions != null && actions.Grab != null)
            {
                bool performed = actions.Grab.IsPressed();
                selectInput.QueueManualState(performed, performed ? 1f : 0f);
            }

            if (actions != null && actions.Push != null && hasSelection)
            {
                float scroll = actions.Push.ReadValue<float>();
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    _holdDistance = Mathf.Clamp(
                        _holdDistance + Mathf.Sign(scroll) * _pushSpeed,
                        _minHoldDistance, _maxHoldDistance);
                }
            }

            if (_camera == null) return;

            Ray ray = CursorRay();
            transform.SetPositionAndRotation(ray.origin, Quaternion.LookRotation(ray.direction));

            // The attach transform is where a grabbed object sits. Pushing it along the
            // ray is what lets the scroll wheel move a held node nearer and further.
            if (attachTransform != null)
            {
                attachTransform.position = ray.origin + ray.direction * _holdDistance;
            }
        }

        /// <summary>
        /// The ray under the cursor. While the cursor is locked for mouse look it sits at
        /// the centre of the screen, so aiming becomes a crosshair rather than a pointer,
        /// which is what a player expects once the mouse is steering the view.
        /// </summary>
        Ray CursorRay()
        {
            Vector2 screen = Cursor.lockState == CursorLockMode.Locked
                ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                : CursorPosition();

            return _camera.ScreenPointToRay(screen);
        }

        static Vector2 CursorPosition()
        {
            UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
            return mouse != null
                ? mouse.position.ReadValue()
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }

        protected override void OnSelectEntering(SelectEnterEventArgs args)
        {
            base.OnSelectEntering(args);

            // Pick the object up at the distance it already is, so it does not jump
            // towards or away from the camera the instant it is grabbed.
            if (_camera != null && args.interactableObject != null)
            {
                Vector3 toObject = args.interactableObject.transform.position - _camera.transform.position;
                _holdDistance = Mathf.Clamp(toObject.magnitude, _minHoldDistance, _maxHoldDistance);
            }
        }

        protected override void OnSelectExiting(SelectExitEventArgs args)
        {
            base.OnSelectExiting(args);
            _holdDistance = _defaultHoldDistance;
        }
    }
}
