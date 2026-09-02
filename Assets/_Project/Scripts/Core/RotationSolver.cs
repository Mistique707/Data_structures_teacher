using System.Collections.Generic;

namespace Pivot.Core
{
    /// <summary>
    /// Rotations are the only way to reshape a binary search tree without changing
    /// which values it holds — every other operation is forced, because each empty
    /// slot accepts exactly one interval of values. That makes this the tool the
    /// rotation challenge is built on.
    ///
    /// The solver answers "how few rotations reach the shortest possible tree?" by
    /// breadth-first search over tree shapes. The rotation graph on binary trees is
    /// connected and its diameter is small, so the answer is usually found after
    /// exploring only a handful of states.
    /// </summary>
    public static class RotationSolver
    {
        /// <summary>Above this the search space stops being worth exploring at interactive speed.</summary>
        public const int MaxNodes = 12;

        /// <summary>Hard stop so a pathological case can never stall a frame.</summary>
        public const int MaxStatesExplored = 400000;

        /// <summary>Returned when the tree is too big to solve exactly.</summary>
        public const int Unknown = -1;

        /// <summary>
        /// The shortest height any tree of this many nodes can have, which is the
        /// height of a complete tree: floor(log2(n)). Empty is -1, one node is 0.
        /// </summary>
        public static int MinimumHeight(int nodeCount)
        {
            if (nodeCount <= 0) return -1;

            int height = 0;
            int capacity = 1;
            while (capacity * 2 <= nodeCount)
            {
                capacity *= 2;
                height++;
            }

            return height;
        }

        public static bool IsAtMinimumHeight(BinarySearchTree tree)
        {
            return tree.Height == MinimumHeight(tree.Count);
        }

        /// <summary>
        /// Whether an exact answer is available for a tree this size. The challenge UI
        /// checks this and hides itself rather than offering a target it cannot score.
        /// </summary>
        public static bool CanSolve(int nodeCount)
        {
            return nodeCount >= 0 && nodeCount <= MaxNodes;
        }

        public static bool CanSolve(BinarySearchTree tree)
        {
            return tree != null && CanSolve(tree.Count);
        }

        /// <summary>
        /// Fewest rotations from the current shape to any shape of minimum height.
        /// Zero when the tree is already there, <see cref="Unknown"/> when the tree
        /// is larger than <see cref="MaxNodes"/>.
        /// </summary>
        public static int MinimumRotations(BinarySearchTree tree)
        {
            if (tree == null || tree.Count == 0) return 0;
            if (tree.Count > MaxNodes) return Unknown;

            Shape start = Shape.From(tree);
            int target = MinimumHeight(tree.Count);
            if (start.Height() <= target) return 0;

            HashSet<long> seen = new HashSet<long>();
            Queue<Shape> frontier = new Queue<Shape>();
            Queue<int> depths = new Queue<int>();

            seen.Add(start.Key());
            frontier.Enqueue(start);
            depths.Enqueue(0);

            int explored = 0;

            while (frontier.Count > 0)
            {
                Shape current = frontier.Dequeue();
                int depth = depths.Dequeue();

                if (++explored > MaxStatesExplored) return Unknown;

                for (int i = 0; i < current.Count; i++)
                {
                    for (int dir = 0; dir < 2; dir++)
                    {
                        bool left = dir == 0;
                        if (!current.CanRotate(i, left)) continue;

                        Shape next = current.Clone();
                        next.Rotate(i, left);

                        long key = next.Key();
                        if (!seen.Add(key)) continue;

                        if (next.Height() <= target) return depth + 1;

                        frontier.Enqueue(next);
                        depths.Enqueue(depth + 1);
                    }
                }
            }

            return Unknown;
        }

        /// <summary>
        /// A tree shape stripped of its values. Nodes are identified by their in-order
        /// position, which a binary search tree fixes completely, so two trees over
        /// the same values have the same shape exactly when these arrays match.
        /// </summary>
        sealed class Shape
        {
            public int Count;
            public int Root;
            public int[] Left;
            public int[] Right;

            public static Shape From(BinarySearchTree tree)
            {
                List<int> inOrder = new List<int>(tree.Count);
                tree.Traverse(TraversalKind.InOrder, inOrder);

                Dictionary<int, int> slotOf = new Dictionary<int, int>(inOrder.Count);
                for (int i = 0; i < inOrder.Count; i++) slotOf[inOrder[i]] = i;

                Shape shape = new Shape
                {
                    Count = inOrder.Count,
                    Left = new int[inOrder.Count],
                    Right = new int[inOrder.Count],
                    Root = slotOf[tree.RootId]
                };

                for (int i = 0; i < inOrder.Count; i++)
                {
                    int id = inOrder[i];
                    int leftId = tree.ChildOf(id, ChildSide.Left);
                    int rightId = tree.ChildOf(id, ChildSide.Right);
                    shape.Left[i] = leftId == 0 ? -1 : slotOf[leftId];
                    shape.Right[i] = rightId == 0 ? -1 : slotOf[rightId];
                }

                return shape;
            }

            public Shape Clone()
            {
                return new Shape
                {
                    Count = Count,
                    Root = Root,
                    Left = (int[])Left.Clone(),
                    Right = (int[])Right.Clone()
                };
            }

            public bool CanRotate(int index, bool left)
            {
                return left ? Right[index] != -1 : Left[index] != -1;
            }

            public void Rotate(int index, bool left)
            {
                int parent = ParentOf(index);
                bool wasLeftChild = parent != -1 && Left[parent] == index;

                int lifted;
                if (left)
                {
                    lifted = Right[index];
                    Right[index] = Left[lifted];
                    Left[lifted] = index;
                }
                else
                {
                    lifted = Left[index];
                    Left[index] = Right[lifted];
                    Right[lifted] = index;
                }

                if (parent == -1) Root = lifted;
                else if (wasLeftChild) Left[parent] = lifted;
                else Right[parent] = lifted;
            }

            int ParentOf(int index)
            {
                for (int i = 0; i < Count; i++)
                {
                    if (Left[i] == index || Right[i] == index) return i;
                }

                return -1;
            }

            public int Height()
            {
                return HeightOf(Root);
            }

            int HeightOf(int index)
            {
                if (index == -1) return -1;
                int a = HeightOf(Left[index]);
                int b = HeightOf(Right[index]);
                return 1 + (a > b ? a : b);
            }

            /// <summary>
            /// A pre-order bitstring: one bit per visit, set for a node and clear for
            /// an empty slot. That is 2n+1 bits, which for twelve nodes is 25 — small
            /// enough to key a dictionary with a plain long. The in-order positions
            /// need no encoding of their own, because the shape already fixes them.
            /// </summary>
            public long Key()
            {
                long key = 1;
                Pack(Root, ref key);
                return key;
            }

            void Pack(int index, ref long key)
            {
                if (index == -1)
                {
                    key <<= 1;
                    return;
                }

                key = (key << 1) | 1L;
                Pack(Left[index], ref key);
                Pack(Right[index], ref key);
            }
        }
    }
}
