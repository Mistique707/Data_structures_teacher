using System;
using System.Collections.Generic;

namespace Pivot.Core
{
    /// <summary>
    /// The model. No Unity types anywhere in here so it can be unit tested on its own.
    /// Every mutating operation optionally fills a caller-supplied step list that the
    /// view replays as animation; passing null skips the narration entirely.
    /// </summary>
    public sealed partial class BinarySearchTree : IStructureModel
    {
        readonly Dictionary<int, BstNode> _byId = new Dictionary<int, BstNode>(64);
        readonly Queue<BstNode> _bfsQueue = new Queue<BstNode>(64);
        readonly Stack<BstNode> _walkStack = new Stack<BstNode>(64);
        readonly List<NodeRecord> _recordScratch = new List<NodeRecord>(64);

        BstNode _root;
        int _nextId = 1;

        public BstNode Root
        {
            get { return _root; }
        }

        public int Count
        {
            get { return _byId.Count; }
        }

        public event Action Changed;

        // ---------------------------------------------------------------- queries

        public BstNode GetById(int id)
        {
            BstNode n;
            return _byId.TryGetValue(id, out n) ? n : null;
        }

        public BstNode Find(int value)
        {
            BstNode cur = _root;
            while (cur != null)
            {
                if (value == cur.Value) return cur;
                cur = value < cur.Value ? cur.Left : cur.Right;
            }

            return null;
        }

        public bool Contains(int value)
        {
            return Find(value) != null;
        }

        /// <summary>Edge count on the longest root-to-leaf path. Empty is -1, a single node 0.</summary>
        public int Height
        {
            get { return HeightOf(_root); }
        }

        public static int HeightOf(BstNode node)
        {
            if (node == null) return -1;
            int left = HeightOf(node.Left);
            int right = HeightOf(node.Right);
            return 1 + (left > right ? left : right);
        }

        public static int DepthOf(BstNode node)
        {
            int d = 0;
            while (node != null && node.Parent != null)
            {
                d++;
                node = node.Parent;
            }

            return d;
        }

        /// <summary>Height-balanced in the AVL sense: no subtree pair differs by more than one.</summary>
        public bool IsBalanced
        {
            get { return BalanceProbe(_root) >= 0; }
        }

        static int BalanceProbe(BstNode node)
        {
            if (node == null) return 0;
            int left = BalanceProbe(node.Left);
            if (left < 0) return -1;
            int right = BalanceProbe(node.Right);
            if (right < 0) return -1;
            int diff = left - right;
            if (diff < 0) diff = -diff;
            if (diff > 1) return -1;
            return 1 + (left > right ? left : right);
        }

        // ------------------------------------------------------------- traversals

        public void Traverse(TraversalKind kind, List<int> intoNodeIds)
        {
            if (intoNodeIds == null) throw new ArgumentNullException("intoNodeIds");
            intoNodeIds.Clear();
            if (_root == null) return;

            switch (kind)
            {
                case TraversalKind.PreOrder:
                    PreOrder(_root, intoNodeIds);
                    break;
                case TraversalKind.InOrder:
                    InOrder(_root, intoNodeIds);
                    break;
                case TraversalKind.PostOrder:
                    PostOrder(_root, intoNodeIds);
                    break;
                case TraversalKind.BreadthFirst:
                    BreadthFirst(intoNodeIds);
                    break;
                default:
                    throw new ArgumentOutOfRangeException("kind");
            }
        }

        static void PreOrder(BstNode n, List<int> into)
        {
            if (n == null) return;
            into.Add(n.Id);
            PreOrder(n.Left, into);
            PreOrder(n.Right, into);
        }

        static void InOrder(BstNode n, List<int> into)
        {
            if (n == null) return;
            InOrder(n.Left, into);
            into.Add(n.Id);
            InOrder(n.Right, into);
        }

        static void PostOrder(BstNode n, List<int> into)
        {
            if (n == null) return;
            PostOrder(n.Left, into);
            PostOrder(n.Right, into);
            into.Add(n.Id);
        }

        void BreadthFirst(List<int> into)
        {
            _bfsQueue.Clear();
            _bfsQueue.Enqueue(_root);
            while (_bfsQueue.Count > 0)
            {
                BstNode n = _bfsQueue.Dequeue();
                into.Add(n.Id);
                if (n.Left != null) _bfsQueue.Enqueue(n.Left);
                if (n.Right != null) _bfsQueue.Enqueue(n.Right);
            }
        }

        // ----------------------------------------------------------------- insert

        /// <summary>Inserts a value. Returns false on a duplicate, which the tree rejects.</summary>
        public bool Insert(int value, List<TreeStep> steps, out int newNodeId)
        {
            newNodeId = 0;

            if (_root == null)
            {
                BstNode created = NewNode(value);
                _root = created;
                newNodeId = created.Id;
                Step(steps, StepKind.Attach, created.Id, 0, value + " becomes the root");
                Step(steps, StepKind.Reflow, 0, 0, null);
                RaiseChanged();
                return true;
            }

            BstNode cur = _root;
            while (true)
            {
                Step(steps, StepKind.Compare, cur.Id, 0, null);

                if (value == cur.Value)
                {
                    Step(steps, StepKind.Reject, cur.Id, 0, value + " is already in the tree");
                    return false;
                }

                bool goLeft = value < cur.Value;
                string caption = value + (goLeft ? " < " : " > ") + cur.Value +
                                 (goLeft ? "  → left" : "  → right");
                BstNode next = goLeft ? cur.Left : cur.Right;

                if (next == null)
                {
                    BstNode created = NewNode(value);
                    created.Parent = cur;
                    cur.SetChild(goLeft ? ChildSide.Left : ChildSide.Right, created);
                    newNodeId = created.Id;
                    Step(steps, StepKind.Descend, cur.Id, created.Id, caption);
                    Step(steps, StepKind.Attach, created.Id, cur.Id, null);
                    Step(steps, StepKind.Reflow, 0, 0, null);
                    RaiseChanged();
                    return true;
                }

                Step(steps, StepKind.Descend, cur.Id, next.Id, caption);
                cur = next;
            }
        }

        public bool Insert(int value)
        {
            int ignored;
            return Insert(value, null, out ignored);
        }

        // ----------------------------------------------------------------- delete

        /// <summary>
        /// CLRS transplant delete. In the two-child case the in-order successor is
        /// relinked rather than value-copied, so the successor keeps its id and the
        /// view can animate it travelling up into the hole.
        /// </summary>
        public bool Delete(int value, List<TreeStep> steps)
        {
            BstNode z = _root;
            while (z != null && z.Value != value)
            {
                Step(steps, StepKind.Compare, z.Id, 0, null);
                bool goLeft = value < z.Value;
                BstNode next = goLeft ? z.Left : z.Right;
                Step(steps, StepKind.Descend, z.Id, next != null ? next.Id : 0,
                    value + (goLeft ? " < " : " > ") + z.Value +
                    (goLeft ? "  → left" : "  → right"));
                z = next;
            }

            if (z == null)
            {
                Step(steps, StepKind.Reject, 0, 0, value + " is not in the tree");
                return false;
            }

            return DeleteNode(z, steps);
        }

        public bool DeleteById(int nodeId, List<TreeStep> steps)
        {
            BstNode z = GetById(nodeId);
            return z != null && DeleteNode(z, steps);
        }

        bool DeleteNode(BstNode z, List<TreeStep> steps)
        {
            int children = z.ChildCount;

            if (children == 0)
            {
                Step(steps, StepKind.Remove, z.Id, 0, z.Value + " is a leaf — just pop it");
                Transplant(z, null);
            }
            else if (children == 1)
            {
                BstNode only = z.Left ?? z.Right;
                Step(steps, StepKind.Remove, z.Id, only.Id,
                    z.Value + " has one child — " + only.Value + " takes its place");
                Transplant(z, only);
            }
            else
            {
                BstNode y = Minimum(z.Right);
                Step(steps, StepKind.Remove, z.Id, y.Id,
                    z.Value + " has two children — successor " + y.Value + " moves in");

                if (!ReferenceEquals(y.Parent, z))
                {
                    Step(steps, StepKind.Transplant, y.Id, y.Right != null ? y.Right.Id : 0, null);
                    Transplant(y, y.Right);
                    y.Right = z.Right;
                    y.Right.Parent = y;
                }

                Transplant(z, y);
                y.Left = z.Left;
                y.Left.Parent = y;
                Step(steps, StepKind.Transplant, y.Id, 0, null);
            }

            Forget(z);
            Step(steps, StepKind.Reflow, 0, 0, null);
            RaiseChanged();
            return true;
        }

        public static BstNode Minimum(BstNode node)
        {
            while (node != null && node.Left != null) node = node.Left;
            return node;
        }

        public static BstNode Maximum(BstNode node)
        {
            while (node != null && node.Right != null) node = node.Right;
            return node;
        }

        /// <summary>Replaces the subtree at <paramref name="target"/> with <paramref name="replacement"/>.</summary>
        void Transplant(BstNode target, BstNode replacement)
        {
            if (target.Parent == null) _root = replacement;
            else if (target.IsLeftChild) target.Parent.Left = replacement;
            else target.Parent.Right = replacement;

            if (replacement != null) replacement.Parent = target.Parent;
        }

        // -------------------------------------------------------------- rotations

        public bool CanRotateLeft(int nodeId)
        {
            BstNode x = GetById(nodeId);
            return x != null && x.Right != null;
        }

        public bool CanRotateRight(int nodeId)
        {
            BstNode x = GetById(nodeId);
            return x != null && x.Left != null;
        }

        public bool RotateLeft(int nodeId, List<TreeStep> steps)
        {
            BstNode x = GetById(nodeId);
            if (x == null || x.Right == null)
            {
                Step(steps, StepKind.Reject, nodeId, 0, "Needs a right child to rotate left");
                return false;
            }

            BstNode y = x.Right;
            x.Right = y.Left;
            if (y.Left != null) y.Left.Parent = x;
            y.Parent = x.Parent;

            if (x.Parent == null) _root = y;
            else if (x.IsLeftChild) x.Parent.Left = y;
            else x.Parent.Right = y;

            y.Left = x;
            x.Parent = y;

            Step(steps, StepKind.Rotate, x.Id, y.Id, y.Value + " rotates up over " + x.Value);
            Step(steps, StepKind.Reflow, 0, 0, null);
            RaiseChanged();
            return true;
        }

        public bool RotateRight(int nodeId, List<TreeStep> steps)
        {
            BstNode x = GetById(nodeId);
            if (x == null || x.Left == null)
            {
                Step(steps, StepKind.Reject, nodeId, 0, "Needs a left child to rotate right");
                return false;
            }

            BstNode y = x.Left;
            x.Left = y.Right;
            if (y.Right != null) y.Right.Parent = x;
            y.Parent = x.Parent;

            if (x.Parent == null) _root = y;
            else if (x.IsLeftChild) x.Parent.Left = y;
            else x.Parent.Right = y;

            y.Right = x;
            x.Parent = y;

            Step(steps, StepKind.Rotate, x.Id, y.Id, y.Value + " rotates up over " + x.Value);
            Step(steps, StepKind.Reflow, 0, 0, null);
            RaiseChanged();
            return true;
        }

        // --------------------------------------------------------------- reparent

        /// <summary>
        /// Checks a drag-and-drop before it happens. The reason string is written to be
        /// shown to the user as-is: it always explains the rule, never scolds.
        /// </summary>
        public ValidationResult ValidateReparent(int nodeId, int newParentId, ChildSide side)
        {
            if (GetById(nodeId) == null) return ValidationResult.Invalid("That node is not in the tree.");
            if (GetById(newParentId) == null) return ValidationResult.Invalid("That slot is not in the tree.");

            return BstRules.CheckAll(BstRules.Default, this, new DropAttempt(nodeId, newParentId, side));
        }

        public bool Reparent(int nodeId, int newParentId, ChildSide side, List<TreeStep> steps)
        {
            ValidationResult check = ValidateReparent(nodeId, newParentId, side);
            if (!check.Ok)
            {
                Step(steps, StepKind.Reject, nodeId, newParentId, check.Reason);
                return false;
            }

            BstNode node = GetById(nodeId);
            BstNode parent = GetById(newParentId);

            Step(steps, StepKind.Detach, node.Id, node.Parent != null ? node.Parent.Id : 0, null);

            Detach(node);
            parent.SetChild(side, node);
            node.Parent = parent;

            Step(steps, StepKind.Attach, node.Id, parent.Id,
                node.Value + " goes " + SideWord(side) + " of " + parent.Value);
            Step(steps, StepKind.Reflow, 0, 0, null);
            RaiseChanged();
            return true;
        }

        /// <summary>Lifts a node and its subtree out of the tree without destroying it.</summary>
        public bool Detach(BstNode node)
        {
            if (node == null) return false;

            if (node.Parent == null)
            {
                if (ReferenceEquals(node, _root)) _root = null;
            }
            else
            {
                if (node.IsLeftChild) node.Parent.Left = null;
                else node.Parent.Right = null;
                node.Parent = null;
            }

            return true;
        }

        public static bool IsDescendant(BstNode candidate, BstNode ancestor)
        {
            BstNode cur = candidate;
            while (cur != null)
            {
                if (ReferenceEquals(cur, ancestor)) return true;
                cur = cur.Parent;
            }

            return false;
        }

        static string SideWord(ChildSide side)
        {
            return side == ChildSide.Left ? "left" : "right";
        }

        // ------------------------------------------------------- snapshot / build

        public TreeSnapshot Capture()
        {
            _recordScratch.Clear();
            CaptureWalk(_root, 0, ChildSide.Left);
            return new TreeSnapshot(_recordScratch.ToArray(), _root != null ? _root.Id : 0, _nextId);
        }

        void CaptureWalk(BstNode n, int parentId, ChildSide side)
        {
            if (n == null) return;
            _recordScratch.Add(new NodeRecord(n.Id, n.Value, parentId, side));
            CaptureWalk(n.Left, n.Id, ChildSide.Left);
            CaptureWalk(n.Right, n.Id, ChildSide.Right);
        }

        public void Restore(TreeSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");

            _byId.Clear();
            _root = null;
            _nextId = snapshot.NextId;

            NodeRecord[] records = snapshot.Nodes;
            for (int i = 0; i < records.Length; i++)
            {
                BstNode n = new BstNode { Id = records[i].Id, Value = records[i].Value };
                _byId[n.Id] = n;
            }

            for (int i = 0; i < records.Length; i++)
            {
                BstNode n = _byId[records[i].Id];
                int parentId = records[i].ParentId;
                if (parentId == 0) continue;

                BstNode parent = _byId[parentId];
                n.Parent = parent;
                parent.SetChild(records[i].Side, n);
            }

            if (snapshot.RootId != 0) _root = _byId[snapshot.RootId];
            RaiseChanged();
        }

        public void Clear()
        {
            _byId.Clear();
            _root = null;
            _nextId = 1;
            RaiseChanged();
        }

        /// <summary>Seeds by inserting in order. Used for the authored start state and by Reset.</summary>
        public void BuildFrom(IReadOnlyList<int> values)
        {
            Clear();
            if (values == null) return;
            for (int i = 0; i < values.Count; i++) Insert(values[i]);
        }

        // ---------------------------------------------------------------- helpers

        BstNode NewNode(int value)
        {
            BstNode n = new BstNode { Id = _nextId++, Value = value };
            _byId[n.Id] = n;
            return n;
        }

        void Forget(BstNode n)
        {
            _byId.Remove(n.Id);
            n.Parent = null;
            n.Left = null;
            n.Right = null;
        }

        static void Step(List<TreeStep> steps, StepKind kind, int nodeId, int otherId, string caption)
        {
            if (steps == null) return;
            steps.Add(new TreeStep(kind, nodeId, otherId, caption));
        }

        void RaiseChanged()
        {
            Action handler = Changed;
            if (handler != null) handler();
        }

        /// <summary>Fills <paramref name="into"/> with every live node id, parents before children.</summary>
        public void CollectIds(List<int> into)
        {
            into.Clear();
            if (_root == null) return;
            _walkStack.Clear();
            _walkStack.Push(_root);
            while (_walkStack.Count > 0)
            {
                BstNode n = _walkStack.Pop();
                into.Add(n.Id);
                if (n.Right != null) _walkStack.Push(n.Right);
                if (n.Left != null) _walkStack.Push(n.Left);
            }
        }

        /// <summary>True when the tree still satisfies the search property everywhere.</summary>
        public bool IsValidBst()
        {
            return ValidProbe(_root, long.MinValue, long.MaxValue);
        }

        static bool ValidProbe(BstNode n, long low, long high)
        {
            if (n == null) return true;
            if (n.Value <= low || n.Value >= high) return false;
            return ValidProbe(n.Left, low, n.Value) && ValidProbe(n.Right, n.Value, high);
        }
    }
}
