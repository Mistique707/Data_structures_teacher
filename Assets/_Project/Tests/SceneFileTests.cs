using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;

namespace Pivot.Tests
{
    /// <summary>
    /// Reads the saved .unity file as text and asserts against what is actually written
    /// to disk.
    ///
    /// This exists because opening a scene is not the same as inspecting it. NodeView is
    /// [ExecuteAlways], so OnEnable repositions the label pivot the moment the scene
    /// loads — which means a test that measures the live transform passes even when the
    /// file records the label buried at the node centre. It would assert the code works,
    /// not that the authored data survived, and would have gone green on the broken
    /// version. These tests cannot: nothing runs between the file and the assertion.
    ///
    /// <see cref="SceneFileTests.NegativeControlCatchesAnUnauthoredScene"/> proves the
    /// parser actually discriminates, rather than passing because it found nothing.
    /// </summary>
    public class SceneFileTests
    {
        const string TreeLabPath = "Assets/_Project/Scenes/01_TreeLab.unity";

        /// <summary>A prefab instance override, exactly as the YAML records it.</summary>
        readonly struct Override
        {
            public readonly string Target;
            public readonly string Path;
            public readonly string Value;

            public Override(string target, string path, string value)
            {
                Target = target;
                Path = path;
                Value = value;
            }
        }

        /// <summary>
        /// Pulls every recorded prefab-instance override out of a scene file, grouped by
        /// the PrefabInstance block it belongs to.
        ///
        /// Grouping by the block matters: the "target" fileID inside a modification
        /// points at the object in the SOURCE prefab, so it is identical across every
        /// instance of that prefab. Keying on it collapses seven differently coloured
        /// nodes into one entry, which is a false failure rather than a real one.
        /// </summary>
        static List<Override> ReadOverrides(string[] lines)
        {
            List<Override> found = new List<Override>();

            int block = -1;
            string path = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];

                if (raw.StartsWith("--- !u!1001 "))
                {
                    block++;
                    path = null;
                    continue;
                }

                if (raw.StartsWith("--- !u!") || block < 0) continue;

                string line = raw.Trim();

                if (line.StartsWith("propertyPath:"))
                {
                    path = line.Substring("propertyPath:".Length).Trim();
                    continue;
                }

                if (!line.StartsWith("value:") || path == null) continue;

                found.Add(new Override(
                    block.ToString(CultureInfo.InvariantCulture),
                    path,
                    line.Substring("value:".Length).Trim()));
                path = null;
            }

            return found;
        }

        static float ParseFloat(string raw)
        {
            float value;
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value
                : 0f;
        }

        /// <summary>Distinct serialised node colours, grouped by the component they belong to.</summary>
        static HashSet<string> DistinctNodeColours(string[] lines)
        {
            Dictionary<string, string[]> byTarget = new Dictionary<string, string[]>();

            foreach (Override entry in ReadOverrides(lines))
            {
                int channel;
                if (entry.Path == "_colour.r") channel = 0;
                else if (entry.Path == "_colour.g") channel = 1;
                else if (entry.Path == "_colour.b") channel = 2;
                else continue;

                string[] rgb;
                if (!byTarget.TryGetValue(entry.Target, out rgb))
                {
                    rgb = new string[3];
                    byTarget[entry.Target] = rgb;
                }

                rgb[channel] = entry.Value;
            }

            HashSet<string> distinct = new HashSet<string>();
            foreach (KeyValuePair<string, string[]> pair in byTarget)
            {
                distinct.Add(string.Join(",", pair.Value));
            }

            return distinct;
        }

        /// <summary>Label pivots pushed clear of the node centre, as recorded in a file.</summary>
        static int LabelPivotsPushedOut(string[] lines, float minimum)
        {
            int count = 0;

            foreach (Override entry in ReadOverrides(lines))
            {
                if (entry.Path != "m_LocalPosition.z") continue;

                float z = ParseFloat(entry.Value);
                if (z < 0f) z = -z;
                if (z >= minimum) count++;
            }

            return count;
        }

        /// <summary>
        /// The resting position of the label pivot as the prefab records it. Reading the
        /// raw z of every Transform in the file and taking the largest magnitude is
        /// enough: nothing else in the node prefab is offset along z.
        /// </summary>
        static float LargestTransformZ(string[] lines)
        {
            float largest = 0f;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith("m_LocalPosition:")) continue;

                int marker = line.IndexOf("z:", System.StringComparison.Ordinal);
                if (marker < 0) continue;

                string tail = line.Substring(marker + 2).Trim().TrimEnd('}').Trim();
                float z = ParseFloat(tail);
                if (z < 0f) z = -z;
                if (z > largest) largest = z;
            }

            return largest;
        }

        static string[] TreeLabLines()
        {
            Assert.IsTrue(File.Exists(TreeLabPath), "Scene file missing: " + TreeLabPath);
            return File.ReadAllLines(TreeLabPath);
        }

        // ------------------------------------------------------------------- real

        [Test]
        public void SceneFileRecordsDistinctDepthColours()
        {
            HashSet<string> colours = DistinctNodeColours(TreeLabLines());

            Assert.GreaterOrEqual(colours.Count, 3,
                "The saved scene records " + colours.Count + " distinct node colour(s). " +
                "A MaterialPropertyBlock is not serialised, so if the colour is not " +
                "written as a prefab override the tree reloads all one colour.");
        }

        [Test]
        public void SceneFileRecordsOneColourPerSeedNode()
        {
            HashSet<string> colours = DistinctNodeColours(TreeLabLines());

            // Seven nodes across three depths: three distinct colours, seven records.
            Assert.LessOrEqual(colours.Count, 7, "More colour records than there are nodes.");
        }

        /// <summary>
        /// The label offset belongs to the prefab, so every instance inherits it and a
        /// hand edit is not fought by anything at load. This caught the offset existing
        /// only as runtime behaviour: the scene recorded every pivot at z = 0 and the
        /// numbers appeared outside the bubbles purely because ExecuteAlways moved them.
        /// </summary>
        [Test]
        public void NodePrefabPlacesItsLabelOutsideTheBubble()
        {
            const string prefabPath = "Assets/_Project/Prefabs/Node.prefab";
            Assert.IsTrue(File.Exists(prefabPath), "Missing " + prefabPath);

            // Node radius is 0.14 m, so anything at least 0.1 m out clears the sphere.
            float offset = LargestTransformZ(File.ReadAllLines(prefabPath));

            Assert.GreaterOrEqual(offset, 0.1f,
                "The node prefab parks its label pivot " + offset + " m from the centre, " +
                "which is inside the bubble. The number has to be saved outside it, not " +
                "moved there at load time.");
        }

        // -------------------------------------------------------- negative control

        /// <summary>
        /// The guard has to fail on bad data or it guards nothing. This feeds the same
        /// parsers a scene that was never authored properly — every node the same
        /// fallback colour, every label pivot at the origin — and asserts they report
        /// it. Without this, the tests above could pass by finding nothing at all.
        /// </summary>
        [Test]
        public void NegativeControlCatchesAnUnauthoredScene()
        {
            // Two separate PrefabInstance blocks, both the fallback white, and a pivot
            // left at the origin. The block headers matter: without them the parser
            // would read nothing and the control would pass vacuously.
            string[] broken =
            {
                "--- !u!1001 &1",
                "PrefabInstance:",
                "  m_Modification:",
                "    m_Modifications:",
                "    - target: {fileID: 111, guid: aaa, type: 3}",
                "      propertyPath: _colour.r",
                "      value: 1",
                "      objectReference: {fileID: 0}",
                "    - target: {fileID: 111, guid: aaa, type: 3}",
                "      propertyPath: _colour.g",
                "      value: 1",
                "      objectReference: {fileID: 0}",
                "    - target: {fileID: 111, guid: aaa, type: 3}",
                "      propertyPath: _colour.b",
                "      value: 1",
                "      objectReference: {fileID: 0}",
                "    - target: {fileID: 222, guid: aaa, type: 3}",
                "      propertyPath: m_LocalPosition.z",
                "      value: 0",
                "      objectReference: {fileID: 0}",
                "--- !u!1001 &2",
                "PrefabInstance:",
                "  m_Modification:",
                "    m_Modifications:",
                "    - target: {fileID: 111, guid: aaa, type: 3}",
                "      propertyPath: _colour.r",
                "      value: 1",
                "      objectReference: {fileID: 0}",
                "    - target: {fileID: 111, guid: aaa, type: 3}",
                "      propertyPath: _colour.g",
                "      value: 1",
                "      objectReference: {fileID: 0}",
                "    - target: {fileID: 111, guid: aaa, type: 3}",
                "      propertyPath: _colour.b",
                "      value: 1",
                "      objectReference: {fileID: 0}",
                "    - target: {fileID: 222, guid: aaa, type: 3}",
                "      propertyPath: m_LocalPosition.z",
                "      value: 0",
                "      objectReference: {fileID: 0}"
            };

            // The parser has to engage at all, or everything below is vacuous.
            Assert.AreEqual(8, ReadOverrides(broken).Count,
                "The control data itself did not parse, so it proves nothing.");

            Assert.AreEqual(1, DistinctNodeColours(broken).Count,
                "Two nodes sharing one colour must read as a single distinct colour.");

            Assert.AreEqual(0, LabelPivotsPushedOut(broken, 0.1f),
                "A label pivot left at the origin must not count as pushed out.");

            // And a well formed one has to read as two, or the grouping is broken the
            // way the first version of this parser was.
            string[] healthy = (string[])broken.Clone();
            for (int i = 0; i < healthy.Length; i++)
            {
                if (healthy[i] == "      value: 1" && i > 20) healthy[i] = "      value: 0.4";
            }

            Assert.AreEqual(2, DistinctNodeColours(healthy).Count,
                "Two blocks with different colours must read as two distinct colours. " +
                "One means the parser is keying on the source prefab object again.");
        }

        /// <summary>And the parser has to actually find things, not silently read zero.</summary>
        [Test]
        public void ParserFindsOverridesInTheRealScene()
        {
            List<Override> all = ReadOverrides(TreeLabLines());

            Assert.Greater(all.Count, 20,
                "Only " + all.Count + " overrides parsed out of the scene. The file " +
                "format changed and these guards are reading nothing.");
        }
    }
}
