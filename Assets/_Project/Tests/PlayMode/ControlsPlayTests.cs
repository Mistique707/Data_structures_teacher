using System.Collections;
using NUnit.Framework;
using Pivot.Interaction;
using Pivot.Structures;
using Pivot.UI;
using Pivot.Utils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Pivot.PlayTests
{
    /// <summary>
    /// Every bound control, driven through the real actions asset with synthetic
    /// devices, exactly as a hand on a mouse and keyboard would drive it.
    ///
    /// This exists because mouse look shipped dead. The rig moved on WASD, the grab
    /// worked, every structural test was green, and yaw never changed — because a
    /// static sensitivity defaulted to zero when nothing had called Settings.Load.
    /// No test had ever moved the mouse. Now one does, and so does one for each of
    /// the other bindings the help overlay claims exist.
    ///
    /// Inherits InputTestFixture, which replaces the live input system with a
    /// controllable one for the duration of each test and restores it afterwards.
    /// </summary>
    public class ControlsPlayTests : InputTestFixture
    {
        const string SceneName = "01_TreeLab";

        Mouse _mouse;
        Keyboard _keyboard;

        public override void Setup()
        {
            base.Setup();

            // The fixture starts with no devices at all. These have to exist before the
            // scene loads, so PivotActions binds to them when it enables its map.
            _mouse = InputSystem.AddDevice<Mouse>();
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        IEnumerator LoadLab()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            for (int i = 0; i < 5; i++) yield return null;
        }

        static DesktopLocomotion Locomotion()
        {
            DesktopLocomotion locomotion = Object.FindAnyObjectByType<DesktopLocomotion>();
            Assert.IsNotNull(locomotion, "No desktop rig in the running scene.");
            return locomotion;
        }

        /// <summary>Feeds mouse motion the way the hardware would: a delta per frame.</summary>
        IEnumerator DragMouse(Vector2 perFrame, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                Set(_mouse.delta, perFrame);
                yield return null;
            }

            Set(_mouse.delta, Vector2.zero);
            yield return null;
        }

        // ------------------------------------------------------------------- look

        [UnityTest]
        public IEnumerator MovingTheMouseTurnsTheView()
        {
            yield return LoadLab();
            DesktopLocomotion rig = Locomotion();

            Assert.IsTrue(rig.Captured, "Cursor should be captured on Play.");

            float yawBefore = rig.transform.eulerAngles.y;

            yield return DragMouse(new Vector2(40f, 0f), 10);

            float yawAfter = rig.transform.eulerAngles.y;
            float turned = Mathf.Abs(Mathf.DeltaAngle(yawBefore, yawAfter));

            Assert.Greater(turned, 5f,
                "Four hundred counts of mouse movement turned the view " + turned +
                " degrees. Sensitivity is " + Settings.MouseSensitivity +
                "; zero here means Settings never loaded.");
        }

        [UnityTest]
        public IEnumerator MouseLookIsNotFrameRateDependent()
        {
            yield return LoadLab();
            DesktopLocomotion rig = Locomotion();
            Assert.IsTrue(rig.Captured);

            // The same total mouse travel, delivered as many small deltas and as few
            // large ones, has to turn the view by the same amount. If look were scaled
            // by deltaTime the two would diverge with frame rate.
            float start = rig.transform.eulerAngles.y;
            yield return DragMouse(new Vector2(10f, 0f), 20);
            float small = Mathf.DeltaAngle(start, rig.transform.eulerAngles.y);

            start = rig.transform.eulerAngles.y;
            yield return DragMouse(new Vector2(100f, 0f), 2);
            float large = Mathf.DeltaAngle(start, rig.transform.eulerAngles.y);

            Assert.That(large, Is.EqualTo(small).Within(0.5f),
                "200 counts as 20x10 turned " + small + " deg but as 2x100 turned " + large + " deg.");
        }

        [UnityTest]
        public IEnumerator PitchIsClampedSoTheViewCannotFlip()
        {
            yield return LoadLab();
            DesktopLocomotion rig = Locomotion();
            Assert.IsTrue(rig.Captured);

            // Far more vertical travel than any clamp allows.
            yield return DragMouse(new Vector2(0f, 500f), 30);

            Transform head = RigManager.Instance.ActiveCamera.transform;
            float pitch = head.localEulerAngles.x;
            if (pitch > 180f) pitch -= 360f;

            Assert.That(Mathf.Abs(pitch), Is.LessThanOrEqualTo(85.5f),
                "Pitch reached " + pitch + " degrees; it should clamp before flipping.");
        }

        // ----------------------------------------------------------------- cursor

        [UnityTest]
        public IEnumerator EscapeReleasesTheCursorAndAClickTakesItBack()
        {
            yield return LoadLab();
            Locomotion();

            Cursor.lockState = CursorLockMode.Locked;
            yield return null;

            Press(_keyboard.escapeKey);
            yield return null;
            Release(_keyboard.escapeKey);
            yield return null;

            DesktopLocomotion rig = Locomotion();
            Assert.IsFalse(rig.Captured, "Escape did not release the cursor.");

            Press(_mouse.leftButton);
            yield return null;
            Release(_mouse.leftButton);
            yield return null;

            Assert.IsTrue(rig.Captured, "Clicking back into the view did not recapture the cursor.");
        }

        [UnityTest]
        public IEnumerator TheRecaptureClickDoesNotAlsoGrab()
        {
            yield return LoadLab();
            MouseRayInteractor interactor = Object.FindAnyObjectByType<MouseRayInteractor>();
            Assert.IsNotNull(interactor);

            // Cursor released, then a single click. That click is spent on recapture and
            // must not fall through to a grab even with a node dead centre.
            DesktopLocomotion rig = Locomotion();
            Press(_keyboard.escapeKey);
            yield return null;
            Release(_keyboard.escapeKey);
            yield return null;
            Assert.IsFalse(rig.Captured);

            NodeView root = null;
            foreach (NodeView node in Object.FindObjectsByType<NodeView>(FindObjectsInactive.Exclude))
            {
                if (node.name == "Node 50") root = node;
            }
            CharacterController controller = rig.GetComponent<CharacterController>();
            controller.enabled = false;
            rig.transform.position = root.transform.position + new Vector3(0f, -1.6f, -0.9f);
            rig.transform.rotation = Quaternion.identity;
            RigManager.Instance.ActiveCamera.transform.localRotation = Quaternion.identity;
            controller.enabled = true;
            yield return null;

            Press(_mouse.leftButton);
            yield return null;
            yield return null;
            Release(_mouse.leftButton);
            for (int i = 0; i < 4; i++) yield return null;

            Assert.IsTrue(rig.Captured, "The click did not recapture.");
            Assert.IsFalse(interactor.hasSelection,
                "The click that recaptured the cursor also grabbed something.");

            // And the NEXT click, with the cursor already captured, must grab.
            Press(_mouse.leftButton);
            for (int i = 0; i < 12 && !interactor.hasSelection; i++) yield return null;
            Assert.IsTrue(interactor.hasSelection, "A second click, cursor captured, did not grab.");
            Release(_mouse.leftButton);
            for (int i = 0; i < 12 && interactor.hasSelection; i++) yield return null;
        }

        // ----------------------------------------------------------------- stance

        [UnityTest]
        public IEnumerator CrouchDropsTheEyesAndSpringsBackOnRelease()
        {
            yield return LoadLab();
            Transform head = RigManager.Instance.ActiveCamera.transform;

            float standing = head.localPosition.y;

            Press(_keyboard.leftCtrlKey);
            yield return new WaitForSeconds(0.5f);
            float crouched = head.localPosition.y;

            Assert.Less(crouched, standing - 0.3f,
                "Holding crouch only dropped the eyes " + (standing - crouched) + " m.");

            Release(_keyboard.leftCtrlKey);
            yield return new WaitForSeconds(0.6f);

            float lift = PivotActions.Instance.Elevate.ReadValue<float>();
            Assert.That(head.localPosition.y, Is.EqualTo(standing).Within(0.03f),
                "Eyes did not spring back to standing after releasing crouch. Elevate " +
                "reads " + lift + " after release; -1 means the input layer still holds the " +
                "key, 0 means the stance code is not returning.");
        }

        [UnityTest]
        public IEnumerator StretchRaisesTheEyesAndCannotBeHeldAsAHover()
        {
            yield return LoadLab();
            Transform head = RigManager.Instance.ActiveCamera.transform;
            float standing = head.localPosition.y;

            Press(_keyboard.spaceKey);
            yield return new WaitForSeconds(0.5f);
            Assert.Greater(head.localPosition.y, standing + 0.15f, "Space did not raise the eyes.");

            // Held for a long time it must settle, not keep climbing.
            float atHalfSecond = head.localPosition.y;
            yield return new WaitForSeconds(1.0f);
            Assert.That(head.localPosition.y, Is.EqualTo(atHalfSecond).Within(0.02f),
                "Holding Space kept climbing; that is a hover, not a stretch.");

            Release(_keyboard.spaceKey);
            yield return new WaitForSeconds(0.6f);
            Assert.That(head.localPosition.y, Is.EqualTo(standing).Within(0.03f),
                "Eyes did not return to standing after releasing Space.");
        }

        // ------------------------------------------------------------- framing

        [UnityTest]
        public IEnumerator PressingFFramesTheWholeTree()
        {
            yield return LoadLab();
            DesktopLocomotion rig = Locomotion();

            // Walk off somewhere the tree is out of view first, so framing has to do
            // real work rather than pass because the start position already sees it.
            CharacterController controller = rig.GetComponent<CharacterController>();
            controller.enabled = false;
            rig.transform.position = new Vector3(3.5f, 0.08f, 3.5f);
            rig.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            controller.enabled = true;
            yield return null;

            Press(_keyboard.fKey);
            yield return null;
            Release(_keyboard.fKey);
            yield return null;
            yield return null;

            Camera camera = RigManager.Instance.ActiveCamera;
            NodeView[] nodes = Object.FindObjectsByType<NodeView>(FindObjectsInactive.Exclude);
            Assert.Greater(nodes.Length, 0);

            for (int i = 0; i < nodes.Length; i++)
            {
                Vector3 v = camera.WorldToViewportPoint(nodes[i].transform.position);
                Assert.Greater(v.z, 0f, "'" + nodes[i].name + "' is behind the camera after F.");
                Assert.That(v.x, Is.InRange(-0.05f, 1.05f), "'" + nodes[i].name + "' is off screen after F.");
                Assert.That(v.y, Is.InRange(-0.05f, 1.05f), "'" + nodes[i].name + "' is off screen after F.");
            }
        }

        // ---------------------------------------------------------------- help

        [UnityTest]
        public IEnumerator F1TogglesTheHelpOverlay()
        {
            yield return LoadLab();
            HelpOverlay help = Object.FindAnyObjectByType<HelpOverlay>();
            Assert.IsNotNull(help, "No HelpOverlay in the running scene.");

            bool before = help.IsVisible;

            Press(_keyboard.f1Key);
            yield return null;
            Release(_keyboard.f1Key);
            yield return null;

            Assert.AreNotEqual(before, help.IsVisible, "F1 did not toggle the help overlay.");

            Press(_keyboard.f1Key);
            yield return null;
            Release(_keyboard.f1Key);
            yield return null;

            Assert.AreEqual(before, help.IsVisible, "A second F1 did not toggle it back.");
        }

        [UnityTest]
        public IEnumerator TheHelpOverlayOnlyListsControlsThatDoSomething()
        {
            yield return LoadLab();
            HelpOverlay help = Object.FindAnyObjectByType<HelpOverlay>();
            string text = help.CurrentText;

            // Everything it lists has to be real. Rotate held is bound but has no
            // consumer yet, so listing it would be a lie; this pins that.
            StringAssert.Contains("Move", text);
            StringAssert.Contains("Grab", text);
            StringAssert.Contains("Frame", text);
            StringAssert.DoesNotContain("Rotate held", text,
                "The overlay lists Rotate held while nothing consumes RotateHeld.");

            // And no row may be blank: each is read from the asset, so a blank one
            // means a binding went missing.
            foreach (string line in text.Split('\n'))
            {
                if (line.Trim().Length == 0) continue;
                Assert.IsFalse(line.StartsWith("-"),
                    "A help row has no binding: '" + line.Trim() + "'.");
            }
        }

        // -------------------------------------------------------------- scroll

        [UnityTest]
        public IEnumerator ScrollWhileHoldingPushesTheNodeAway()
        {
            yield return LoadLab();
            MouseRayInteractor interactor = Object.FindAnyObjectByType<MouseRayInteractor>();
            DesktopLocomotion rig = Locomotion();

            // Stand squarely in front of the root, then grab it through the real button.
            NodeView root = null;
            foreach (NodeView node in Object.FindObjectsByType<NodeView>(FindObjectsInactive.Exclude))
            {
                if (node.name == "Node 50") root = node;
            }

            Assert.IsNotNull(root);

            CharacterController controller = rig.GetComponent<CharacterController>();
            controller.enabled = false;
            rig.transform.position = root.transform.position + new Vector3(0f, -1.6f, -0.9f);
            rig.transform.rotation = Quaternion.identity;
            RigManager.Instance.ActiveCamera.transform.localRotation = Quaternion.identity;
            controller.enabled = true;
            yield return null;
            yield return null;
            Assert.IsTrue(rig.Captured, "Cursor should be captured on Play.");

            Press(_mouse.leftButton);
            for (int i = 0; i < 12 && !interactor.hasSelection; i++) yield return null;
            Assert.IsTrue(interactor.hasSelection, "Left button did not grab the node in front of the camera.");

            float before = interactor.HoldDistance;

            Set(_mouse.scroll, new Vector2(0f, 120f));
            yield return null;
            Set(_mouse.scroll, Vector2.zero);
            yield return null;

            Assert.Greater(interactor.HoldDistance, before,
                "Scrolling up while holding did not push the node further away.");

            Release(_mouse.leftButton);
            for (int i = 0; i < 12 && interactor.hasSelection; i++) yield return null;
            Assert.IsFalse(interactor.hasSelection, "Releasing the button did not drop the node.");
        }
    }
}
