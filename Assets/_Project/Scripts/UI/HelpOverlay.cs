using System.Text;
using Pivot.Interaction;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pivot.UI
{
    /// <summary>
    /// The controls list, toggled with Help (F1).
    ///
    /// Every key shown is read back out of the actions asset with
    /// <see cref="PivotActions.Describe"/> rather than typed here. That is the whole
    /// point: the overlay cannot drift from what the keys actually do, and it follows a
    /// rebind without anyone remembering to update a string.
    /// </summary>
    public sealed class HelpOverlay : MonoBehaviour
    {
        [SerializeField] bool _visibleAtStart;

        readonly StringBuilder _text = new StringBuilder(640);

        GUIStyle _style;
        GUIStyle _titleStyle;
        bool _visible;
        bool _dirty = true;

        public bool IsVisible
        {
            get { return _visible; }
        }

        /// <summary>The rendered rows, so a test can check what the overlay claims.</summary>
        public string CurrentText
        {
            get
            {
                PivotActions actions = PivotActions.Instance;
                if (actions != null && _dirty) Rebuild(actions);
                return _text.ToString();
            }
        }

        void Awake()
        {
            _visible = _visibleAtStart;
        }

        void Update()
        {
            PivotActions actions = PivotActions.Instance;
            if (actions == null || actions.Help == null) return;

            if (actions.Help.WasPressedThisFrame())
            {
                _visible = !_visible;
                _dirty = true;
            }
        }

        void Row(string label, InputAction action)
        {
            _text.Append(PivotActions.Describe(action).PadRight(22)).Append(label).Append('\n');
        }

        void Rebuild(PivotActions actions)
        {
            _text.Length = 0;

            Row("Move", actions.Move);
            Row("Up / down", actions.Elevate);
            Row("Run", actions.Sprint);
            Row("Look (hold)", actions.HoldLook);
            Row("Look (toggle)", actions.ToggleLook);
            Row("Grab / release", actions.Grab);
            Row("Push / pull held", actions.Push);
            // RotateHeld (Q/E) is bound and reserved for the BST rotation, which is not
            // built yet. It is deliberately not listed until something consumes it: a
            // help screen that names a key that does nothing is worse than a gap.
            Row("Frame the tree", actions.FrameTree);
            Row("Step animation", actions.Step);
            Row("Release cursor", actions.Menu);
            Row("This list", actions.Help);

            _dirty = false;
        }

        void OnGUI()
        {
            PivotActions actions = PivotActions.Instance;
            if (actions == null) return;

            if (!_visible)
            {
                DrawHint(actions);
                return;
            }

            if (_dirty) Rebuild(actions);
            EnsureStyles();

            const float width = 340f;
            const float height = 250f;
            Rect box = new Rect(Screen.width - width - 14f, 14f, width, height);

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = previous;

            GUI.Label(new Rect(box.x + 14f, box.y + 10f, box.width - 28f, 22f), "Controls", _titleStyle);
            GUI.Label(new Rect(box.x + 14f, box.y + 36f, box.width - 28f, box.height - 46f),
                _text.ToString(), _style);
        }

        /// <summary>A single line, so a first-time player can find the list at all.</summary>
        void DrawHint(PivotActions actions)
        {
            EnsureStyles();

            string hint = PivotActions.Describe(actions.Help) + "  controls";
            Vector2 size = _style.CalcSize(new GUIContent(hint));
            Rect box = new Rect(Screen.width - size.x - 26f, 14f, size.x + 16f, size.y + 8f);

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = previous;

            GUI.Label(new Rect(box.x + 8f, box.y + 4f, box.width, box.height), hint, _style);
        }

        void EnsureStyles()
        {
            if (_style != null) return;

            _style = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = false };
            _style.normal.textColor = Color.white;

            _titleStyle = new GUIStyle(_style) { fontSize = 15, fontStyle = FontStyle.Bold };
        }
    }
}
