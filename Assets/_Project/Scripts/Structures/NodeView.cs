using Pivot.Utils;
using TMPro;
using UnityEngine;

namespace Pivot.Structures
{
    /// <summary>
    /// One bubble. Owns nothing about the tree beyond the id it is currently standing
    /// for, so the reconciler is free to hand it a different node at any time — which
    /// is what lets a pool of these survive inserts, deletes and undo without ever
    /// respawning and losing its animation state.
    ///
    /// Colour and highlight go through a MaterialPropertyBlock into the shader's
    /// instancing buffer, so every node in the tree shares one material and one draw.
    /// </summary>
    /// <remarks>
    /// ExecuteAlways because a MaterialPropertyBlock is runtime state on the Renderer
    /// and is never serialised into a scene. Without this, an authored scene would
    /// reload with every node showing the material's fallback colour, and the depth
    /// coding would only appear once you pressed Play. The colour is therefore stored
    /// in a serialised field and pushed back into the block whenever the object wakes,
    /// in the Editor as well as at run time.
    /// </remarks>
    [ExecuteAlways]
    public sealed class NodeView : MonoBehaviour
    {
        [Header("Parts")]
        [SerializeField] Transform _body;
        [SerializeField] MeshRenderer _renderer;
        [SerializeField] TextMeshPro _label;
        [SerializeField] Transform _labelPivot;

        [Header("Authored state")]
        [Tooltip("Model id this view stands for. Serialised so the authored scene can be " +
                 "reconciled against a model rather than rebuilt.")]
        [SerializeField] int _nodeId;

        [Tooltip("Serialised so the authored scene keeps its depth colour without Play.")]
        [SerializeField] Color _colour = Color.white;

        static readonly int InstanceColour = Shader.PropertyToID("_InstanceColour");
        static readonly int InstanceHighlight = Shader.PropertyToID("_InstanceHighlight");

        MaterialPropertyBlock _block;
        Transform _transform;

        Vector3 _basePosition;
        Vector3 _baseScale = Vector3.one;
        bool _baseScaleCaptured;
        Color _highlight;
        float _highlightAmount;
        float _bobPhase;
        int _value = int.MinValue;

        public int NodeId
        {
            get { return _nodeId; }
        }

        public bool InUse { get; private set; }

        /// <summary>
        /// While true, idle bob and breathing leave this node alone. Set while it is
        /// being carried, so the grab tweens own the scale channel without the idle
        /// writing over them every frame.
        /// </summary>
        public bool IdleSuspended { get; set; }

        public Transform Body
        {
            get { return _body != null ? _body : _transform; }
        }

        public Vector3 BasePosition
        {
            get { return _basePosition; }
        }

        /// <summary>The colour saved with the scene, before any runtime highlight.</summary>
        public Color AuthoredColour
        {
            get { return _colour; }
        }

        void OnEnable()
        {
            EnsureReady();
            PushBlock();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            EnsureReady();
            PushBlock();
        }
#endif

        void Awake()
        {
            EnsureReady();

            // Desynchronised so a tree full of bubbles breathes as a crowd rather than
            // pulsing in lockstep, which reads as a glitch.
            _bobPhase = Random.value * Mathf.PI * 2f;
        }

        /// <summary>
        /// Awake does not run when a prefab is instantiated in edit mode, and the scene
        /// authoring tools drive these views directly, so state is created on demand
        /// rather than assumed.
        /// </summary>
        void EnsureReady()
        {
            if (_transform == null) _transform = transform;
            if (_block == null) _block = new MaterialPropertyBlock();

            // The body carries the authored size of the mesh, so that is the scale
            // everything animates around. Assuming one here inflated every node.
            if (!_baseScaleCaptured && _body != null)
            {
                _baseScale = _body.localScale;
                _baseScaleCaptured = true;
            }
        }

        /// <summary>The body's authored scale, which idle and grab animate around.</summary>
        public Vector3 RestScale
        {
            get
            {
                EnsureReady();
                return _baseScale;
            }
        }

        public void Acquire(int nodeId, int value, Vector3 position, Color colour, ThemeSO theme)
        {
            _nodeId = nodeId;
            InUse = true;

            EnsureReady();
            _basePosition = position;
            _transform.localPosition = position;

            SetValue(value);
            SetColour(colour);
            SetHighlight(theme != null ? theme.Held : Color.white, 0f);

            gameObject.SetActive(true);
        }

        public void Release()
        {
            InUse = false;
            _nodeId = 0;
            _value = int.MinValue;
            gameObject.SetActive(false);
        }

        public void SetValue(int value)
        {
            if (_value == value) return;
            _value = value;

            // Only touches the mesh when the number genuinely changed. A reconcile that
            // moves nodes around should never rebuild text it did not alter.
            if (_label != null) _label.SetText("{0}", value);
        }

        public void SetColour(Color colour)
        {
            _colour = colour;
            PushBlock();
        }

        public void SetHighlight(Color tint, float amount)
        {
            _highlight = tint;
            _highlightAmount = Mathf.Clamp01(amount);
            PushBlock();
        }

        void PushBlock()
        {
            if (_renderer == null) return;
            EnsureReady();

            _renderer.GetPropertyBlock(_block);

            // Alpha carries "a block was written" for the colour, and the highlight
            // strength for the tint, which keeps both inside one instanced vector.
            _block.SetColor(InstanceColour, new Color(_colour.r, _colour.g, _colour.b, 1f));
            _block.SetColor(InstanceHighlight,
                new Color(_highlight.r, _highlight.g, _highlight.b, _highlightAmount));

            _renderer.SetPropertyBlock(_block);
        }

        /// <summary>Where the reconciler wants this node to sit once it has finished moving.</summary>
        public void SetBasePosition(Vector3 position)
        {
            _basePosition = position;
        }

        public void SetLocalPosition(Vector3 position)
        {
            EnsureReady();
            _transform.localPosition = position;
        }

        public void SetBaseScale(Vector3 scale)
        {
            _baseScale = scale;
            _baseScaleCaptured = true;
        }

        /// <summary>
        /// Idle motion, driven from the one tween runner rather than an Update on every
        /// node. Applied on top of whatever the layout and any active tween decided, so
        /// it never fights them.
        /// </summary>
        public void ApplyIdle(float time, ThemeSO theme)
        {
            if (theme == null || _body == null || IdleSuspended) return;

            float bob = Mathf.Sin(time * theme.IdleBobSpeed + _bobPhase) * theme.IdleBobAmplitude;
            float breathe = 1f + Mathf.Sin(time * theme.BreatheSpeed + _bobPhase) * theme.BreatheAmplitude;

            _body.localPosition = new Vector3(0f, bob, 0f);
            _body.localScale = _baseScale * breathe;
        }

        /// <summary>Turn the label to the viewer. Called once per frame by the view, for all nodes.</summary>
        public void FaceLabel(Vector3 viewerPosition, float pushDistance)
        {
            if (_labelPivot == null) return;
            EnsureReady();

            Vector3 anchor = _transform.position;
            Vector3 toViewer = viewerPosition - anchor;
            if (toViewer.sqrMagnitude < 1e-6f) return;

            toViewer.Normalize();
            _labelPivot.position = anchor + toViewer * pushDistance;
            _labelPivot.rotation = Quaternion.LookRotation(-toViewer, Vector3.up);
        }

#if UNITY_EDITOR
        /// <summary>Used by the authoring tool so a prefab can be wired without hand-dragging.</summary>
        public void EditorBind(Transform body, MeshRenderer renderer, TextMeshPro label, Transform labelPivot)
        {
            _body = body;
            _renderer = renderer;
            _label = label;
            _labelPivot = labelPivot;
        }
#endif
    }
}
