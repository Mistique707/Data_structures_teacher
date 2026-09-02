using System.Collections.Generic;
using NUnit.Framework;
using Pivot.Core;

namespace Pivot.Tests
{
    /// <summary>
    /// The two lessons the app is built around. Placement is forced, so insertion
    /// order is the only thing that decides shape; and rotation is the only way to
    /// change a shape afterwards.
    /// </summary>
    public class ShapeAndRotationTests
    {
        BinarySearchTree _tree;
        List<int> _ids;

        [SetUp]
        public void SetUp()
        {
            _tree = new BinarySearchTree();
            _ids = new List<int>(32);
        }

        List<int> InOrderValues()
        {
            _tree.Traverse(TraversalKind.InOrder, _ids);
            List<int> values = new List<int>(_ids.Count);
            for (int i = 0; i < _ids.Count; i++) values.Add(_tree.GetById(_ids[i]).Value);
            return values;
        }

        // ------------------------------------------------------- order is the lesson

        [Test]
        public void AscendingInsertion_CollapsesIntoALinkedList()
        {
            _tree.BuildFrom(new[] { 20, 30, 40, 50, 60 });

            Assert.AreEqual(5, _tree.Count);
            Assert.AreEqual(4, _tree.Height, "five nodes in ascending order give a chain, not a tree");
            Assert.IsFalse(_tree.IsBalanced);

            // Every node hangs off the right of the one before it: this is a list.
            _tree.CollectIds(_ids);
            for (int i = 0; i < _ids.Count; i++)
            {
                BstNode node = _tree.GetById(_ids[i]);
                Assert.IsNull(node.Left, "a node in the chain should have nothing on its left");
            }

            Assert.AreEqual(20, _tree.Root.Value);
        }

        [Test]
        public void DescendingInsertion_CollapsesTheOtherWay()
        {
            _tree.BuildFrom(new[] { 60, 50, 40, 30, 20 });

            Assert.AreEqual(4, _tree.Height);
            Assert.AreEqual(60, _tree.Root.Value);

            _tree.CollectIds(_ids);
            for (int i = 0; i < _ids.Count; i++)
            {
                Assert.IsNull(_tree.GetById(_ids[i]).Right);
            }
        }

        [Test]
        public void TheSameValuesInABetterOrder_GiveTheShortestPossibleTree()
        {
            _tree.BuildFrom(new[] { 40, 20, 60, 30, 50 });

            Assert.AreEqual(5, _tree.Count);
            Assert.AreEqual(2, _tree.Height);
            Assert.IsTrue(_tree.IsBalanced);
            Assert.AreEqual(RotationSolver.MinimumHeight(5), _tree.Height);
        }

        [Test]
        public void OrderChangesTheShapeButNeverTheSortedOutput()
        {
            List<int> expected = new List<int> { 20, 30, 40, 50, 60 };

            _tree.BuildFrom(new[] { 20, 30, 40, 50, 60 });
            int chainHeight = _tree.Height;
            Assert.AreEqual(expected, InOrderValues());

            _tree.BuildFrom(new[] { 40, 20, 60, 30, 50 });
            Assert.AreEqual(expected, InOrderValues());
            Assert.Less(_tree.Height, chainHeight);

            _tree.BuildFrom(new[] { 60, 30, 50, 20, 40 });
            Assert.AreEqual(expected, InOrderValues());
        }

        [Test]
        public void HeightAndBalanceReactToEveryInsertSoTheReadoutCanBeLive()
        {
            int[] ascending = { 20, 30, 40, 50, 60 };
            int[] expectedHeights = { 0, 1, 2, 3, 4 };

            _tree.Clear();
            for (int i = 0; i < ascending.Length; i++)
            {
                _tree.Insert(ascending[i]);
                Assert.AreEqual(expectedHeights[i], _tree.Height,
                    "height after inserting " + ascending[i]);
            }

            Assert.IsFalse(_tree.IsBalanced);
        }

        // --------------------------------------------------------- minimum height

        [Test]
        public void MinimumHeight_IsTheHeightOfACompleteTree()
        {
            Assert.AreEqual(-1, RotationSolver.MinimumHeight(0));
            Assert.AreEqual(0, RotationSolver.MinimumHeight(1));
            Assert.AreEqual(1, RotationSolver.MinimumHeight(2));
            Assert.AreEqual(1, RotationSolver.MinimumHeight(3));
            Assert.AreEqual(2, RotationSolver.MinimumHeight(4));
            Assert.AreEqual(2, RotationSolver.MinimumHeight(7));
            Assert.AreEqual(3, RotationSolver.MinimumHeight(8));
            Assert.AreEqual(3, RotationSolver.MinimumHeight(12));
            Assert.AreEqual(4, RotationSolver.MinimumHeight(16));
        }

        [Test]
        public void ABalancedTreeIsAlreadyAtMinimumHeight()
        {
            _tree.BuildFrom(new[] { 50, 30, 70, 20, 40, 60, 80 });

            Assert.IsTrue(RotationSolver.IsAtMinimumHeight(_tree));
            Assert.AreEqual(0, RotationSolver.MinimumRotations(_tree));
        }

        // ------------------------------------------------------- rotation solving

        [Test]
        public void AFourNodeChain_IsOneRotationFromTheBest()
        {
            _tree.BuildFrom(new[] { 1, 2, 3, 4 });

            Assert.AreEqual(3, _tree.Height);
            Assert.AreEqual(2, RotationSolver.MinimumHeight(4));
            Assert.AreEqual(1, RotationSolver.MinimumRotations(_tree));
        }

        [Test]
        public void AFiveNodeChain_IsTwoRotationsFromTheBest()
        {
            _tree.BuildFrom(new[] { 1, 2, 3, 4, 5 });

            Assert.AreEqual(4, _tree.Height);
            Assert.AreEqual(2, RotationSolver.MinimumRotations(_tree));
        }

        [Test]
        public void ASevenNodeChain_IsFourRotationsFromTheBest()
        {
            _tree.BuildFrom(new[] { 1, 2, 3, 4, 5, 6, 7 });

            Assert.AreEqual(6, _tree.Height);
            Assert.AreEqual(4, RotationSolver.MinimumRotations(_tree));
        }

        [Test]
        public void ADescendingChain_CostsTheSameAsAnAscendingOne()
        {
            _tree.BuildFrom(new[] { 7, 6, 5, 4, 3, 2, 1 });
            Assert.AreEqual(4, RotationSolver.MinimumRotations(_tree));
        }

        [Test]
        public void TheSolversAnswerIsActuallyAchievable()
        {
            // Walk the tree down to minimum height greedily and confirm the solver's
            // count is not larger than something a player could stumble into.
            _tree.BuildFrom(new[] { 1, 2, 3, 4, 5 });
            int claimed = RotationSolver.MinimumRotations(_tree);

            int used = 0;
            int target = RotationSolver.MinimumHeight(_tree.Count);
            while (_tree.Height > target && used < 20)
            {
                // Rotate the root towards its taller side.
                BstNode root = _tree.Root;
                int leftHeight = BinarySearchTree.HeightOf(root.Left);
                int rightHeight = BinarySearchTree.HeightOf(root.Right);
                bool rotateLeft = rightHeight >= leftHeight;

                if (!(rotateLeft ? _tree.RotateLeft(root.Id, null) : _tree.RotateRight(root.Id, null)))
                    break;

                used++;
            }

            Assert.AreEqual(target, _tree.Height, "the greedy walk should reach minimum height");
            Assert.LessOrEqual(claimed, used, "the solver must never claim more than an achievable route");
            Assert.IsTrue(_tree.IsValidBst());
        }

        [Test]
        public void RotationsNeverChangeWhichValuesTheTreeHolds()
        {
            _tree.BuildFrom(new[] { 1, 2, 3, 4, 5, 6, 7 });
            List<int> before = InOrderValues();

            _tree.RotateLeft(_tree.RootId, null);
            _tree.RotateLeft(_tree.RootId, null);
            _tree.RotateRight(_tree.RootId, null);

            Assert.AreEqual(before, InOrderValues());
            Assert.IsTrue(_tree.IsValidBst());
        }

        [Test]
        public void ATreeLargerThanTheSolverHandles_ReportsUnknownRatherThanStalling()
        {
            List<int> many = new List<int>();
            for (int i = 1; i <= RotationSolver.MaxNodes + 1; i++) many.Add(i);
            _tree.BuildFrom(many);

            Assert.AreEqual(RotationSolver.Unknown, RotationSolver.MinimumRotations(_tree));
        }

        [Test]
        public void TheChallengeKnowsWhenItCannotScoreItself()
        {
            // The UI hides the challenge rather than showing a target it cannot verify.
            Assert.IsTrue(RotationSolver.CanSolve(0));
            Assert.IsTrue(RotationSolver.CanSolve(7));
            Assert.IsTrue(RotationSolver.CanSolve(RotationSolver.MaxNodes));
            Assert.IsFalse(RotationSolver.CanSolve(RotationSolver.MaxNodes + 1));

            List<int> many = new List<int>();
            for (int i = 1; i <= RotationSolver.MaxNodes + 1; i++) many.Add(i);
            _tree.BuildFrom(many);
            Assert.IsFalse(RotationSolver.CanSolve(_tree));

            _tree.BuildFrom(new[] { 50, 30, 70 });
            Assert.IsTrue(RotationSolver.CanSolve(_tree));
        }

        [Test]
        public void AnEmptyTreeNeedsNoRotations()
        {
            Assert.AreEqual(0, RotationSolver.MinimumRotations(_tree));
            _tree.BuildFrom(new[] { 42 });
            Assert.AreEqual(0, RotationSolver.MinimumRotations(_tree));
        }
    }
}
