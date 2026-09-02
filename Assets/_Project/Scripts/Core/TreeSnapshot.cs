using System.Collections.Generic;

namespace Pivot.Core
{
    public readonly struct NodeRecord
    {
        public readonly int Id;
        public readonly int Value;
        public readonly int ParentId;
        public readonly ChildSide Side;

        public NodeRecord(int id, int value, int parentId, ChildSide side)
        {
            Id = id;
            Value = value;
            ParentId = parentId;
            Side = side;
        }
    }

    /// <summary>
    /// A complete structural copy of a tree. Trees here are small, so commands
    /// capture one of these and restore it verbatim on undo: exact, and it keeps
    /// node ids stable so the view animates rather than respawns.
    /// </summary>
    public sealed class TreeSnapshot
    {
        public readonly NodeRecord[] Nodes;
        public readonly int RootId;
        public readonly int NextId;

        public TreeSnapshot(NodeRecord[] nodes, int rootId, int nextId)
        {
            Nodes = nodes;
            RootId = rootId;
            NextId = nextId;
        }

        public int Count
        {
            get { return Nodes.Length; }
        }

        public IEnumerable<int> Values
        {
            get
            {
                for (int i = 0; i < Nodes.Length; i++) yield return Nodes[i].Value;
            }
        }
    }
}
