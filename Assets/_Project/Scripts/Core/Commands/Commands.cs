using System.Collections.Generic;

namespace Pivot.Core.Commands
{
    /// <summary>One reversible thing the user did, with a name the UI can show.</summary>
    public interface ICommand
    {
        string Name { get; }

        /// <summary>Runs the command, narrating into <paramref name="steps"/>. False means nothing changed.</summary>
        bool Execute(List<TreeStep> steps);

        void Undo();

        void Redo();
    }

    /// <summary>
    /// Base for anything that mutates a tree. Undo works by restoring a structural
    /// snapshot taken before the change rather than by hand-writing an inverse for
    /// each operation: trees here hold at most a couple of hundred nodes, snapshots
    /// preserve node ids exactly so the view animates instead of respawning, and it
    /// is impossible to get subtly wrong the way a hand-written delete inverse is.
    /// </summary>
    public abstract class TreeCommand : ICommand
    {
        protected readonly BinarySearchTree Tree;

        TreeSnapshot _before;
        TreeSnapshot _after;

        protected TreeCommand(BinarySearchTree tree)
        {
            Tree = tree;
        }

        public abstract string Name { get; }

        public bool Execute(List<TreeStep> steps)
        {
            _before = Tree.Capture();
            if (!Perform(steps)) return false;
            _after = Tree.Capture();
            return true;
        }

        public void Undo()
        {
            Tree.Restore(_before);
        }

        public void Redo()
        {
            Tree.Restore(_after);
        }

        protected abstract bool Perform(List<TreeStep> steps);
    }

    public sealed class InsertCommand : TreeCommand
    {
        readonly int _value;

        public int NewNodeId { get; private set; }

        public InsertCommand(BinarySearchTree tree, int value) : base(tree)
        {
            _value = value;
        }

        public override string Name
        {
            get { return "Insert " + _value; }
        }

        protected override bool Perform(List<TreeStep> steps)
        {
            int id;
            bool ok = Tree.Insert(_value, steps, out id);
            NewNodeId = id;
            return ok;
        }
    }

    public sealed class DeleteCommand : TreeCommand
    {
        readonly int _nodeId;
        readonly int _value;

        public DeleteCommand(BinarySearchTree tree, int nodeId) : base(tree)
        {
            _nodeId = nodeId;
            BstNode node = tree.GetById(nodeId);
            _value = node != null ? node.Value : 0;
        }

        public override string Name
        {
            get { return "Delete " + _value; }
        }

        protected override bool Perform(List<TreeStep> steps)
        {
            return Tree.DeleteById(_nodeId, steps);
        }
    }

    public sealed class RotateCommand : TreeCommand
    {
        readonly int _nodeId;
        readonly bool _left;
        readonly int _value;

        public RotateCommand(BinarySearchTree tree, int nodeId, bool left) : base(tree)
        {
            _nodeId = nodeId;
            _left = left;
            BstNode node = tree.GetById(nodeId);
            _value = node != null ? node.Value : 0;
        }

        public override string Name
        {
            get { return "Rotate " + _value + (_left ? " left" : " right"); }
        }

        protected override bool Perform(List<TreeStep> steps)
        {
            return _left ? Tree.RotateLeft(_nodeId, steps) : Tree.RotateRight(_nodeId, steps);
        }
    }

    public sealed class ReparentCommand : TreeCommand
    {
        readonly int _nodeId;
        readonly int _parentId;
        readonly ChildSide _side;
        readonly int _value;

        public ReparentCommand(BinarySearchTree tree, int nodeId, int parentId, ChildSide side)
            : base(tree)
        {
            _nodeId = nodeId;
            _parentId = parentId;
            _side = side;
            BstNode node = tree.GetById(nodeId);
            _value = node != null ? node.Value : 0;
        }

        public override string Name
        {
            get { return "Move " + _value; }
        }

        protected override bool Perform(List<TreeStep> steps)
        {
            return Tree.Reparent(_nodeId, _parentId, _side, steps);
        }
    }

    /// <summary>Rebuilds the tree from its authored seed. Undoable like anything else.</summary>
    public sealed class ResetCommand : TreeCommand
    {
        readonly IReadOnlyList<int> _seed;

        public ResetCommand(BinarySearchTree tree, IReadOnlyList<int> seed) : base(tree)
        {
            _seed = seed;
        }

        public override string Name
        {
            get { return "Reset"; }
        }

        protected override bool Perform(List<TreeStep> steps)
        {
            Tree.BuildFrom(_seed);
            if (steps != null) steps.Add(new TreeStep(StepKind.Reflow, 0, "Back to the starting tree"));
            return true;
        }
    }
}
