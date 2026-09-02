using System.Collections.Generic;
using NUnit.Framework;
using Pivot.Core;

namespace Pivot.Tests
{
    public class TidyTreeLayoutTests
    {
        static readonly int[] Seed = { 50, 30, 70, 20, 40, 60, 80 };

        BinarySearchTree _tree;
        TidyTreeLayout _layout;
        Dictionary<int, LayoutPoint> _points;

        [SetUp]
        public void SetUp()
        {
            _tree = new BinarySearchTree();
            _layout = new TidyTreeLayout { SiblingSpacing = 1f, LevelSpacing = 1f };
            _points = new Dictionary<int, LayoutPoint>(32);
        }

        LayoutPoint Of(int value)
        {
            return _points[_tree.Find(value).Id];
        }

        [Test]
        public void EmptyTree_ProducesNoPoints()
        {
            _layout.Compute(_tree, _points);
            Assert.AreEqual(0, _points.Count);
        }

        [Test]
        public void EveryLiveNodeGetsExactlyOnePoint()
        {
            _tree.BuildFrom(Seed);
            _layout.Compute(_tree, _points);
            Assert.AreEqual(_tree.Count, _points.Count);
        }

        [Test]
        public void DepthDrivesTheVerticalPosition()
        {
            _tree.BuildFrom(Seed);
            _layout.Compute(_tree, _points);

            Assert.AreEqual(0f, Of(50).Y, 1e-4f);
            Assert.AreEqual(1f, Of(30).Y, 1e-4f);
            Assert.AreEqual(1f, Of(70).Y, 1e-4f);
            Assert.AreEqual(2f, Of(20).Y, 1e-4f);
            Assert.AreEqual(2f, Of(80).Y, 1e-4f);
        }

        [Test]
        public void ParentsSitCentredOverTheirTwoChildren()
        {
            _tree.BuildFrom(Seed);
            _layout.Compute(_tree, _points);

            Assert.AreEqual(0.5f * (Of(20).X + Of(40).X), Of(30).X, 1e-4f);
            Assert.AreEqual(0.5f * (Of(60).X + Of(80).X), Of(70).X, 1e-4f);
            Assert.AreEqual(0.5f * (Of(30).X + Of(70).X), Of(50).X, 1e-4f);
        }

        [Test]
        public void InOrderPositionMatchesLeftToRightOnScreen()
        {
            _tree.BuildFrom(Seed);
            _layout.Compute(_tree, _points);

            List<int> ids = new List<int>();
            _tree.Traverse(TraversalKind.InOrder, ids);

            for (int i = 1; i < ids.Count; i++)
            {
                Assert.Less(_points[ids[i - 1]].X, _points[ids[i]].X,
                    "a tidy tree must never cross its own branches");
            }
        }

        [Test]
        public void NodesOnTheSameRowNeverOverlap()
        {
            _tree.BuildFrom(new[] { 50, 30, 70, 20, 40, 60, 80, 10, 25, 35, 45, 55, 65, 75, 85 });
            _layout.Compute(_tree, _points);

            Dictionary<float, List<float>> rows = new Dictionary<float, List<float>>();
            foreach (KeyValuePair<int, LayoutPoint> pair in _points)
            {
                List<float> row;
                if (!rows.TryGetValue(pair.Value.Y, out row))
                {
                    row = new List<float>();
                    rows[pair.Value.Y] = row;
                }

                row.Add(pair.Value.X);
            }

            foreach (KeyValuePair<float, List<float>> row in rows)
            {
                row.Value.Sort();
                for (int i = 1; i < row.Value.Count; i++)
                {
                    Assert.GreaterOrEqual(row.Value[i] - row.Value[i - 1], _layout.SiblingSpacing - 1e-4f,
                        "row at y=" + row.Key + " has two nodes closer than the sibling spacing");
                }
            }
        }

        [Test]
        public void ALoneLeftChildLeansLeftOfItsParent()
        {
            _tree.BuildFrom(new[] { 50, 30 });
            _layout.Compute(_tree, _points);

            Assert.Less(Of(30).X, Of(50).X,
                "without a phantom sibling a single child would sit dead centre and read as ambiguous");
        }

        [Test]
        public void ALoneRightChildLeansRightOfItsParent()
        {
            _tree.BuildFrom(new[] { 50, 70 });
            _layout.Compute(_tree, _points);

            Assert.Greater(Of(70).X, Of(50).X);
        }

        [Test]
        public void ADegenerateChainStaircasesInsteadOfStackingUp()
        {
            _tree.BuildFrom(new[] { 10, 20, 30, 40, 50 });
            _layout.Compute(_tree, _points);

            Assert.Less(Of(10).X, Of(20).X);
            Assert.Less(Of(20).X, Of(30).X);
            Assert.Less(Of(30).X, Of(40).X);
            Assert.Less(Of(40).X, Of(50).X);
        }

        [Test]
        public void DeepInnerSubtreesArePushedApartRatherThanCollided()
        {
            // The classic Reingold-Tilford stress case: two deep subtrees whose inner
            // contours would touch if only the immediate children were considered.
            _tree.BuildFrom(new[] { 50, 20, 80, 10, 30, 70, 90, 35, 65 });
            _layout.Compute(_tree, _points);

            Assert.Less(Of(35).X, Of(65).X);
            Assert.GreaterOrEqual(Of(65).X - Of(35).X, _layout.SiblingSpacing - 1e-4f);
        }

        [Test]
        public void TheLayoutIsCentredOnItsOwnBounds()
        {
            _tree.BuildFrom(new[] { 10, 20, 30, 40 });
            _layout.Compute(_tree, _points);

            float min = float.MaxValue;
            float max = float.MinValue;
            foreach (KeyValuePair<int, LayoutPoint> pair in _points)
            {
                if (pair.Value.X < min) min = pair.Value.X;
                if (pair.Value.X > max) max = pair.Value.X;
            }

            Assert.AreEqual(0f, 0.5f * (min + max), 1e-4f);
        }

        [Test]
        public void TheSameTreeAlwaysLaysOutTheSameWay()
        {
            _tree.BuildFrom(Seed);
            _layout.Compute(_tree, _points);
            Dictionary<int, LayoutPoint> first = new Dictionary<int, LayoutPoint>(_points);

            for (int run = 0; run < 3; run++)
            {
                _layout.Compute(_tree, _points);
                foreach (KeyValuePair<int, LayoutPoint> pair in first)
                {
                    Assert.AreEqual(pair.Value.X, _points[pair.Key].X, 1e-5f);
                    Assert.AreEqual(pair.Value.Y, _points[pair.Key].Y, 1e-5f);
                }
            }
        }
    }
}
