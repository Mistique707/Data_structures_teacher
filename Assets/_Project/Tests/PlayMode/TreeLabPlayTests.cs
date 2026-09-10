using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Pivot.Interaction;
using Pivot.Structures;
using Pivot.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Pivot.PlayTests
{
    /// <summary>
    /// Actually presses Play.
    ///
    /// The Edit Mode scene tests prove the scene is wired; they cannot prove it runs. A
    /// null reference in an Awake, an action map that never enabled, a rig that picked
    /// the wrong branch — none of that shows up until something executes. These load the
    /// scene the way the player does and watch for it going wrong.
    /// </summary>
    public class TreeLabPlayTests
    {
        const string SceneName = "01_TreeLab";

        readonly List<string> _errors = new List<string>();

        [SetUp]
        public void Listen()
        {
            _errors.Clear();
            Application.logMessageReceived += Record;
        }

        [TearDown]
        public void StopListening()
        {
            Application.logMessageReceived -= Record;
        }

        void Record(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _errors.Add(type + ": " + message);
            }
        }

        IEnumerator LoadLab()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);

            // A few frames so Awake, OnEnable and the first Updates have all run.
            for (int i = 0; i < 5; i++) yield return null;
        }

        void AssertNoErrors()
        {
            if (_errors.Count == 0) return;
            Assert.Fail("The scene logged " + _errors.Count + " error(s) on load:\n  " +
                        string.Join("\n  ", _errors));
        }

        [UnityTest]
        public IEnumerator TheLabLoadsAndRunsWithoutErrors()
        {
            yield return LoadLab();
            AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator TheDesktopRigWinsWhenThereIsNoHeadset()
        {
            yield return LoadLab();

            RigManager manager = RigManager.Instance;
            Assert.IsNotNull(manager, "No RigManager came up.");

            // Nothing here has a headset, and no XR loader starts in a test run, so the
            // auto path must land on desktop. If this ever fails on a machine with a
            // headset attached, that is the auto detection working, not a regression.
            Assert.AreEqual(ActiveRig.Desktop, manager.Mode);
            Assert.IsNotNull(manager.ActiveCamera, "The rig came up without a camera.");
            Assert.IsTrue(manager.ActiveCamera.isActiveAndEnabled);

            AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator ExactlyOneCameraAndListenerAreLive()
        {
            yield return LoadLab();

            int cameras = 0;
            foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
            {
                if (camera.isActiveAndEnabled) cameras++;
            }

            int listeners = 0;
            foreach (AudioListener listener in
                     Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude))
            {
                if (listener.isActiveAndEnabled) listeners++;
            }

            Assert.AreEqual(1, cameras, "Expected one live camera, found " + cameras + ".");
            Assert.AreEqual(1, listeners, "Expected one live audio listener, found " + listeners + ".");
        }

        [UnityTest]
        public IEnumerator EveryInputActionIsBoundAndEnabled()
        {
            yield return LoadLab();

            PivotActions actions = PivotActions.Instance;
            Assert.IsNotNull(actions, "PivotActions never came up.");
            Assert.IsNotNull(actions.Asset, "No actions asset assigned.");

            // Each of these is a control the player is told about in the help overlay.
            // A null one means the asset and the code have drifted apart.
            Assert.IsNotNull(actions.Move, "Move is unbound.");
            Assert.IsNotNull(actions.Look, "Look is unbound.");
            Assert.IsNotNull(actions.Grab, "Grab is unbound.");
            Assert.IsNotNull(actions.FrameTree, "FrameTree is unbound.");
            Assert.IsNotNull(actions.Menu, "Menu is unbound.");
            Assert.IsNotNull(actions.Help, "Help is unbound.");

            Assert.IsTrue(actions.Move.enabled, "The action map never enabled.");
            Assert.IsTrue(actions.Grab.enabled);

            // The help overlay prints these, so an empty one is a blank row on screen.
            Assert.AreNotEqual("-", PivotActions.Describe(actions.Move));
            Assert.AreNotEqual("-", PivotActions.Describe(actions.Grab));
            Assert.AreNotEqual("-", PivotActions.Describe(actions.FrameTree));

            AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator NodesArePickUpAbleAndTheInteractorIsLive()
        {
            yield return LoadLab();

            MouseRayInteractor interactor =
                Object.FindFirstObjectByType<MouseRayInteractor>();
            Assert.IsNotNull(interactor, "No MouseRayInteractor in the running scene.");
            Assert.IsTrue(interactor.isActiveAndEnabled, "The interactor is not enabled.");

            NodeView[] nodes = Object.FindObjectsByType<NodeView>(FindObjectsInactive.Exclude);
            Assert.AreEqual(7, nodes.Length, "Expected the seven seed nodes.");

            for (int i = 0; i < nodes.Length; i++)
            {
                XRGrabInteractable grab = nodes[i].GetComponent<XRGrabInteractable>();
                Assert.IsNotNull(grab, "'" + nodes[i].name + "' is not grabbable.");
                Assert.IsTrue(grab.isActiveAndEnabled);
            }

            AssertNoErrors();
        }

        /// <summary>
        /// Walks the rig with a synthetic input value rather than a real key, which is
        /// enough to prove the movement path is connected end to end: read, accelerate,
        /// clamp, write to the transform.
        /// </summary>
        [UnityTest]
        public IEnumerator TheRigActuallyMovesWhenMoveIsDriven()
        {
            yield return LoadLab();

            DesktopLocomotion locomotion = Object.FindFirstObjectByType<DesktopLocomotion>();
            Assert.IsNotNull(locomotion, "No desktop locomotion in the running scene.");

            Vector3 before = locomotion.transform.position;

            // Measured over elapsed time, not a frame count. Batch mode runs frames as
            // fast as it can, so twenty frames can be two milliseconds of simulation and
            // says nothing about whether movement works.
            const float seconds = 0.6f;
            locomotion.DriveForTest(new Vector2(0f, 1f), 0f);
            yield return new WaitForSeconds(seconds);
            locomotion.DriveForTest(Vector2.zero, 0f);

            float travelled = Vector3.Distance(before, locomotion.transform.position);

            // A 2.6 m/s walk with a 0.09 s ramp covers well over a metre in 0.6 s. Half a
            // metre is a floor loose enough to survive a slow machine and still fail hard
            // if the input path is disconnected.
            Assert.Greater(travelled, 0.5f,
                "Holding forward for " + seconds + " s moved the rig " + travelled + " m.");

            AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator FramingTheTreeLooksAtIt()
        {
            yield return LoadLab();

            DesktopLocomotion locomotion = Object.FindFirstObjectByType<DesktopLocomotion>();
            Camera camera = RigManager.Instance.ActiveCamera;
            Assert.IsNotNull(locomotion);
            Assert.IsNotNull(camera);

            NodeView[] nodes = Object.FindObjectsByType<NodeView>(FindObjectsInactive.Exclude);
            Bounds bounds = new Bounds(nodes[0].transform.position, Vector3.zero);
            for (int i = 1; i < nodes.Length; i++) bounds.Encapsulate(nodes[i].transform.position);

            locomotion.Frame(bounds, camera.fieldOfView);
            yield return null;

            // Every node should now be in front of the camera and inside the frustum.
            for (int i = 0; i < nodes.Length; i++)
            {
                Vector3 viewport = camera.WorldToViewportPoint(nodes[i].transform.position);
                Assert.Greater(viewport.z, 0f, "'" + nodes[i].name + "' is behind the camera.");
                Assert.That(viewport.x, Is.InRange(-0.05f, 1.05f), "'" + nodes[i].name + "' is off screen.");
                Assert.That(viewport.y, Is.InRange(-0.05f, 1.05f), "'" + nodes[i].name + "' is off screen.");
            }

            AssertNoErrors();
        }
    }
}
