using System.Collections.Generic;

namespace Pivot.Core
{
    /// <summary>
    /// The one module this slice ships. It bundles the BST model, the tidy-tree
    /// layout, the drop rules and the view that draws them, which is the whole of
    /// what a data structure has to provide. A stack module would be this file with
    /// a different model, a linear layout and a shorter rule list.
    /// </summary>
    public sealed class BstModule : IDataStructureModule
    {
        readonly BinarySearchTree _tree;
        readonly TidyTreeLayout _layout;
        readonly IReadOnlyList<IValidationRule> _rules;
        readonly List<int> _seed;

        public BstModule(IReadOnlyList<int> seed = null)
        {
            _tree = new BinarySearchTree();
            _layout = new TidyTreeLayout();
            _rules = BstRules.Default;
            _seed = new List<int>(8);

            if (seed != null) _seed.AddRange(seed);
            if (_seed.Count > 0) _tree.BuildFrom(_seed);
        }

        public string DisplayName
        {
            get { return "Binary Search Tree"; }
        }

        public BinarySearchTree Tree
        {
            get { return _tree; }
        }

        public TidyTreeLayout TreeLayout
        {
            get { return _layout; }
        }

        public IStructureModel Model
        {
            get { return _tree; }
        }

        public ILayoutStrategy Layout
        {
            get { return _layout; }
        }

        public IReadOnlyList<IValidationRule> Rules
        {
            get { return _rules; }
        }

        public IStructureView View { get; set; }

        /// <summary>The values the scene was authored with. Reset rebuilds from these.</summary>
        public IReadOnlyList<int> Seed
        {
            get { return _seed; }
        }

        public void SetSeed(IReadOnlyList<int> values)
        {
            _seed.Clear();
            if (values == null) return;
            for (int i = 0; i < values.Count; i++) _seed.Add(values[i]);
        }

        public ValidationResult Validate(in DropAttempt attempt)
        {
            return BstRules.CheckAll(_rules, _tree, attempt);
        }

        public void ResetToSeed()
        {
            _tree.BuildFrom(_seed);
        }
    }
}
