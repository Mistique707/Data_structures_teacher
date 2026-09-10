using Pivot.Structures;
using Pivot.Utils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Pivot.Interaction
{
    /// <summary>
    /// How picking a node up and putting it down feels.
    ///
    /// The whole phase is this one interaction, because everything else inherits from it.
    /// Three beats, and each is doing a specific job:
    ///
    /// - <b>Squash on pickup.</b> A fast anticipatory squash, wider than it is tall, that
    ///   says the node noticed. It is short — a long one reads as lag rather than
    ///   response, and the grab has to feel instant.
    /// - <b>Held.</b> Slightly bigger and tinted, so the held node reads as lifted out of
    ///   the tree rather than merely under the cursor.
    /// - <b>Spring on release.</b> An outward overshoot that settles, plus the rotation
    ///   easing back to upright. Nothing about position: the node stays exactly where it
    ///   was dropped, because validation and reflow are not this phase.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public sealed class NodeGrabFeel : MonoBehaviour
    {
        [SerializeField] ThemeSO _theme;
        [SerializeField] NodeView _view;

        [Header("Pickup")]
        [Tooltip("How much the node squashes when grabbed. Wider than tall.")]
        [SerializeField, Range(0f, 0.6f)] float _squash = 0.26f;

        [SerializeField, Range(0.03f, 0.4f)] float _squashDuration = 0.14f;

        [Header("Held")]
        [Tooltip("Scale while carried, so it reads as lifted out of the tree.")]
        [SerializeField, Range(1f, 1.4f)] float _heldScale = 1.12f;

        [SerializeField, Range(0f, 1f)] float _heldTint = 0.35f;

        [Header("Release")]
        [Tooltip("Outward overshoot on release, before it settles.")]
        [SerializeField, Range(0f, 0.8f)] float _releasePunch = 0.34f;

        [SerializeField, Range(0.05f, 0.8f)] float _releaseDuration = 0.42f;

        [Tooltip("Seconds for a dropped node to ease back to upright.")]
        [SerializeField, Range(0.05f, 1.2f)] float _uprightDuration = 0.45f;

        XRGrabInteractable _grab;
        Transform _body;

        // The body is authored at the node's real size, not at one, so every scale here
        // is a multiple of that rather than an absolute value.
        Vector3 _rest = Vector3.one;

        // Counts down after a release. Idle breathing stays out of the way until the
        // release tween has finished owning the scale channel.
        float _idleResumeIn;

        void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
            if (_view == null) _view = GetComponent<NodeView>();
            _body = _view != null ? _view.Body : transform;
            _rest = _view != null ? _view.RestScale : _body.localScale;
        }

        void OnEnable()
        {
            _grab.selectEntered.AddListener(OnGrabbed);
            _grab.selectExited.AddListener(OnReleased);
        }

        void OnDisable()
        {
            _grab.selectEntered.RemoveListener(OnGrabbed);
            _grab.selectExited.RemoveListener(OnReleased);
        }

        void OnGrabbed(SelectEnterEventArgs args)
        {
            if (_body == null) return;

            TweenRunner runner = TweenRunner.Instance;
            AnimationCurve curve = _theme != null ? _theme.Squash : null;

            // Squash first, then settle to the carried size. Two tweens on the same
            // transform on purpose: the second replaces the first as it finishes, which
            // is what gives the shape a beat rather than a single ramp.
            runner.Cancel(_body, TweenChannel.LocalScale);
            _body.localScale = Vector3.Scale(_rest,
                new Vector3(1f + _squash, 1f - _squash * 0.8f, 1f + _squash));

            runner.ScaleTo(_body, _rest * _heldScale, _squashDuration, curve);

            if (_view != null)
            {
                _view.IdleSuspended = true;
                if (_theme != null) _view.SetHighlight(_theme.Held, _heldTint);
            }

            _idleResumeIn = 0f;
            AudioService.Fire(Sfx.Whoosh);
        }

        void OnReleased(SelectExitEventArgs args)
        {
            if (_body == null) return;

            TweenRunner runner = TweenRunner.Instance;

            runner.Cancel(_body, TweenChannel.LocalScale);
            runner.Punch(_body, _rest, _releasePunch, _releaseDuration,
                _theme != null ? _theme.Pop : null);

            // Rotation eases back to upright so a node that was tumbled while carried
            // does not sit crooked. Position is deliberately untouched.
            runner.RotateTo(transform, Quaternion.identity, _uprightDuration,
                _theme != null ? _theme.Reflow : null);

            if (_view != null) _view.SetHighlight(Color.white, 0f);

            _idleResumeIn = _releaseDuration;
            AudioService.Fire(Sfx.Snap, 1.05f);
        }

        void Update()
        {
            if (_idleResumeIn <= 0f) return;

            _idleResumeIn -= Time.deltaTime;
            if (_idleResumeIn > 0f) return;

            if (_view != null) _view.IdleSuspended = false;
            _idleResumeIn = 0f;
        }

#if UNITY_EDITOR
        public void EditorBind(ThemeSO theme, NodeView view)
        {
            _theme = theme;
            _view = view;
        }
#endif
    }
}
