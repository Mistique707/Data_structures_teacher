using System.Collections.Generic;
using NUnit.Framework;
using Pivot.Core;
using Pivot.Core.Commands;

namespace Pivot.Tests
{
    public class CommandHistoryTests
    {
        static readonly int[] Seed = { 50, 30, 70, 20, 40, 60, 80 };

        BinarySearchTree _tree;
        CommandHistory _history;
        List<TreeStep> _steps;
        List<int> _ids;

        [SetUp]
        public void SetUp()
        {
            _tree = new BinarySearchTree();
            _tree.BuildFrom(Seed);
            _history = new CommandHistory();
            _steps = new List<TreeStep>(32);
            _ids = new List<int>(32);
        }

        List<int> Values()
        {
            _tree.Traverse(TraversalKind.InOrder, _ids);
            List<int> values = new List<int>(_ids.Count);
            for (int i = 0; i < _ids.Count; i++) values.Add(_tree.GetById(_ids[i]).Value);
            return values;
        }

        [Test]
        public void FreshHistory_HasNothingToUndoOrRedo()
        {
            Assert.IsFalse(_history.CanUndo);
            Assert.IsFalse(_history.CanRedo);
            Assert.IsFalse(_history.Undo());
            Assert.IsFalse(_history.Redo());
        }

        [Test]
        public void Undo_TakesBackAnInsert()
        {
            Assert.IsTrue(_history.Run(new InsertCommand(_tree, 45), _steps));
            Assert.AreEqual(8, _tree.Count);

            Assert.IsTrue(_history.Undo());
            Assert.AreEqual(7, _tree.Count);
            Assert.IsNull(_tree.Find(45));
            Assert.AreEqual(new List<int> { 20, 30, 40, 50, 60, 70, 80 }, Values());
        }

        [Test]
        public void Undo_PutsBackADeletedSubtreeExactly()
        {
            List<int> idsBefore = new List<int>();
            _tree.CollectIds(idsBefore);

            _history.Run(new DeleteCommand(_tree, _tree.Find(30).Id), _steps);
            Assert.AreEqual(6, _tree.Count);

            Assert.IsTrue(_history.Undo());

            List<int> idsAfter = new List<int>();
            _tree.CollectIds(idsAfter);
            Assert.AreEqual(idsBefore, idsAfter, "undo restores node identity, not just shape");
            Assert.AreEqual(new List<int> { 20, 30, 40, 50, 60, 70, 80 }, Values());
            Assert.IsTrue(_tree.IsValidBst());
        }

        [Test]
        public void Undo_AndRedo_WalkTheSameHistoryBothWays()
        {
            _history.Run(new InsertCommand(_tree, 45), _steps);
            _history.Run(new RotateCommand(_tree, _tree.RootId, true), _steps);
            _history.Run(new DeleteCommand(_tree, _tree.Find(20).Id), _steps);

            List<int> atTheTop = Values();

            Assert.IsTrue(_history.Undo());
            Assert.IsTrue(_history.Undo());
            Assert.IsTrue(_history.Undo());
            Assert.IsFalse(_history.CanUndo);
            Assert.AreEqual(new List<int> { 20, 30, 40, 50, 60, 70, 80 }, Values());

            Assert.IsTrue(_history.Redo());
            Assert.IsTrue(_history.Redo());
            Assert.IsTrue(_history.Redo());
            Assert.IsFalse(_history.CanRedo);
            Assert.AreEqual(atTheTop, Values());
        }

        [Test]
        public void RunningSomethingNew_ThrowsAwayTheRedoBranch()
        {
            _history.Run(new InsertCommand(_tree, 45), _steps);
            _history.Undo();
            Assert.IsTrue(_history.CanRedo);

            _history.Run(new InsertCommand(_tree, 55), _steps);

            Assert.IsFalse(_history.CanRedo);
            Assert.IsNull(_tree.Find(45));
            Assert.IsNotNull(_tree.Find(55));
        }

        [Test]
        public void ACommandThatChangedNothing_IsNotRecorded()
        {
            Assert.IsFalse(_history.Run(new InsertCommand(_tree, 30), _steps), "duplicate insert");
            Assert.IsFalse(_history.CanUndo);

            int leafId = _tree.Find(20).Id;
            Assert.IsFalse(_history.Run(new RotateCommand(_tree, leafId, true), _steps));
            Assert.IsFalse(_history.CanUndo);

            Assert.IsFalse(_history.Run(
                new ReparentCommand(_tree, _tree.Find(20).Id, _tree.Find(70).Id, ChildSide.Left), _steps));
            Assert.IsFalse(_history.CanUndo, "a refused drop must not eat the user's next undo");
        }

        [Test]
        public void UndoNames_DescribeWhatWillBeTakenBack()
        {
            _history.Run(new InsertCommand(_tree, 45), _steps);
            Assert.AreEqual("Insert 45", _history.NextUndoName);

            _history.Run(new DeleteCommand(_tree, _tree.Find(80).Id), _steps);
            Assert.AreEqual("Delete 80", _history.NextUndoName);

            _history.Undo();
            Assert.AreEqual("Delete 80", _history.NextRedoName);
            Assert.AreEqual("Insert 45", _history.NextUndoName);
        }

        [Test]
        public void Rotate_UndoesBackToTheOriginalShape()
        {
            _history.Run(new RotateCommand(_tree, _tree.Find(30).Id, true), _steps);
            Assert.AreEqual(40, _tree.Root.Left.Value);

            _history.Undo();

            Assert.AreEqual(30, _tree.Root.Left.Value);
            Assert.AreEqual(20, _tree.Find(30).Left.Value);
            Assert.AreEqual(40, _tree.Find(30).Right.Value);
        }

        [Test]
        public void Reset_RebuildsTheSeedAndIsItselfUndoable()
        {
            _history.Run(new InsertCommand(_tree, 45), _steps);
            _history.Run(new DeleteCommand(_tree, _tree.Find(70).Id), _steps);

            Assert.IsTrue(_history.Run(new ResetCommand(_tree, Seed), _steps));
            Assert.AreEqual(new List<int> { 20, 30, 40, 50, 60, 70, 80 }, Values());
            Assert.AreEqual(2, _tree.Height);

            Assert.IsTrue(_history.Undo());
            Assert.IsNull(_tree.Find(70));
            Assert.IsNotNull(_tree.Find(45));
        }

        [Test]
        public void HistoryStopsGrowingOnceItHitsCapacity()
        {
            _history.Capacity = 3;
            for (int value = 1; value <= 10; value++)
            {
                _history.Run(new InsertCommand(_tree, value), _steps);
            }

            Assert.AreEqual(3, _history.DoneCount);

            while (_history.Undo())
            {
            }

            Assert.AreEqual(14, _tree.Count, "only the three retained inserts can be taken back");
        }

        [Test]
        public void Clear_DropsBothStacksWithoutTouchingTheTree()
        {
            _history.Run(new InsertCommand(_tree, 45), _steps);
            _history.Undo();
            _history.Clear();

            Assert.IsFalse(_history.CanUndo);
            Assert.IsFalse(_history.CanRedo);
            Assert.AreEqual(7, _tree.Count);
        }

        [Test]
        public void Module_ValidatesThroughItsOwnRuleListAndResetsToItsSeed()
        {
            BstModule module = new BstModule(Seed);

            Assert.AreEqual(7, module.Model.Count);
            Assert.AreEqual(4, module.Rules.Count);

            int twenty = module.Tree.Find(20).Id;
            int seventy = module.Tree.Find(70).Id;
            Assert.IsFalse(module.Validate(new DropAttempt(twenty, seventy, ChildSide.Left)).Ok);

            module.Tree.Delete(30, null);
            Assert.AreEqual(6, module.Model.Count);

            module.ResetToSeed();
            Assert.AreEqual(7, module.Model.Count);
            Assert.IsNotNull(module.Tree.Find(30));
        }
    }
}
