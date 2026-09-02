namespace Pivot.Core
{
    /// <summary>
    /// A node in the tree. Id is stable for the node's whole life, including across
    /// undo, so the view can reconcile against it instead of rebuilding.
    /// </summary>
    public sealed class BstNode
    {
        public int Id;
        public int Value;
        public BstNode Parent;
        public BstNode Left;
        public BstNode Right;

        public bool IsLeaf
        {
            get { return Left == null && Right == null; }
        }

        public int ChildCount
        {
            get
            {
                int n = 0;
                if (Left != null) n++;
                if (Right != null) n++;
                return n;
            }
        }

        public bool IsLeftChild
        {
            get { return Parent != null && ReferenceEquals(Parent.Left, this); }
        }

        public BstNode Child(ChildSide side)
        {
            return side == ChildSide.Left ? Left : Right;
        }

        public void SetChild(ChildSide side, BstNode node)
        {
            if (side == ChildSide.Left) Left = node;
            else Right = node;
        }
    }
}
