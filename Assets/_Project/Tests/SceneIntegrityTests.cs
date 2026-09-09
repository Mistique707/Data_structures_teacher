using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Pivot.Structures;
using Pivot.VFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Pivot.Tests
{
    /// <summary>
    /// Opens the authored scene and checks it is actually there and actually wired.
    ///
    /// This exists because a phase once ended with working shaders, materials, prefabs
    /// and a reconciler, and nothing at all to open: the project had zero .unity files
    /// and Build Settings pointed at a deleted scene. A screenshot from a throwaway
    /// harness is not evidence that the project runs. These tests are.
    ///
    /// The scene is opened additively and closed again so running the suite inside the
    /// Editor does not disturb whatever the developer had open.
    /// </summary>
    public class SceneIntegrityTests
    {
        const string TreeLabPath = "Assets/_Project/Scenes/01_TreeLab.unity";

        /// <summary>The authored starting tree. Seven nodes, balanced, height two.</summary>
        static readonly int[] SeedValues = { 50, 30, 70, 20, 40, 60, 80 };

        Scene _scene;

        [SetUp]
        public void OpenScene()
        {
            Assert.IsTrue(File.Exists(TreeLabPath),
                "The Tree Lab scene is missing. Every phase has to leave a scene that opens.");

            _scene = EditorSceneManager.OpenScene(TreeLabPath, OpenSceneMode.Additive);
        }

        [TearDown]
        public void CloseScene()
        {
            if (_scene.IsValid()) EditorSceneManager.CloseScene(_scene, true);
        }

        static List<T> FindAll<T>(Scene scene) where T : Component
        {
            List<T> found = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                found.AddRange(root.GetComponentsInChildren<T>(true));
            }

            return found;
        }

        [Test]
        public void TreeLabIsInBuildSettingsAndEnabled()
        {
            foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes)
            {
                if (entry.path != TreeLabPath) continue;
                Assert.IsTrue(entry.enabled, "Tree Lab is in Build Settings but disabled.");
                return;
            }

            Assert.Fail("Tree Lab is not in Build Settings.");
        }

        [Test]
        public void BuildSettingsHasNoDanglingScenePaths()
        {
            foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes)
            {
                Assert.IsTrue(File.Exists(entry.path),
                    "Build Settings points at a scene that does not exist: " + entry.path);
            }
        }

        [Test]
        public void SeedTreeIsAuthoredAsRealGameObjects()
        {
            List<NodeView> nodes = FindAll<NodeView>(_scene);

            Assert.AreEqual(SeedValues.Length, nodes.Count,
                "The seed tree must exist in the scene file before Play, not be spawned at boot.");

            // Named after their values, so the hierarchy is readable in the Inspector.
            List<string> names = new List<string>();
            for (int i = 0; i < nodes.Count; i++) names.Add(nodes[i].name);

            for (int i = 0; i < SeedValues.Length; i++)
            {
                Assert.Contains("Node " + SeedValues[i], names,
                    "Missing an authored node for seed value " + SeedValues[i]);
            }
        }

        [Test]
        public void SeedTreeHasOneEdgeFewerThanItsNodes()
        {
            List<NodeView> nodes = FindAll<NodeView>(_scene);
            List<EdgeView> edges = FindAll<EdgeView>(_scene);

            Assert.AreEqual(nodes.Count - 1, edges.Count,
                "A tree of n nodes has exactly n-1 edges.");
        }

        [Test]
        public void NodesAreSpreadOutRatherThanStackedAtTheOrigin()
        {
            List<NodeView> nodes = FindAll<NodeView>(_scene);

            HashSet<Vector3> positions = new HashSet<Vector3>();
            for (int i = 0; i < nodes.Count; i++)
            {
                positions.Add(nodes[i].transform.position);
            }

            Assert.AreEqual(nodes.Count, positions.Count,
                "Two nodes share a position, so the layout was never applied.");
        }

        [Test]
        public void SceneHasACameraALightRigAndAnEnvironmentController()
        {
            Assert.AreEqual(1, FindAll<Camera>(_scene).Count, "Expected exactly one camera.");
            Assert.AreEqual(1, FindAll<LightRig>(_scene).Count, "Expected exactly one light rig.");
            Assert.AreEqual(1, FindAll<EnvironmentController>(_scene).Count,
                "Expected exactly one environment controller.");

            List<Light> lights = FindAll<Light>(_scene);
            Assert.AreEqual(2, lights.Count, "Expected a key and a fill, and nothing else.");
        }

        [Test]
        public void SceneHasNoMissingScripts()
        {
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    Component[] components = t.GetComponents<Component>();
                    for (int i = 0; i < components.Length; i++)
                    {
                        Assert.IsNotNull(components[i],
                            "Missing script on '" + t.name + "'. A reference broke.");
                    }
                }
            }
        }

        [Test]
        public void SceneHasNoMissingMeshOrMaterialReferences()
        {
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    Assert.IsNotNull(filter.sharedMesh,
                        "Missing mesh on '" + filter.name + "'.");
                }

                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Material[] materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        Assert.IsNotNull(materials[i],
                            "Missing material on '" + renderer.name + "'.");
                    }
                }
            }
        }

        [Test]
        public void HierarchyIsGroupedRatherThanDumpedFlatAtRoot()
        {
            List<string> roots = new List<string>();
            foreach (GameObject root in _scene.GetRootGameObjects()) roots.Add(root.name);

            Assert.Contains("--- Environment ---", roots);
            Assert.Contains("--- Tree ---", roots);
            Assert.Contains("--- Systems ---", roots);

            Assert.AreEqual(3, roots.Count,
                "Only the named group parents belong at the root of the scene.");
        }
    }
}
