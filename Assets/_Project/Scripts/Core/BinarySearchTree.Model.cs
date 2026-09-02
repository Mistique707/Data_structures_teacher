namespace Pivot.Core
{
    /// <summary>
    /// The generic face of the tree. Kept in its own file so the algorithmic half
    /// stays readable and so a future structure can copy this shape without copying
    /// the BST logic with it.
    /// </summary>
    public sealed partial class BinarySearchTree
    {
        public int RootId
        {
            get { return _root != null ? _root.Id : 0; }
        }

        public int ValueOf(int id)
        {
            BstNode n = GetById(id);
            return n != null ? n.Value : 0;
        }

        public int ParentOf(int id)
        {
            BstNode n = GetById(id);
            return n != null && n.Parent != null ? n.Parent.Id : 0;
        }

        public int ChildOf(int id, ChildSide side)
        {
            BstNode n = GetById(id);
            if (n == null) return 0;
            BstNode child = n.Child(side);
            return child != null ? child.Id : 0;
        }
    }
}
