using System.Collections.Generic;
using NUnit.Framework;
using Pivot.Core;

namespace Pivot.Tests
{
    /// <summary>
    /// The seed tree these tests use, which is also the tree authored into the lab scene:
    ///
    ///            50
    ///          /    \
    ///        30      70
    ///       /  \    /  \
    ///     20   40  60   80
    /// </summary>
    public class BinarySearchTreeTests
    {
        static readonly int[] Seed = { 50, 30, 70, 20, 40, 60, 80 };

        BinarySearchTree _tree;
        List<TreeStep> _steps;
        List<int> _ids;

        [SetUp]
        public void SetUp()
        {
            _tree = new BinarySearchTree();
            _tree.BuildFrom(Seed);
            _steps = new List<TreeStep>(32);
            _ids = new List<int>(32);
        }

        // ------------------------------------------------------------------ shape

        [Test]
        public void Seed_HasTheExpectedShape()
        {
            Assert.AreEqual(7, _tree.Count);
            Assert.AreEqual(2, _tree.Height);
            Assert.IsTrue(_tree.IsBalanced);
            Assert.IsTrue(_tree.IsValidBst());
            Assert.AreEqual(50, _tree.Root.Value);
            Assert.AreEqual(30, _tree.Root.Left.Value);
            Assert.AreEqual(70, _tree.Root.Right.Value);
        }

        [Test]
        public void EmptyTree_HasHeightMinusOne()
        {
            _tree.Clear();
            Assert.AreEqual(0, _tree.Count);
            Assert.AreEqual(-1, _tree.Height);
            Assert.IsTrue(_tree.IsBalanced);
            Assert.IsNull(_tree.Root);
        }

        [Test]
        public void DegenerateChain_IsNotBalanced()
        {
            _tree.BuildFrom(new[] { 1, 2, 3, 4 });
            Assert.AreEqual(3, _tree.Height);
            Assert.IsFalse(_tree.IsBalanced);
        }

        // ----------------------------------------------------------------- insert

        [Test]
        public void Insert_PutsTheValueWhereASearchWouldFindIt()
        {
            int id;
            Assert.IsTrue(_tree.Insert(45, _steps, out id));

            BstNode inserted = _tree.GetById(id);
            Assert.IsNotNull(inserted);
            Assert.AreEqual(45, inserted.Value);
            Assert.AreEqual(40, inserted.Parent.Value);
            Assert.AreSame(inserted, _tree.Find(40).Right);
            Assert.IsTrue(_tree.IsValidBst());
            Assert.AreEqual(8, _tree.Count);
        }

        [Test]
        public void Insert_IntoEmptyTree_BecomesTheRoot()
        {
            _tree.Clear();
            int id;
            Assert.IsTrue(_tree.Insert(42, _steps, out id));
            Assert.AreEqual(id, _tree.RootId);
            Assert.AreEqual(0, _tree.Height);
        }

        [Test]
        public void Insert_RejectsADuplicateAndChangesNothing()
        {
            int id;
            Assert.IsFalse(_tree.Insert(30, _steps, out id));
            Assert.AreEqual(0, id);
            Assert.AreEqual(7, _tree.Count);
            Assert.AreEqual(StepKind.Reject, _steps[_steps.Count - 1].Kind);
            StringAssert.Contains("already in the tree", _steps[_steps.Count - 1].Caption);
        }

        [Test]
        public void Insert_NarratesEveryComparisonOnTheWayDown()
        {
            int id;
            _tree.Insert(45, _steps, out id);

            List<string> captions = new List<string>();
            for (int i = 0; i < _steps.Count; i++)
            {
                if (_steps[i].Kind == StepKind.Descend) captions.Add(_steps[i].Caption);
            }

            // 45 < 50 -> left, 45 > 30 -> right, 45 > 40 -> right
            Assert.AreEqual(3, captions.Count);
            StringAssert.Contains("45 < 50", captions[0]);
            StringAssert.Contains("left", captions[0]);
            StringAssert.Contains("45 > 30", captions[1]);
            StringAssert.Contains("right", captions[1]);
            StringAssert.Contains("45 > 40", captions[2]);
        }

        // ----------------------------------------------------------------- delete

        [Test]
        public void Delete_LeafJustDisappears()
        {
            int parentId = _tree.Find(30).Id;

            Assert.IsTrue(_tree.Delete(20, _steps));

            Assert.AreEqual(6, _tree.Count);
            Assert.IsNull(_tree.Find(20));
            Assert.IsNull(_tree.GetById(parentId).Left);
            Assert.IsTrue(_tree.IsValidBst());
        }

        [Test]
        public void Delete_NodeWithOneChild_PullsThatChildUp()
        {
            _tree.Insert(45);                       // 40 now has a single right child
            int childId = _tree.Find(45).Id;

            Assert.IsTrue(_tree.Delete(40, _steps));

            Assert.IsNull(_tree.Find(40));
            BstNode moved = _tree.GetById(childId);
            Assert.IsNotNull(moved, "the surviving child keeps its identity");
            Assert.AreEqual(45, moved.Value);
            Assert.AreEqual(30, moved.Parent.Value);
            Assert.AreSame(moved, _tree.Find(30).Right);
            Assert.IsTrue(_tree.IsValidBst());
        }

        [Test]
        public void Delete_NodeWithTwoChildren_MovesTheSuccessorNodeItself()
        {
            int successorId = _tree.Find(40).Id;     // in-order successor of 30

            Assert.IsTrue(_tree.Delete(30, _steps));

            Assert.IsNull(_tree.Find(30));
            BstNode successor = _tree.GetById(successorId);
            Assert.IsNotNull(successor,
                "the successor must keep its id so the view can animate it moving, not respawn it");
            Assert.AreEqual(40, successor.Value);
            Assert.AreSame(successor, _tree.Root.Left);
            Assert.AreEqual(20, successor.Left.Value);
            Assert.IsNull(successor.Right);
            Assert.IsTrue(_tree.IsValidBst());
            Assert.AreEqual(6, _tree.Count);
        }

        [Test]
        public void Delete_TwoChildren_WhenSuccessorIsDeeperThanTheRightChild()
        {
            _tree.Insert(55);                        // 60 gains a left child
            int successorId = _tree.Find(55).Id;

            Assert.IsTrue(_tree.Delete(50, _steps)); // the root, successor is 55

            BstNode successor = _tree.GetById(successorId);
            Assert.AreEqual(55, successor.Value);
            Assert.AreSame(successor, _tree.Root);
            Assert.AreEqual(30, _tree.Root.Left.Value);
            Assert.AreEqual(70, _tree.Root.Right.Value);
            Assert.AreEqual(60, _tree.Find(70).Left.Value);
            Assert.IsNull(_tree.Find(60).Left);
            Assert.IsTrue(_tree.IsValidBst());
        }

        [Test]
        public void Delete_TheOnlyNode_EmptiesTheTree()
        {
            _tree.BuildFrom(new[] { 7 });
            Assert.IsTrue(_tree.Delete(7, _steps));
            Assert.AreEqual(0, _tree.Count);
            Assert.IsNull(_tree.Root);
            Assert.AreEqual(0, _tree.RootId);
        }

        [Test]
        public void Delete_MissingValue_IsRefusedAndExplained()
        {
            Assert.IsFalse(_tree.Delete(99, _steps));
            Assert.AreEqual(7, _tree.Count);
            Assert.AreEqual(StepKind.Reject, _steps[_steps.Count - 1].Kind);
            StringAssert.Contains("not in the tree", _steps[_steps.Count - 1].Caption);
        }

        // -------------------------------------------------------------- rotations

        [Test]
        public void RotateLeft_LiftsTheRightChildAndKeepsTheOrdering()
        {
            List<int> before = InOrderValues();
            int pivotId = _tree.Find(30).Id;

            Assert.IsTrue(_tree.RotateLeft(pivotId, _steps));

            Assert.AreEqual(40, _tree.Root.Left.Value, "40 rose over 30");
            Assert.AreEqual(30, _tree.Find(40).Left.Value);
            Assert.AreEqual(20, _tree.Find(30).Left.Value);
            Assert.IsNull(_tree.Find(30).Right);
            Assert.AreEqual(before, InOrderValues(), "a rotation never changes the sorted order");
            Assert.IsTrue(_tree.IsValidBst());
        }

        [Test]
        public void RotateRight_LiftsTheLeftChildAndKeepsTheOrdering()
        {
            List<int> before = InOrderValues();
            int pivotId = _tree.Find(70).Id;

            Assert.IsTrue(_tree.RotateRight(pivotId, _steps));

            Assert.AreEqual(60, _tree.Root.Right.Value);
            Assert.AreEqual(70, _tree.Find(60).Right.Value);
            Assert.AreEqual(80, _tree.Find(70).Right.Value);
            Assert.IsNull(_tree.Find(70).Left);
            Assert.AreEqual(before, InOrderValues());
            Assert.IsTrue(_tree.IsValidBst());
        }

        [Test]
        public void Rotate_AtTheRoot_ReplacesTheRoot()
        {
            List<int> before = InOrderValues();

            Assert.IsTrue(_tree.RotateLeft(_tree.RootId, _steps));

            Assert.AreEqual(70, _tree.Root.Value);
            Assert.IsNull(_tree.Root.Parent);
            Assert.AreEqual(50, _tree.Root.Left.Value);
            Assert.AreEqual(before, InOrderValues());
            Assert.IsTrue(_tree.IsValidBst());
        }

        [Test]
        public void Rotate_RoundTrip_RestoresTheOriginalShape()
        {
            int pivotId = _tree.Find(30).Id;
            Assert.IsTrue(_tree.RotateLeft(pivotId, _steps));
            Assert.IsTrue(_tree.RotateRight(_tree.Find(40).Id, _steps));

            Assert.AreEqual(30, _tree.Root.Left.Value);
            Assert.AreEqual(20, _tree.Find(30).Left.Value);
            Assert.AreEqual(40, _tree.Find(30).Right.Value);
            Assert.IsTrue(_tree.IsValidBst());
        }

        [Test]
        public void Rotate_WithoutTheChildItNeeds_IsRefusedAndExplained()
        {
            int leafId = _tree.Find(20).Id;

            Assert.IsFalse(_tree.RotateLeft(leafId, _steps));
            Assert.IsFalse(_tree.CanRotateLeft(leafId));
            Assert.IsFalse(_tree.CanRotateRight(leafId));
            Assert.AreEqual(StepKind.Reject, _steps[_steps.Count - 1].Kind);
            StringAssert.Contains("right child", _steps[_steps.Count - 1].Caption);
            Assert.AreEqual(7, _tree.Count);
        }

        // ------------------------------------------------------------- traversals

        [Test]
        public void PreOrder_VisitsRootThenLeftThenRight()
        {
            CollectAssert(TraversalKind.PreOrder, new[] { 50, 30, 20, 40, 70, 60, 80 });
        }

        [Test]
        public void InOrder_ComesOutSorted()
        {
            CollectAssert(TraversalKind.InOrder, new[] { 20, 30, 40, 50, 60, 70, 80 });
        }

        [Test]
        public void InOrder_StaysSortedAfterAnyMixOfOperations()
        {
            _tree.Insert(45);
            _tree.Insert(10);
            _tree.Insert(99);
            _tree.Delete(30, null);
            _tree.RotateLeft(_tree.RootId, null);

            List<int> values = InOrderValues();
            for (int i = 1; i < values.Count; i++)
            {
                Assert.Less(values[i - 1], values[i],
                    "in-order output must always be sorted, whatever the tree has been through");
            }
        }

        [Test]
        public void PostOrder_VisitsChildrenBeforeTheirParent()
        {
            CollectAssert(TraversalKind.PostOrder, new[] { 20, 40, 30, 60, 80, 70, 50 });
        }

        [Test]
        public void BreadthFirst_VisitsLevelByLevel()
        {
            CollectAssert(TraversalKind.BreadthFirst, new[] { 50, 30, 70, 20, 40, 60, 80 });
        }

        [Test]
        public void Traversals_OfAnEmptyTree_ProduceNothing()
        {
            _tree.Clear();
            _tree.Traverse(TraversalKind.InOrder, _ids);
            Assert.AreEqual(0, _ids.Count);
            _tree.Traverse(TraversalKind.BreadthFirst, _ids);
            Assert.AreEqual(0, _ids.Count);
        }

        // --------------------------------------------------------- drop validation

        [Test]
        public void Drop_OntoAnOccupiedSlot_IsRefusedAndNamesTheOccupant()
        {
            ValidationResult result = _tree.ValidateReparent(
                _tree.Find(20).Id, _tree.Find(70).Id, ChildSide.Left);

            Assert.IsFalse(result.Ok);
            StringAssert.Contains("already holds 60", result.Reason);
        }

        [Test]
        public void Drop_OntoItsOwnDescendant_IsRefusedAsALoop()
        {
            ValidationResult result = _tree.ValidateReparent(
                _tree.Find(30).Id, _tree.Find(20).Id, ChildSide.Left);

            Assert.IsFalse(result.Ok);
            StringAssert.Contains("loop", result.Reason);
        }

        [Test]
        public void Drop_OnItself_IsRefused()
        {
            int id = _tree.Find(30).Id;
            ValidationResult result = _tree.ValidateReparent(id, id, ChildSide.Left);

            Assert.IsFalse(result.Ok);
            StringAssert.Contains("own parent", result.Reason);
        }

        [Test]
        public void Drop_OfATooSmallValue_NamesTheWholeIntervalTheSlotAccepts()
        {
            _tree.Delete(60, null);                  // free up the left slot under 70

            ValidationResult result = _tree.ValidateReparent(
                _tree.Find(20).Id, _tree.Find(70).Id, ChildSide.Left);

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(
                "20 can't go here — this slot only accepts values between 50 and 70.",
                result.Reason);
        }

        [Test]
        public void Drop_OfATooLargeValue_NamesTheWholeIntervalTheSlotAccepts()
        {
            _tree.Delete(20, null);                  // free up the left slot under 30

            ValidationResult result = _tree.ValidateReparent(
                _tree.Find(80).Id, _tree.Find(30).Id, ChildSide.Left);

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(
                "80 can't go here — this slot only accepts values below 30.",
                result.Reason);
        }

        [Test]
        public void Drop_IntoAnUnboundedSlot_SaysAboveRatherThanNamingAFakeCeiling()
        {
            _tree.Delete(80, null);                  // free up the right slot under 70

            ValidationResult result = _tree.ValidateReparent(
                _tree.Find(20).Id, _tree.Find(70).Id, ChildSide.Right);

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(
                "20 can't go here — this slot only accepts values above 70.",
                result.Reason);
        }

        [Test]
        public void EmptySlotsAnnounceTheirOwnIntervalWithNothingBeingDragged()
        {
            _tree.Delete(60, null);
            _tree.Delete(20, null);

            Assert.AreEqual("values between 50 and 70",
                BstRules.DescribeSlot(_tree, _tree.Find(70).Id, ChildSide.Left));
            Assert.AreEqual("values below 30",
                BstRules.DescribeSlot(_tree, _tree.Find(30).Id, ChildSide.Left));
            Assert.AreEqual("values above 80",
                BstRules.DescribeSlot(_tree, _tree.Find(80).Id, ChildSide.Right));
            Assert.AreEqual("values between 30 and 40",
                BstRules.DescribeSlot(_tree, _tree.Find(40).Id, ChildSide.Left));
        }

        [Test]
        public void TheIntervalsOfEveryEmptySlotTileTheNumberLineWithoutOverlapping()
        {
            // The reason each value has exactly one home: the open slots partition
            // everything the tree does not already hold.
            _tree.CollectIds(_ids);
            List<int> nodeIds = new List<int>(_ids);

            List<int> lows = new List<int>();
            List<int> highs = new List<int>();

            foreach (int id in nodeIds)
            {
                for (int side = 0; side < 2; side++)
                {
                    ChildSide which = side == 0 ? ChildSide.Left : ChildSide.Right;
                    if (_tree.ChildOf(id, which) != 0) continue;

                    int low, high;
                    BstRules.SlotBounds(_tree, id, which, 0, out low, out high);
                    lows.Add(low);
                    highs.Add(high);
                }
            }

            Assert.AreEqual(8, lows.Count, "seven nodes leave eight open slots");

            // Sorting by lower bound, each interval must start exactly where the last ended.
            List<int> order = new List<int>();
            for (int i = 0; i < lows.Count; i++) order.Add(i);
            order.Sort((a, b) => lows[a].CompareTo(lows[b]));

            Assert.AreEqual(int.MinValue, lows[order[0]]);
            Assert.AreEqual(int.MaxValue, highs[order[order.Count - 1]]);

            for (int i = 1; i < order.Count; i++)
            {
                Assert.AreEqual(highs[order[i - 1]], lows[order[i]],
                    "slot intervals must meet exactly, leaving no value homeless and none with two homes");
            }
        }

        [Test]
        public void Drop_JudgesTheWholeBranch_NotJustTheNodeBeingHeld()
        {
            // 30 itself would be legal under 20's freed right slot, but it carries 40 with it.
            _tree.Delete(40, null);
            _tree.Insert(35);                        // 30 -> right -> 35
            _tree.Delete(20, null);

            ValidationResult result = _tree.ValidateReparent(
                _tree.Find(30).Id, _tree.Find(60).Id, ChildSide.Left);

            Assert.IsFalse(result.Ok, "the branch holds 30 and 35, both below 50");
        }

        [Test]
        public void Drop_BackIntoTheSlotItCameFrom_IsAllowed()
        {
            ValidationResult result = _tree.ValidateReparent(
                _tree.Find(60).Id, _tree.Find(70).Id, ChildSide.Left);

            Assert.IsTrue(result.Ok, result.Reason);
        }

        [Test]
        public void EveryNodeHasExactlyOneLegalSlot_TheOneItAlreadyOccupies()
        {
            // This is the whole lesson: a valid BST leaves each value exactly one home.
            _tree.CollectIds(_ids);
            List<int> nodeIds = new List<int>(_ids);

            foreach (int nodeId in nodeIds)
            {
                if (nodeId == _tree.RootId) continue;

                int legalSlots = 0;
                foreach (int parentId in nodeIds)
                {
                    if (_tree.ValidateReparent(nodeId, parentId, ChildSide.Left).Ok) legalSlots++;
                    if (_tree.ValidateReparent(nodeId, parentId, ChildSide.Right).Ok) legalSlots++;
                }

                Assert.AreEqual(1, legalSlots,
                    "value " + _tree.ValueOf(nodeId) + " should have exactly one valid parent slot");
            }
        }

        [Test]
        public void Reparent_TakesTheWholeBranchWithIt()
        {
            _tree.Delete(60, null);
            _tree.Delete(80, null);                  // 70 is now a leaf
            int branchId = _tree.Find(30).Id;
            int childId = _tree.Find(20).Id;

            // Illegal, so nothing moves and the tree is untouched.
            Assert.IsFalse(_tree.Reparent(branchId, _tree.Find(70).Id, ChildSide.Left, _steps));
            Assert.AreEqual(StepKind.Reject, _steps[_steps.Count - 1].Kind);
            Assert.AreSame(_tree.GetById(branchId), _tree.Root.Left);

            // Putting it back where it was is legal, and the children come along.
            Assert.IsTrue(_tree.Reparent(branchId, _tree.RootId, ChildSide.Left, _steps));
            Assert.AreSame(_tree.GetById(childId), _tree.GetById(branchId).Left);
            Assert.IsTrue(_tree.IsValidBst());
            Assert.AreEqual(5, _tree.Count);
        }

        // --------------------------------------------------------------- snapshots

        [Test]
        public void Snapshot_RoundTripsShapeValuesAndIds()
        {
            TreeSnapshot before = _tree.Capture();
            List<int> idsBefore = new List<int>();
            _tree.CollectIds(idsBefore);

            _tree.Delete(30, null);
            _tree.Insert(99);
            _tree.RotateLeft(_tree.RootId, null);

            _tree.Restore(before);

            List<int> idsAfter = new List<int>();
            _tree.CollectIds(idsAfter);

            Assert.AreEqual(idsBefore, idsAfter, "ids must survive so the view animates rather than respawns");
            Assert.AreEqual(new List<int> { 20, 30, 40, 50, 60, 70, 80 }, InOrderValues());
            Assert.AreEqual(2, _tree.Height);
            Assert.IsTrue(_tree.IsValidBst());
        }

        [Test]
        public void Snapshot_OfAnEmptyTree_RestoresCleanly()
        {
            _tree.Clear();
            TreeSnapshot empty = _tree.Capture();

            _tree.BuildFrom(Seed);
            _tree.Restore(empty);

            Assert.AreEqual(0, _tree.Count);
            Assert.IsNull(_tree.Root);
        }

        [Test]
        public void Restore_LeavesTheIdCounterSomewhereSafeForLaterInserts()
        {
            TreeSnapshot before = _tree.Capture();
            _tree.Insert(45);
            _tree.Restore(before);

            int id;
            Assert.IsTrue(_tree.Insert(45, null, out id));

            _tree.CollectIds(_ids);
            int seen = 0;
            for (int i = 0; i < _ids.Count; i++)
            {
                if (_ids[i] == id) seen++;
            }

            Assert.AreEqual(1, seen, "a fresh insert must not collide with a restored node id");
            Assert.AreEqual(8, _tree.Count);
            Assert.IsTrue(_tree.IsValidBst());
        }

        // ---------------------------------------------------------------- helpers

        List<int> InOrderValues()
        {
            _tree.Traverse(TraversalKind.InOrder, _ids);
            List<int> values = new List<int>(_ids.Count);
            for (int i = 0; i < _ids.Count; i++) values.Add(_tree.GetById(_ids[i]).Value);
            return values;
        }

        void CollectAssert(TraversalKind kind, int[] expected)
        {
            _tree.Traverse(kind, _ids);
            List<int> values = new List<int>(_ids.Count);
            for (int i = 0; i < _ids.Count; i++) values.Add(_tree.GetById(_ids[i]).Value);
            Assert.AreEqual(new List<int>(expected), values);
        }
    }
}
