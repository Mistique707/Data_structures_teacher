using System.Collections.Generic;
using Pivot.Core;
using Pivot.Utils;
using UnityEngine;

namespace Pivot.Structures
{
    /// <summary>
    /// Reconciles what is on screen against the model. It is never told what changed —
    /// it works that out by comparing the ids the model reports with the ids it is
    /// currently showing, which means one code path serves insert, delete, rotate,
    /// reparent, undo, redo and reset alike.
    ///
    /// Nodes are matched by id and never teleport: a node that already exists tweens to
    /// its new place, a node that has appeared pops in at its parent's position and
    /// travels out, and a node that has gone is popped rather than switched off. Node
    /// ids survive undo because commands restore snapshots that preserve them, so an
    /// undo animates backwards instead of rebuilding the tree.
    /// </summary>
    public sealed class TreeView : MonoBehaviour, IStructureView
    {
        [Header("Bindings")]
        [SerializeField] ThemeSO _theme;
        [SerializeField] NodeView _nodePrefab;
        [SerializeField] EdgeView _edgePrefab;
        [SerializeField] Transform _nodeRoot;
        [SerializeField] Transform _edgeRoot;

        [Header("Layout")]
        [Tooltip("World metres per layout unit. Matched to node diameter so siblings " +
                 "sit just clear of each other.")]
        [SerializeField] float _layoutScale = 0.28f;

        [SerializeField] float _nodeDiameter = 0.28f;

        [Tooltip("Label billboard push, as a fraction of node diameter.")]
        [SerializeField, Range(0.4f, 1f)] float _labelPush = 0.55f;

        [Header("Pooling")]
        [SerializeField] int _prewarmNodes = 24;
        [SerializeField] int _prewarmEdges = 24;

        ComponentPool<NodeView> _nodePool;
        ComponentPool<EdgeView> _edgePool;

        readonly Dictionary<int, NodeView> _nodesById = new Dictionary<int, NodeView>(64);
        readonly Dictionary<long, EdgeView> _edgesByPair = new Dictionary<long, EdgeView>(64);

        // Scratch, reused every reconcile so a steady state allocates nothing.
        readonly List<int> _modelIds = new List<int>(64);
        readonly List<int> _retired = new List<int>(16);
        readonly List<long> _retiredEdges = new List<long>(16);
        readonly Dictionary<int, LayoutPoint> _layout = new Dictionary<int, LayoutPoint>(64);

        Transform _viewer;
        float _idleTime;

        public IReadOnlyDictionary<int, LayoutPoint> Layout
        {
            get { return _layout; }
        }

        public float NodeRadius
        {
            get { return _nodeDiameter * 0.5f; }
        }

        void Awake()
        {
            if (_nodeRoot == null) _nodeRoot = transform;
            if (_edgeRoot == null) _edgeRoot = transform;

            _nodePool = new ComponentPool<NodeView>(_nodePrefab, _nodeRoot, _prewarmNodes);
            _edgePool = new ComponentPool<EdgeView>(_edgePrefab, _edgeRoot, _prewarmEdges);

            AdoptAuthoredScene();
        }

        /// <summary>
        /// Takes ownership of node and edge views that were authored into the scene
        /// rather than spawned from the pool.
        ///
        /// The seed tree exists as real GameObjects saved in the scene, which is the
        /// point, but that means this view has never seen them. Registering them by
        /// their serialised ids is what lets edges follow their nodes from the first
        /// frame, and is the same registry a live model will reconcile against later.
        /// </summary>
        public void AdoptAuthoredScene()
        {
            NodeView[] nodes = _nodeRoot.GetComponentsInChildren<NodeView>(true);
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].NodeId == 0) continue;
                _nodesById[nodes[i].NodeId] = nodes[i];
            }

            EdgeView[] edges = _edgeRoot.GetComponentsInChildren<EdgeView>(true);
            for (int i = 0; i < edges.Length; i++)
            {
                if (edges[i].ParentId == 0 || edges[i].ChildId == 0) continue;
                _edgesByPair[PairKey(edges[i].ParentId, edges[i].ChildId)] = edges[i];
            }

            if (nodes.Length > 0 || edges.Length > 0)
            {
                Debug.Log("[Pivot] TreeView adopted " + _nodesById.Count + " authored node(s) and " +
                          _edgesByPair.Count + " edge(s).");
            }
        }

        /// <summary>The transform labels turn to face. Set by the rig once it knows which one won.</summary>
        public void SetViewer(Transform viewer)
        {
            _viewer = viewer;
        }

        public Vector3 WorldPosition(LayoutPoint point)
        {
            return new Vector3(point.X * _layoutScale, -point.Y * _layoutScale, 0f);
        }

        public bool TryGetNode(int nodeId, out NodeView view)
        {
            return _nodesById.TryGetValue(nodeId, out view);
        }

        // ------------------------------------------------------------- reconcile

        public void Reconcile(IStructureModel model, IReadOnlyDictionary<int, LayoutPoint> layout)
        {
            if (model == null) return;

            CopyLayout(layout);

            model.CollectIds(_modelIds);

            SyncNodes(model);
            SyncEdges(model);
        }

        void CopyLayout(IReadOnlyDictionary<int, LayoutPoint> layout)
        {
            if (ReferenceEquals(layout, _layout)) return;

            _layout.Clear();
            if (layout == null) return;

            foreach (KeyValuePair<int, LayoutPoint> pair in layout) _layout[pair.Key] = pair.Value;
        }

        void SyncNodes(IStructureModel model)
        {
            float duration = _theme != null ? _theme.ReflowDuration : 0.4f;
            AnimationCurve curve = _theme != null ? _theme.Reflow : null;

            for (int i = 0; i < _modelIds.Count; i++)
            {
                int id = _modelIds[i];

                LayoutPoint point;
                if (!_layout.TryGetValue(id, out point)) continue;

                Vector3 target = WorldPosition(point);
                int depth = DepthOf(model, id);
                Color colour = _theme != null ? _theme.ColourForDepth(depth) : Color.cyan;

                NodeView view;
                if (_nodesById.TryGetValue(id, out view))
                {
                    // Already on screen: move it, never jump it.
                    view.SetValue(model.ValueOf(id));
                    view.SetColour(colour);
                    view.SetBasePosition(target);

                    if ((view.transform.localPosition - target).sqrMagnitude > 1e-6f)
                    {
                        TweenRunner.Instance.Move(view.transform, target, duration, curve);
                    }

                    continue;
                }

                // New: born at its parent so it visibly travels out to its slot rather
                // than fading in at a position the user never saw it reach.
                view = _nodePool.Rent();
                Vector3 birth = target;

                int parentId = model.ParentOf(id);
                LayoutPoint parentPoint;
                if (parentId != 0 && _layout.TryGetValue(parentId, out parentPoint))
                {
                    birth = WorldPosition(parentPoint);
                }

                view.Acquire(id, model.ValueOf(id), birth, colour, _theme);
                view.SetBaseScale(Vector3.one);
                view.SetBasePosition(target);
                _nodesById[id] = view;

                TweenRunner.Instance.Move(view.transform, target, duration, curve);

                if (_theme != null)
                {
                    TweenRunner.Instance.Punch(view.Body, Vector3.one, _theme.SpawnPunch,
                        _theme.PopDuration, _theme.Pop);
                }
            }

            // Anything still on screen that the model no longer has.
            _retired.Clear();
            foreach (KeyValuePair<int, NodeView> pair in _nodesById)
            {
                if (!ContainsId(pair.Key)) _retired.Add(pair.Key);
            }

            for (int i = 0; i < _retired.Count; i++)
            {
                NodeView view = _nodesById[_retired[i]];
                _nodesById.Remove(_retired[i]);
                TweenRunner.Instance.CancelAll(view.transform);
                view.Release();
                _nodePool.Return(view);
            }
        }

        bool ContainsId(int id)
        {
            for (int i = 0; i < _modelIds.Count; i++)
            {
                if (_modelIds[i] == id) return true;
            }

            return false;
        }

        static int DepthOf(IStructureModel model, int id)
        {
            int depth = 0;
            int cursor = model.ParentOf(id);
            while (cursor != 0 && depth < 64)
            {
                depth++;
                cursor = model.ParentOf(cursor);
            }

            return depth;
        }

        static long PairKey(int parentId, int childId)
        {
            return ((long)parentId << 32) | (uint)childId;
        }

        void SyncEdges(IStructureModel model)
        {
            // Mark every edge the model currently wants, creating what is missing.
            for (int i = 0; i < _modelIds.Count; i++)
            {
                int parentId = _modelIds[i];
                TouchEdge(model, parentId, model.ChildOf(parentId, ChildSide.Left));
                TouchEdge(model, parentId, model.ChildOf(parentId, ChildSide.Right));
            }

            _retiredEdges.Clear();
            foreach (KeyValuePair<long, EdgeView> pair in _edgesByPair)
            {
                EdgeView edge = pair.Value;
                if (model.ParentOf(edge.ChildId) == edge.ParentId && edge.ParentId != 0) continue;
                _retiredEdges.Add(pair.Key);
            }

            for (int i = 0; i < _retiredEdges.Count; i++)
            {
                EdgeView edge = _edgesByPair[_retiredEdges[i]];
                _edgesByPair.Remove(_retiredEdges[i]);
                edge.Release();
                _edgePool.Return(edge);
            }
        }

        void TouchEdge(IStructureModel model, int parentId, int childId)
        {
            if (childId == 0) return;

            long key = PairKey(parentId, childId);
            EdgeView edge;

            if (!_edgesByPair.TryGetValue(key, out edge))
            {
                edge = _edgePool.Rent();
                edge.Acquire(parentId, childId);
                _edgesByPair[key] = edge;
            }

            if (_theme != null)
            {
                edge.SetColours(_theme.ColourForDepth(DepthOf(model, parentId)),
                                _theme.ColourForDepth(DepthOf(model, childId)));
            }
        }

        // ------------------------------------------------------------ per frame

        /// <summary>
        /// Edges follow the nodes rather than being told where to go, so they stay
        /// attached through every tween without anything having to coordinate them.
        /// One loop for all edges, one for all labels: no Update on any node or edge.
        /// </summary>
        void LateUpdate()
        {
            _idleTime += Time.deltaTime;

            bool hasViewer = _viewer != null;
            Vector3 viewerPosition = hasViewer ? _viewer.position : Vector3.zero;
            float push = _nodeDiameter * _labelPush;

            foreach (KeyValuePair<int, NodeView> pair in _nodesById)
            {
                NodeView node = pair.Value;
                if (node == null) continue;

                node.ApplyIdle(_idleTime, _theme);
                if (hasViewer) node.FaceLabel(viewerPosition, push);
            }

            foreach (KeyValuePair<long, EdgeView> pair in _edgesByPair)
            {
                EdgeView edge = pair.Value;

                NodeView parent;
                NodeView child;
                if (!_nodesById.TryGetValue(edge.ParentId, out parent)) continue;
                if (!_nodesById.TryGetValue(edge.ChildId, out child)) continue;

                // World space, then converted, so an edge keeps up with a node that is
                // being carried around by an interactor outside this hierarchy.
                edge.Span(
                    edge.transform.parent.InverseTransformPoint(parent.transform.position),
                    edge.transform.parent.InverseTransformPoint(child.transform.position),
                    NodeRadius);
            }
        }

        // ------------------------------------------------------- IStructureView

        public void PlaySteps(IReadOnlyList<TreeStep> steps)
        {
            // Playback lives in the step player, which arrives with the interaction
            // phase. Until then the reconcile above is what makes changes visible.
        }

        public void ShowRejection(int nodeId, string reason)
        {
            NodeView view;
            if (!_nodesById.TryGetValue(nodeId, out view)) return;

            if (_theme != null)
            {
                view.SetHighlight(_theme.InvalidDrop, 1f);
                TweenRunner.Instance.Punch(view.Body, Vector3.one, -_theme.SpawnPunch * 0.5f,
                    _theme.PopDuration, _theme.Pop);
            }
        }
    }
}
