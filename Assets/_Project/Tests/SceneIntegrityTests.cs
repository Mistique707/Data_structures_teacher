using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Pivot.Interaction;
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
    ///
    /// These check the scene as Unity loads it, which is the right level for structure:
    /// counts, names, wiring, missing references. It is the wrong level for authored
    /// data, because components here are [ExecuteAlways] and can repair themselves on
    /// load — a live transform check would pass even if the file held nothing. Anything
    /// about what was actually saved belongs in <see cref="SceneFileTests"/>, which
    /// reads the .unity as text.
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
            Assert.AreEqual(1, FindAll<LightRig>(_scene).Count, "Expected exactly one light rig.");
            Assert.AreEqual(1, FindAll<EnvironmentController>(_scene).Count,
                "Expected exactly one environment controller.");

            List<Light> lights = FindAll<Light>(_scene);
            Assert.AreEqual(2, lights.Count, "Expected a key and a fill, and nothing else.");
        }

        /// <summary>
        /// Exactly one rig, present in the committed scene and already enabled. Not a
        /// prefab to drag in, and never both at once: two active rigs means two cameras
        /// and two audio listeners, which fails quietly rather than loudly.
        /// </summary>
        [Test]
        public void ExactlyOneRigIsPresentAndEnabled()
        {
            List<RigManager> managers = FindAll<RigManager>(_scene);
            Assert.AreEqual(1, managers.Count, "Expected exactly one RigManager.");

            List<DesktopLocomotion> desktop = FindAll<DesktopLocomotion>(_scene);
            Assert.AreEqual(1, desktop.Count, "Expected exactly one desktop rig.");

            int activeRigs = 0;
            if (desktop[0].gameObject.activeInHierarchy) activeRigs++;

            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                Transform vr = root.transform.Find("VRRig");
                if (vr != null && vr.gameObject.activeInHierarchy) activeRigs++;
            }

            Assert.AreEqual(1, activeRigs,
                "Exactly one rig must be enabled in the saved scene; found " + activeRigs + ".");
        }

        [Test]
        public void TheSceneHasExactlyOneCameraAndOneAudioListener()
        {
            int cameras = 0;
            int listeners = 0;

            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    if (camera.gameObject.activeInHierarchy) cameras++;
                }

                foreach (AudioListener listener in root.GetComponentsInChildren<AudioListener>(true))
                {
                    if (listener.gameObject.activeInHierarchy) listeners++;
                }
            }

            Assert.AreEqual(1, cameras, "Expected one active camera, found " + cameras + ".");
            Assert.AreEqual(1, listeners, "Expected one active audio listener, found " + listeners + ".");
        }

        [Test]
        public void InputGoesThroughTheActionsAsset()
        {
            List<PivotActions> actions = FindAll<PivotActions>(_scene);
            Assert.AreEqual(1, actions.Count, "Expected exactly one PivotActions in the scene.");
            Assert.IsNotNull(actions[0].Asset,
                "PivotActions has no Input Actions asset assigned, so nothing is bound.");
        }

        [Test]
        public void NodesAreGrabbable()
        {
            List<NodeView> nodes = FindAll<NodeView>(_scene);
            Assert.Greater(nodes.Count, 0);

            for (int i = 0; i < nodes.Count; i++)
            {
                Assert.IsNotNull(
                    nodes[i].GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>(),
                    "'" + nodes[i].name + "' has no XRGrabInteractable, so no rig can pick it up.");
            }
        }

        /// <summary>
        /// Ids are what let the view reconcile against a model. They became serialised
        /// fields after the scene was authored, so every view sat at id 0 and TreeView
        /// adopted nothing at all — silently, because a tree that never changes looks
        /// identical either way.
        /// </summary>
        [Test]
        public void AuthoredViewsCarryTheirModelIds()
        {
            List<NodeView> nodes = FindAll<NodeView>(_scene);
            Assert.Greater(nodes.Count, 0);

            HashSet<int> seen = new HashSet<int>();
            for (int i = 0; i < nodes.Count; i++)
            {
                Assert.AreNotEqual(0, nodes[i].NodeId,
                    "'" + nodes[i].name + "' has no model id, so nothing can reconcile it.");
                Assert.IsTrue(seen.Add(nodes[i].NodeId),
                    "Two nodes share id " + nodes[i].NodeId + ".");
            }

            List<EdgeView> edges = FindAll<EdgeView>(_scene);
            for (int i = 0; i < edges.Count; i++)
            {
                Assert.AreNotEqual(0, edges[i].ParentId, "'" + edges[i].name + "' has no parent id.");
                Assert.AreNotEqual(0, edges[i].ChildId, "'" + edges[i].name + "' has no child id.");
                Assert.Contains(edges[i].ParentId, new List<int>(seen), "Edge points at a missing node.");
                Assert.Contains(edges[i].ChildId, new List<int>(seen), "Edge points at a missing node.");
            }
        }

        /// <summary>
        /// CharacterController defaults minMoveDistance to 0.001 m and DISCARDS any move
        /// shorter than that. At a high frame rate a normal walking speed produces
        /// per-frame steps below the threshold, so the rig crawls while its velocity
        /// reads as correct — which is exactly how this hid.
        /// </summary>
        [Test]
        public void TheRigDoesNotDiscardSmallMoves()
        {
            List<CharacterController> controllers = FindAll<CharacterController>(_scene);
            Assert.AreEqual(1, controllers.Count, "Expected one CharacterController on the rig.");

            Assert.AreEqual(0f, controllers[0].minMoveDistance, 0.0001f,
                "minMoveDistance must be 0, or fast frames move the rig nowhere.");
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

        /// <summary>
        /// Reads a serialised field, so this does reflect the file. The stronger check
        /// on the raw YAML lives in SceneFileTests.SceneFileRecordsDistinctDepthColours.
        /// </summary>
        [Test]
        public void AuthoredNodesKeepDistinctDepthColours()
        {
            List<NodeView> nodes = FindAll<NodeView>(_scene);

            HashSet<Color> colours = new HashSet<Color>();
            for (int i = 0; i < nodes.Count; i++) colours.Add(nodes[i].AuthoredColour);

            Assert.GreaterOrEqual(colours.Count, 3,
                "Seven nodes across three depths should carry at least three distinct " +
                "colours. All one colour means the property block was never persisted.");
        }

        /// <summary>
        /// Confirms the label ends up outside the bubble once the scene is loaded. Note
        /// what this does not prove: NodeView is [ExecuteAlways] and parks the pivot in
        /// OnEnable, so this would pass even on a scene file that saved the label at the
        /// node centre. SceneFileTests.SceneFileRecordsLabelPivotsOutsideTheirBubbles is
        /// the test that actually guards the saved data.
        /// </summary>
        [Test]
        public void AuthoredNodeLabelsSitOutsideTheirBubbles()
        {
            List<NodeView> nodes = FindAll<NodeView>(_scene);
            Assert.Greater(nodes.Count, 0);

            for (int i = 0; i < nodes.Count; i++)
            {
                TMPro.TextMeshPro label = nodes[i].GetComponentInChildren<TMPro.TextMeshPro>(true);
                Assert.IsNotNull(label, "No label under '" + nodes[i].name + "'.");

                float distance = Vector3.Distance(
                    label.transform.position, nodes[i].transform.position);

                Assert.Greater(distance, 0.1f,
                    "Label on '" + nodes[i].name + "' is buried inside the bubble.");

                Assert.IsNotEmpty(label.text, "Label on '" + nodes[i].name + "' has no number.");
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
            Assert.Contains("--- Rig ---", roots);

            Assert.AreEqual(4, roots.Count,
                "Only the named group parents belong at the root of the scene.");
        }
    }
}
