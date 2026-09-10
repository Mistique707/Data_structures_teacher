using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pivot.Interaction
{
    /// <summary>
    /// The one place anything reads player input from. Actions are resolved by name from
    /// the asset once, so gameplay never touches a key directly and rebinding works
    /// without a single call site changing.
    ///
    /// Debug tools deliberately do not go through here — they read Keyboard.current, so
    /// their keys never appear in the rebinding UI and can never collide with a binding
    /// the user chose. See ARCHITECTURE.md, rule 2.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class PivotActions : MonoBehaviour
    {
        [SerializeField] InputActionAsset _asset;

        [Tooltip("Action map enabled at boot.")]
        [SerializeField] string _mapName = "Desktop";

        static PivotActions _instance;

        readonly Dictionary<string, InputAction> _byName =
            new Dictionary<string, InputAction>(StringComparer.Ordinal);

        InputActionMap _map;

        public static PivotActions Instance
        {
            get { return _instance; }
        }

        public InputActionAsset Asset
        {
            get { return _asset; }
        }

        public InputAction Move { get; private set; }
        public InputAction Look { get; private set; }
        public InputAction Elevate { get; private set; }
        public InputAction Sprint { get; private set; }
        public InputAction HoldLook { get; private set; }
        public InputAction ToggleLook { get; private set; }
        public InputAction Grab { get; private set; }
        public InputAction Push { get; private set; }
        public InputAction RotateHeld { get; private set; }
        public InputAction FrameTree { get; private set; }
        public InputAction Step { get; private set; }
        public InputAction Menu { get; private set; }
        public InputAction Help { get; private set; }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;

            if (_asset == null)
            {
                Debug.LogError("PivotActions: no Input Actions asset assigned.");
                return;
            }

            _map = _asset.FindActionMap(_mapName, false);
            if (_map == null)
            {
                Debug.LogError("PivotActions: no action map named '" + _mapName + "'.");
                return;
            }

            for (int i = 0; i < _map.actions.Count; i++)
            {
                _byName[_map.actions[i].name] = _map.actions[i];
            }

            Move = Find("Move");
            Look = Find("Look");
            Elevate = Find("Elevate");
            Sprint = Find("Sprint");
            HoldLook = Find("HoldLook");
            ToggleLook = Find("ToggleLook");
            Grab = Find("Grab");
            Push = Find("Push");
            RotateHeld = Find("RotateHeld");
            FrameTree = Find("FrameTree");
            Step = Find("Step");
            Menu = Find("Menu");
            Help = Find("Help");
        }

        void OnEnable()
        {
            if (_map != null) _map.Enable();
        }

        void OnDisable()
        {
            if (_map != null) _map.Disable();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        InputAction Find(string name)
        {
            InputAction action;
            if (_byName.TryGetValue(name, out action)) return action;

            Debug.LogError("PivotActions: '" + _mapName + "' has no action named '" + name + "'.");
            return null;
        }

        public InputAction ByName(string name)
        {
            InputAction action;
            return _byName.TryGetValue(name, out action) ? action : null;
        }

        /// <summary>
        /// What the action is currently bound to, in words. The help overlay shows this
        /// rather than a hardcoded string, so it cannot drift from what the keys do, and
        /// it follows a rebind for free.
        /// </summary>
        public static string Describe(InputAction action)
        {
            if (action == null) return "-";

            string display = action.GetBindingDisplayString(
                InputBinding.DisplayStringOptions.DontUseShortDisplayNames);

            return string.IsNullOrEmpty(display) ? "-" : display;
        }
    }
}
