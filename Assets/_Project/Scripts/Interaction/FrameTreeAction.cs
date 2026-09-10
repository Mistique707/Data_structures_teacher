using Pivot.Structures;
using UnityEngine;

namespace Pivot.Interaction
{
    /// <summary>
    /// Frame Tree (F). Pulls the view back to where the whole tree is visible.
    ///
    /// The bounds are measured from whatever node views are actually in the scene rather
    /// than from the model, so it works on the authored static tree now and will keep
    /// working once the tree is live and changing size.
    /// </summary>
    public sealed class FrameTreeAction : MonoBehaviour
    {
        [SerializeField] DesktopLocomotion _locomotion;
        [SerializeField] Camera _camera;

        [Tooltip("Searched for node views. Defaults to the whole scene if unset.")]
        [SerializeField] Transform _treeRoot;

        [Tooltip("Padding added around the tree, in metres.")]
        [SerializeField] float _margin = 0.2f;

        void Update()
        {
            PivotActions actions = PivotActions.Instance;
            if (actions == null || actions.FrameTree == null) return;
            if (!actions.FrameTree.WasPressedThisFrame()) return;

            Bounds bounds;
            if (!TryMeasure(out bounds)) return;

            if (_locomotion == null) _locomotion = GetComponentInParent<DesktopLocomotion>();
            if (_camera == null) _camera = GetComponentInParent<Camera>();
            if (_locomotion == null || _camera == null) return;

            _locomotion.Frame(bounds, _camera.fieldOfView);
        }

        bool TryMeasure(out Bounds bounds)
        {
            bounds = new Bounds();

            NodeView[] nodes = _treeRoot != null
                ? _treeRoot.GetComponentsInChildren<NodeView>(false)
                : FindObjectsByType<NodeView>(FindObjectsInactive.Exclude);

            if (nodes == null || nodes.Length == 0) return false;

            bounds = new Bounds(nodes[0].transform.position, Vector3.zero);
            for (int i = 1; i < nodes.Length; i++) bounds.Encapsulate(nodes[i].transform.position);

            bounds.Expand(_margin * 2f);
            return true;
        }

#if UNITY_EDITOR
        public void EditorBind(DesktopLocomotion locomotion, Camera camera, Transform treeRoot)
        {
            _locomotion = locomotion;
            _camera = camera;
            _treeRoot = treeRoot;
        }
#endif
    }
}
