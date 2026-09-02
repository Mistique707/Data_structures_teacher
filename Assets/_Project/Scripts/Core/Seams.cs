using System.Collections.Generic;

namespace Pivot.Core
{
    /// <summary>
    /// The shape every data structure presents to the rest of the app. It is
    /// deliberately tree-flavoured: a linear structure (stack, queue, linked list)
    /// reports its successor as the Right child and nothing on the Left, which is
    /// enough for both the layout pass and the view reconciler.
    /// </summary>
    public interface IStructureModel
    {
        int Count { get; }

        /// <summary>Id of the entry point, or 0 when the structure is empty.</summary>
        int RootId { get; }

        /// <summary>Fills the list with every live id. Parents come before children.</summary>
        void CollectIds(List<int> into);

        int ValueOf(int id);

        /// <summary>Parent id, or 0 for the root.</summary>
        int ParentOf(int id);

        /// <summary>Child id on that side, or 0 when the slot is empty.</summary>
        int ChildOf(int id, ChildSide side);

        TreeSnapshot Capture();

        void Restore(TreeSnapshot snapshot);
    }

    /// <summary>Turns a model into positions. Swap the implementation to change how a structure is drawn.</summary>
    public interface ILayoutStrategy
    {
        void Compute(IStructureModel model, IDictionary<int, LayoutPoint> into);
    }

    /// <summary>What the user is trying to do when they let go of a dragged node.</summary>
    public readonly struct DropAttempt
    {
        public readonly int NodeId;
        public readonly int TargetParentId;
        public readonly ChildSide Side;

        public DropAttempt(int nodeId, int targetParentId, ChildSide side)
        {
            NodeId = nodeId;
            TargetParentId = targetParentId;
            Side = side;
        }
    }

    /// <summary>One reason a drop might be refused. Rules explain; they never just say no.</summary>
    public interface IValidationRule
    {
        ValidationResult Check(IStructureModel model, in DropAttempt attempt);
    }

    /// <summary>
    /// The view contract, kept free of Unity types so Core stays engine-agnostic.
    /// The MonoBehaviour that draws the structure implements this.
    /// </summary>
    public interface IStructureView
    {
        /// <summary>Bring the visuals in line with the model, animating from wherever they are now.</summary>
        void Reconcile(IStructureModel model, IReadOnlyDictionary<int, LayoutPoint> layout);

        /// <summary>Queue a narrated operation for playback. Honours step mode.</summary>
        void PlaySteps(IReadOnlyList<TreeStep> steps);

        /// <summary>Show a refusal without changing the structure.</summary>
        void ShowRejection(int nodeId, string reason);
    }

    /// <summary>
    /// A pluggable data structure: model, layout, rules, and the view that draws it.
    /// Adding a stack later means writing one of these plus an authored scene, and
    /// touching nothing that already exists.
    /// </summary>
    public interface IDataStructureModule
    {
        string DisplayName { get; }

        IStructureModel Model { get; }

        ILayoutStrategy Layout { get; }

        IReadOnlyList<IValidationRule> Rules { get; }

        IStructureView View { get; set; }

        /// <summary>Runs every rule and returns the first refusal, or Valid.</summary>
        ValidationResult Validate(in DropAttempt attempt);

        /// <summary>Put the structure back to its authored starting state.</summary>
        void ResetToSeed();
    }
}
