using UnityEngine;

namespace Pivot.Structures
{
    /// <summary>
    /// One rubbery tube between a parent and a child. Rebuilt every frame the layout is
    /// moving, which is cheap: it is a transform set and a property block, no mesh work.
    ///
    /// The tube is a unit cylinder running -1..1 in object Y, so the stretch is a scale
    /// and the shader can read the fraction along the edge straight from the position.
    /// </summary>
    /// <remarks>
    /// ExecuteAlways for the same reason as NodeView: property blocks are not
    /// serialised, so the authored gradient has to be restored when the object wakes.
    /// </remarks>
    [ExecuteAlways]
    public sealed class EdgeView : MonoBehaviour
    {
        [SerializeField] MeshRenderer _renderer;

        [Tooltip("Thickness in metres. Edges are tubes, not lines.")]
        [SerializeField] float _radius = 0.028f;

        [Tooltip("How far short of each node centre the tube stops, as a fraction of " +
                 "node radius. Keeps the tube from poking out of the far side of a bubble.")]
        [SerializeField, Range(0f, 1f)] float _inset = 0.62f;

        static readonly int InstanceColourA = Shader.PropertyToID("_InstanceColourA");
        static readonly int InstanceColourB = Shader.PropertyToID("_InstanceColourB");
        static readonly int InstancePulse = Shader.PropertyToID("_InstancePulse");
        static readonly int InstancePulsePos = Shader.PropertyToID("_InstancePulsePos");

        [Header("Authored state")]
        [Tooltip("The two node ids this edge spans. Serialised for the same reason as " +
                 "NodeView.NodeId: the saved scene has to be reconcilable.")]
        [SerializeField] int _parentId;
        [SerializeField] int _childId;

        [Tooltip("Serialised so the authored scene keeps its gradient without Play.")]
        [SerializeField] Color _colourA = Color.white;
        [SerializeField] Color _colourB = Color.white;

        MaterialPropertyBlock _block;
        Transform _transform;

        float _pulsePosition = -0.5f;
        float _pulseStrength;

        public int ParentId
        {
            get { return _parentId; }
        }

        public int ChildId
        {
            get { return _childId; }
        }

        public bool InUse { get; private set; }

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
        }

        /// <summary>Same reason as NodeView: edit-mode instantiation never calls Awake.</summary>
        void EnsureReady()
        {
            if (_transform == null) _transform = transform;
            if (_block == null) _block = new MaterialPropertyBlock();
        }

        public void Acquire(int parentId, int childId)
        {
            EnsureReady();

            _parentId = parentId;
            _childId = childId;
            InUse = true;
            SetPulse(-0.5f, 0f);
            gameObject.SetActive(true);
        }

        public void Release()
        {
            InUse = false;
            _parentId = 0;
            _childId = 0;
            gameObject.SetActive(false);
        }

        /// <summary>Stretch and orient the tube between two node centres.</summary>
        public void Span(Vector3 from, Vector3 to, float nodeRadius)
        {
            EnsureReady();

            Vector3 delta = to - from;
            float distance = delta.magnitude;

            if (distance < 1e-4f)
            {
                _transform.localPosition = from;
                _transform.localScale = new Vector3(_radius * 2f, 0.0001f, _radius * 2f);
                return;
            }

            Vector3 direction = delta / distance;
            float trim = nodeRadius * _inset;

            Vector3 start = from + direction * trim;
            Vector3 end = to - direction * trim;
            float span = Mathf.Max((end - start).magnitude, 0.0001f);

            _transform.localPosition = (start + end) * 0.5f;
            _transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction);

            // Unit cylinder is two units tall, hence the half.
            _transform.localScale = new Vector3(_radius * 2f, span * 0.5f, _radius * 2f);
        }

        public void SetColours(Color parentEnd, Color childEnd)
        {
            _colourA = parentEnd;
            _colourB = childEnd;
            PushBlock();
        }

        /// <summary>
        /// Position runs 0 at the parent to 1 at the child. Strength zero parks the
        /// pulse and costs the shader nothing beyond one smoothstep.
        /// </summary>
        public void SetPulse(float position, float strength)
        {
            _pulsePosition = position;
            _pulseStrength = strength;
            PushBlock();
        }

        void PushBlock()
        {
            if (_renderer == null) return;
            EnsureReady();

            _renderer.GetPropertyBlock(_block);
            _block.SetColor(InstanceColourA, new Color(_colourA.r, _colourA.g, _colourA.b, 1f));
            _block.SetColor(InstanceColourB, new Color(_colourB.r, _colourB.g, _colourB.b, 1f));
            _block.SetVector(InstancePulse, new Vector4(0f, 0f, 0f, _pulseStrength));
            _block.SetVector(InstancePulsePos, new Vector4(_pulsePosition, 0f, 0f, 0f));
            _renderer.SetPropertyBlock(_block);
        }

#if UNITY_EDITOR
        public void EditorBind(MeshRenderer renderer, float radius, float inset)
        {
            _renderer = renderer;
            _radius = radius;
            _inset = inset;
        }
#endif
    }
}
