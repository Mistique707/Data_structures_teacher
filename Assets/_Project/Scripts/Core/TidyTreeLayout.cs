using System.Collections.Generic;

namespace Pivot.Core
{
    /// <summary>
    /// Reingold-Tilford tidy tree, in the linear-time form Buchheim, Junger and
    /// Leipert describe. Parents sit centred over their children, sibling subtrees
    /// pack as tightly as the spacing allows, and the result is stable: the same
    /// tree always lays out the same way, so animating between two states is safe.
    ///
    /// One deviation from the textbook algorithm, and it matters for teaching: a
    /// node with exactly one child gets a zero-width phantom sibling on the empty
    /// side. Without it a lone child would be centred under its parent and the user
    /// could not tell a left child from a right one.
    /// </summary>
    public sealed class TidyTreeLayout : ILayoutStrategy
    {
        /// <summary>Minimum gap between two adjacent subtrees, in world units.</summary>
        public float SiblingSpacing = 1.15f;

        /// <summary>Vertical drop per level of depth.</summary>
        public float LevelSpacing = 1.25f;

        /// <summary>Centre the finished layout on its bounding box rather than on the root.</summary>
        public bool CentreOnBounds = true;

        sealed class Cell
        {
            public int Id;
            public Cell Parent;
            public Cell Left;
            public Cell Right;
            public int Number;
            public int Depth;

            public float Prelim;
            public float Mod;
            public float Shift;
            public float Change;
            public Cell Thread;
            public Cell Ancestor;

            public bool HasChildren
            {
                get { return Left != null; }
            }

            public void Reset()
            {
                Id = 0;
                Parent = null;
                Left = null;
                Right = null;
                Number = 0;
                Depth = 0;
                Prelim = 0f;
                Mod = 0f;
                Shift = 0f;
                Change = 0f;
                Thread = null;
                Ancestor = this;
            }
        }

        readonly Stack<Cell> _pool = new Stack<Cell>(128);
        readonly List<Cell> _live = new List<Cell>(128);
        readonly List<int> _keyScratch = new List<int>(128);

        public void Compute(IStructureModel model, IDictionary<int, LayoutPoint> into)
        {
            into.Clear();
            Recycle();

            if (model == null || model.RootId == 0) return;

            Cell root = Build(model, model.RootId, null, 1, 0);

            FirstWalk(root);
            SecondWalk(root, -root.Prelim, into);

            if (CentreOnBounds) CentreHorizontally(into);
        }

        // ------------------------------------------------------------------ build

        Cell Build(IStructureModel model, int id, Cell parent, int number, int depth)
        {
            Cell cell = Rent();
            cell.Id = id;
            cell.Parent = parent;
            cell.Number = number;
            cell.Depth = depth;

            int leftId = model.ChildOf(id, ChildSide.Left);
            int rightId = model.ChildOf(id, ChildSide.Right);

            if (leftId == 0 && rightId == 0) return cell;

            cell.Left = leftId != 0
                ? Build(model, leftId, cell, 1, depth + 1)
                : Phantom(cell, 1, depth + 1);

            cell.Right = rightId != 0
                ? Build(model, rightId, cell, 2, depth + 1)
                : Phantom(cell, 2, depth + 1);

            return cell;
        }

        Cell Phantom(Cell parent, int number, int depth)
        {
            Cell cell = Rent();
            cell.Id = 0;
            cell.Parent = parent;
            cell.Number = number;
            cell.Depth = depth;
            return cell;
        }

        // ------------------------------------------------------------- first walk

        void FirstWalk(Cell v)
        {
            if (!v.HasChildren)
            {
                Cell w = LeftSibling(v);
                v.Prelim = w != null ? w.Prelim + SiblingSpacing : 0f;
                return;
            }

            Cell defaultAncestor = v.Left;

            FirstWalk(v.Left);
            defaultAncestor = Apportion(v.Left, defaultAncestor);

            FirstWalk(v.Right);
            defaultAncestor = Apportion(v.Right, defaultAncestor);

            ExecuteShifts(v);

            float midpoint = 0.5f * (v.Left.Prelim + v.Right.Prelim);
            Cell sibling = LeftSibling(v);

            if (sibling != null)
            {
                v.Prelim = sibling.Prelim + SiblingSpacing;
                v.Mod = v.Prelim - midpoint;
            }
            else
            {
                v.Prelim = midpoint;
            }
        }

        Cell Apportion(Cell v, Cell defaultAncestor)
        {
            Cell w = LeftSibling(v);
            if (w == null) return defaultAncestor;

            Cell vip = v;
            Cell vop = v;
            Cell vim = w;
            Cell vom = vip.Parent.Left;

            float sip = vip.Mod;
            float sop = vop.Mod;
            float sim = vim.Mod;
            float som = vom.Mod;

            while (NextRight(vim) != null && NextLeft(vip) != null)
            {
                vim = NextRight(vim);
                vip = NextLeft(vip);
                vom = NextLeft(vom);
                vop = NextRight(vop);
                vop.Ancestor = v;

                float shift = (vim.Prelim + sim) - (vip.Prelim + sip) + SiblingSpacing;
                if (shift > 0f)
                {
                    MoveSubtree(Ancestor(vim, v, defaultAncestor), v, shift);
                    sip += shift;
                    sop += shift;
                }

                sim += vim.Mod;
                sip += vip.Mod;
                som += vom.Mod;
                sop += vop.Mod;
            }

            if (NextRight(vim) != null && NextRight(vop) == null)
            {
                vop.Thread = NextRight(vim);
                vop.Mod += sim - sop;
            }

            if (NextLeft(vip) != null && NextLeft(vom) == null)
            {
                vom.Thread = NextLeft(vip);
                vom.Mod += sip - som;
                defaultAncestor = v;
            }

            return defaultAncestor;
        }

        static void MoveSubtree(Cell wm, Cell wp, float shift)
        {
            int subtrees = wp.Number - wm.Number;
            if (subtrees == 0) return;

            float per = shift / subtrees;
            wp.Change -= per;
            wp.Shift += shift;
            wm.Change += per;
            wp.Prelim += shift;
            wp.Mod += shift;
        }

        /// <summary>Walks the children right to left, applying the shifts apportion accumulated.</summary>
        static void ExecuteShifts(Cell v)
        {
            float shift = 0f;
            float change = 0f;

            Cell w = v.Right;
            w.Prelim += shift;
            w.Mod += shift;
            change += w.Change;
            shift += w.Shift + change;

            w = v.Left;
            w.Prelim += shift;
            w.Mod += shift;
        }

        static Cell Ancestor(Cell vim, Cell v, Cell defaultAncestor)
        {
            if (vim.Ancestor != null && ReferenceEquals(vim.Ancestor.Parent, v.Parent))
                return vim.Ancestor;
            return defaultAncestor;
        }

        static Cell NextLeft(Cell v)
        {
            return v.HasChildren ? v.Left : v.Thread;
        }

        static Cell NextRight(Cell v)
        {
            return v.HasChildren ? v.Right : v.Thread;
        }

        static Cell LeftSibling(Cell v)
        {
            if (v.Parent == null) return null;
            return ReferenceEquals(v, v.Parent.Right) ? v.Parent.Left : null;
        }

        // ------------------------------------------------------------ second walk

        void SecondWalk(Cell v, float m, IDictionary<int, LayoutPoint> into)
        {
            if (v.Id != 0) into[v.Id] = new LayoutPoint(v.Prelim + m, v.Depth * LevelSpacing);

            if (!v.HasChildren) return;
            SecondWalk(v.Left, m + v.Mod, into);
            SecondWalk(v.Right, m + v.Mod, into);
        }

        void CentreHorizontally(IDictionary<int, LayoutPoint> into)
        {
            if (into.Count == 0) return;

            float min = float.MaxValue;
            float max = float.MinValue;
            foreach (KeyValuePair<int, LayoutPoint> pair in into)
            {
                if (pair.Value.X < min) min = pair.Value.X;
                if (pair.Value.X > max) max = pair.Value.X;
            }

            float offset = -0.5f * (min + max);
            if (offset > -0.0001f && offset < 0.0001f) return;

            // A dictionary cannot be mutated while it is being enumerated, so the
            // keys are copied into a list this instance keeps and reuses.
            _keyScratch.Clear();
            foreach (KeyValuePair<int, LayoutPoint> pair in into) _keyScratch.Add(pair.Key);

            for (int i = 0; i < _keyScratch.Count; i++)
            {
                int key = _keyScratch[i];
                LayoutPoint p = into[key];
                into[key] = new LayoutPoint(p.X + offset, p.Y);
            }
        }

        // ------------------------------------------------------------------- pool

        Cell Rent()
        {
            Cell c = _pool.Count > 0 ? _pool.Pop() : new Cell();
            c.Reset();
            _live.Add(c);
            return c;
        }

        void Recycle()
        {
            for (int i = 0; i < _live.Count; i++) _pool.Push(_live[i]);
            _live.Clear();
        }
    }
}
