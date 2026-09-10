using System.Collections;
using NUnit.Framework;
using Pivot.Interaction;
using Pivot.Structures;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Pivot.PlayTests
{
    /// <summary>
    /// Performs an actual grab.
    ///
    /// "I cannot grab anything" is not a diagnosis, and guessing at an interaction stack
    /// this deep wastes a play session per guess. These drive the real interactor against
    /// a real node and fail at the exact stage that is broken: the raycast finding
    /// nothing, the select signal never arriving, or the object not following once held.
    /// </summary>
    public class GrabPlayTests
    {
        const string SceneName = "01_TreeLab";

        MouseRayInteractor _interactor;
        XRGrabInteractable _node;

        /// <summary>
        /// Waits for selection to actually change rather than assuming a frame count.
        /// The manual input reader commits on the frame after it is queued, so a fixed
        /// "yield return null" is a race.
        /// </summary>
        IEnumerator WaitForSelection(bool selected)
        {
            for (int i = 0; i < 30; i++)
            {
                if (_interactor.hasSelection == selected) yield break;
                yield return null;
            }
        }

        IEnumerator LoadAndAim()
        {
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            for (int i = 0; i < 5; i++) yield return null;

            _interactor = Object.FindAnyObjectByType<MouseRayInteractor>();
            Assert.IsNotNull(_interactor, "No MouseRayInteractor in the running scene.");

            NodeView[] nodes = Object.FindObjectsByType<NodeView>(FindObjectsInactive.Exclude);
            Assert.Greater(nodes.Length, 0, "No nodes to grab.");

            // Pick the root, then put the camera squarely in front of it so the cursor
            // ray, which runs down the screen centre while the cursor is locked, lands
            // on it without depending on where the rig happens to be standing.
            NodeView target = null;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].name == "Node 50") target = nodes[i];
            }

            Assert.IsNotNull(target, "Node 50 is missing.");
            _node = target.GetComponent<XRGrabInteractable>();
            Assert.IsNotNull(_node, "Node 50 has no XRGrabInteractable.");

            Camera camera = RigManager.Instance.ActiveCamera;
            Transform rig = Object.FindAnyObjectByType<DesktopLocomotion>().transform;

            // The controller owns the transform, so it has to be off to teleport the
            // rig, and back on immediately afterwards: leaving it off makes every
            // subsequent Update log an error and fails unrelated tests.
            CharacterController controller = rig.GetComponent<CharacterController>();
            bool hadController = controller != null && controller.enabled;
            if (hadController) controller.enabled = false;

            Vector3 nodePosition = target.transform.position;
            rig.position = nodePosition + new Vector3(0f, -1.6f, -0.9f);
            rig.rotation = Quaternion.identity;
            camera.transform.localRotation = Quaternion.identity;

            if (hadController) controller.enabled = true;

            Cursor.lockState = CursorLockMode.Locked;

            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheCursorRayFindsANode()
        {
            yield return LoadAndAim();

            // Stage one: is the ray hitting anything at all? If this fails the aiming is
            // wrong and nothing downstream can work.
            RaycastHit hit;
            bool found = _interactor.TryGetCurrent3DRaycastHit(out hit);

            Assert.IsTrue(found,
                "The interactor's ray hit nothing. Ray origin " +
                _interactor.transform.position + ", direction " + _interactor.transform.forward + ".");

            Assert.AreEqual(_node.transform, hit.collider.transform,
                "The ray hit '" + hit.collider.name + "' rather than the node in front of it.");
        }

        [UnityTest]
        public IEnumerator DrivingGrabSelectsTheNode()
        {
            yield return LoadAndAim();

            Assert.IsFalse(_interactor.hasSelection, "Something was already selected before the test.");

            // Stage two: the select signal. The interactor reads it as a manual value
            // fed from the actions asset, so this proves that path end to end.
            _interactor.ForceGrabForTest(true);
            yield return WaitForSelection(true);

            Assert.IsTrue(_interactor.hasSelection,
                "Driving Grab did not select the node the ray was on.");

            Assert.AreSame(_node, _interactor.firstInteractableSelected as XRGrabInteractable,
                "Something other than the node under the cursor got selected.");
        }

        [UnityTest]
        public IEnumerator AHeldNodeFollowsTheViewAndIsReleasedCleanly()
        {
            yield return LoadAndAim();

            _interactor.ForceGrabForTest(true);
            yield return WaitForSelection(true);
            Assert.IsTrue(_interactor.hasSelection, "Never picked the node up.");

            Vector3 held = _node.transform.position;

            // Stage three: does it actually come with you? Turning the rig should carry
            // the node around with it.
            Transform rig = Object.FindAnyObjectByType<DesktopLocomotion>().transform;
            rig.rotation = Quaternion.Euler(0f, 35f, 0f);
            yield return new WaitForSeconds(0.35f);

            float moved = Vector3.Distance(held, _node.transform.position);
            Assert.Greater(moved, 0.1f,
                "The node did not follow the view while held; it moved " + moved + " m.");

            _interactor.ForceGrabForTest(false);
            yield return WaitForSelection(false);

            Assert.IsFalse(_interactor.hasSelection, "Releasing Grab did not drop the node.");
        }

        /// <summary>The feel is the phase, so the squash and the spring get asserted too.</summary>
        [UnityTest]
        public IEnumerator PickingUpSquashesAndReleasingSprings()
        {
            yield return LoadAndAim();

            NodeView view = _node.GetComponent<NodeView>();
            Transform body = view.Body;

            Vector3 resting = view.RestScale;

            _interactor.ForceGrabForTest(true);
            yield return WaitForSelection(true);
            Assert.IsTrue(_interactor.hasSelection, "Never picked the node up.");

            // The squash is applied the instant the grab fires, before any tween runs,
            // so it is observable on the selection frame: wider than it is tall.
            Assert.Greater(body.localScale.x, resting.x * 1.05f,
                "No squash on pickup; the node did not react to being grabbed.");
            Assert.Less(body.localScale.y, body.localScale.x,
                "The squash should be wider than it is tall.");

            Assert.IsTrue(view.IdleSuspended, "Idle should be suspended while carried.");

            yield return new WaitForSeconds(0.3f);

            _interactor.ForceGrabForTest(false);
            yield return null;

            // The release punch overshoots outward before settling.
            yield return new WaitForSeconds(0.08f);
            Assert.Greater(body.localScale.x, resting.x,
                "No spring on release; the node just stopped.");

            yield return new WaitForSeconds(0.8f);
            Assert.That(body.localScale.x, Is.EqualTo(resting.x).Within(resting.x * 0.08f),
                "The release spring never settled back to resting scale.");
            Assert.IsFalse(view.IdleSuspended, "Idle should resume once the spring settles.");
        }

        [UnityTest]
        public IEnumerator EdgesFollowANodeWhileItIsHeld()
        {
            yield return LoadAndAim();

            TreeView treeView = Object.FindAnyObjectByType<TreeView>();
            Assert.IsNotNull(treeView, "No TreeView in the scene, so nothing drives the edges.");

            EdgeView edge = null;
            foreach (EdgeView candidate in Object.FindObjectsByType<EdgeView>(FindObjectsInactive.Exclude))
            {
                if (candidate.ParentId == _node.GetComponent<NodeView>().NodeId) edge = candidate;
            }

            Assert.IsNotNull(edge,
                "No edge is registered against the grabbed node. Ids are serialised, so " +
                "a null here means the authored scene was never adopted.");

            Vector3 before = edge.transform.position;

            _interactor.ForceGrabForTest(true);
            yield return WaitForSelection(true);
            Assert.IsTrue(_interactor.hasSelection, "Never picked the node up.");

            Transform rig = Object.FindAnyObjectByType<DesktopLocomotion>().transform;
            rig.rotation = Quaternion.Euler(0f, 30f, 0f);
            yield return new WaitForSeconds(0.35f);

            float moved = Vector3.Distance(before, edge.transform.position);
            Assert.Greater(moved, 0.05f,
                "The edge did not follow its node; it moved " + moved + " m.");

            _interactor.ForceGrabForTest(false);
            yield return null;
        }
    }
}
