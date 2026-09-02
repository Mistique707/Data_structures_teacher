using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pivot.Utils
{
    public enum TweenChannel
    {
        Position,
        LocalPosition,
        LocalScale,
        LocalRotation,
        Float
    }

    /// <summary>
    /// One update loop for every animation in the app. Dozens of coroutines would
    /// each allocate an iterator and a frame of garbage; this runs a flat list of
    /// structs instead, so the steady state costs nothing.
    ///
    /// Every motion is shaped by an <see cref="AnimationCurve"/> from the theme asset,
    /// which is what makes squash, stretch and springiness tunable in the Inspector
    /// without touching code.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class TweenRunner : MonoBehaviour
    {
        struct Job
        {
            public Transform Target;
            public TweenChannel Channel;
            public Vector3 From;
            public Vector3 To;
            public Quaternion FromRotation;
            public Quaternion ToRotation;
            public AnimationCurve Curve;
            public float Duration;
            public float Elapsed;
            public Action<float> OnFloat;
            public Action OnComplete;
            public int Token;
            public bool Unscaled;
        }

        static TweenRunner _instance;

        readonly List<Job> _jobs = new List<Job>(128);
        int _nextToken = 1;

        /// <summary>Scales every duration. The animation-speed setting writes this.</summary>
        public static float SpeedMultiplier = 1f;

        public static TweenRunner Instance
        {
            get
            {
                if (_instance != null) return _instance;

                GameObject host = new GameObject("TweenRunner");
                DontDestroyOnLoad(host);
                _instance = host.AddComponent<TweenRunner>();
                return _instance;
            }
        }

        public static bool Exists
        {
            get { return _instance != null; }
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void Update()
        {
            if (_jobs.Count == 0) return;

            float dt = Time.deltaTime;
            float unscaledDt = Time.unscaledDeltaTime;
            float speed = SpeedMultiplier <= 0.01f ? 0.01f : SpeedMultiplier;

            for (int i = _jobs.Count - 1; i >= 0; i--)
            {
                Job job = _jobs[i];

                if (job.Channel != TweenChannel.Float && job.Target == null)
                {
                    RemoveAt(i);
                    continue;
                }

                job.Elapsed += (job.Unscaled ? unscaledDt : dt) * speed;

                float duration = job.Duration <= 0.0001f ? 0.0001f : job.Duration;
                float linear = job.Elapsed / duration;
                bool finished = linear >= 1f;
                if (finished) linear = 1f;

                float eased = job.Curve != null ? job.Curve.Evaluate(linear) : linear;
                Apply(ref job, eased);

                if (!finished)
                {
                    _jobs[i] = job;
                    continue;
                }

                Action done = job.OnComplete;
                RemoveAt(i);
                if (done != null) done();
            }
        }

        void RemoveAt(int index)
        {
            int last = _jobs.Count - 1;
            if (index != last) _jobs[index] = _jobs[last];
            _jobs.RemoveAt(last);
        }

        static void Apply(ref Job job, float t)
        {
            switch (job.Channel)
            {
                case TweenChannel.Position:
                    job.Target.position = Vector3.LerpUnclamped(job.From, job.To, t);
                    break;
                case TweenChannel.LocalPosition:
                    job.Target.localPosition = Vector3.LerpUnclamped(job.From, job.To, t);
                    break;
                case TweenChannel.LocalScale:
                    job.Target.localScale = Vector3.LerpUnclamped(job.From, job.To, t);
                    break;
                case TweenChannel.LocalRotation:
                    job.Target.localRotation = Quaternion.SlerpUnclamped(job.FromRotation, job.ToRotation, t);
                    break;
                case TweenChannel.Float:
                    if (job.OnFloat != null) job.OnFloat(Mathf.LerpUnclamped(job.From.x, job.To.x, t));
                    break;
            }
        }

        // ------------------------------------------------------------------ public

        /// <summary>Stops anything currently driving this transform on that channel.</summary>
        public void Cancel(Transform target, TweenChannel channel)
        {
            for (int i = _jobs.Count - 1; i >= 0; i--)
            {
                if (_jobs[i].Target == target && _jobs[i].Channel == channel) RemoveAt(i);
            }
        }

        public void CancelAll(Transform target)
        {
            for (int i = _jobs.Count - 1; i >= 0; i--)
            {
                if (_jobs[i].Target == target) RemoveAt(i);
            }
        }

        /// <summary>Finishes a transform's tweens immediately, landing on their end values.</summary>
        public void Complete(Transform target)
        {
            for (int i = _jobs.Count - 1; i >= 0; i--)
            {
                if (_jobs[i].Target != target) continue;

                Job job = _jobs[i];
                Apply(ref job, job.Curve != null ? job.Curve.Evaluate(1f) : 1f);
                Action done = job.OnComplete;
                RemoveAt(i);
                if (done != null) done();
            }
        }

        public int Move(Transform target, Vector3 to, float duration, AnimationCurve curve,
            Action onComplete = null, bool local = false, bool unscaled = false)
        {
            TweenChannel channel = local ? TweenChannel.LocalPosition : TweenChannel.Position;
            Cancel(target, channel);

            Job job = new Job
            {
                Target = target,
                Channel = channel,
                From = local ? target.localPosition : target.position,
                To = to,
                Curve = curve,
                Duration = duration,
                OnComplete = onComplete,
                Token = _nextToken++,
                Unscaled = unscaled
            };

            _jobs.Add(job);
            return job.Token;
        }

        public int ScaleTo(Transform target, Vector3 to, float duration, AnimationCurve curve,
            Action onComplete = null, bool unscaled = false)
        {
            Cancel(target, TweenChannel.LocalScale);

            Job job = new Job
            {
                Target = target,
                Channel = TweenChannel.LocalScale,
                From = target.localScale,
                To = to,
                Curve = curve,
                Duration = duration,
                OnComplete = onComplete,
                Token = _nextToken++,
                Unscaled = unscaled
            };

            _jobs.Add(job);
            return job.Token;
        }

        public int RotateTo(Transform target, Quaternion to, float duration, AnimationCurve curve,
            Action onComplete = null, bool unscaled = false)
        {
            Cancel(target, TweenChannel.LocalRotation);

            Job job = new Job
            {
                Target = target,
                Channel = TweenChannel.LocalRotation,
                FromRotation = target.localRotation,
                ToRotation = to,
                Curve = curve,
                Duration = duration,
                OnComplete = onComplete,
                Token = _nextToken++,
                Unscaled = unscaled
            };

            _jobs.Add(job);
            return job.Token;
        }

        /// <summary>
        /// Scales out and back through a curve that starts and ends at zero. Used for
        /// every squash and stretch: spawn, grab, drop, button press.
        /// </summary>
        public int Punch(Transform target, Vector3 baseScale, float amount, float duration,
            AnimationCurve curve, Action onComplete = null, bool unscaled = false)
        {
            Cancel(target, TweenChannel.LocalScale);

            Job job = new Job
            {
                Target = target,
                Channel = TweenChannel.LocalScale,
                From = baseScale,
                To = baseScale * (1f + amount),
                Curve = curve,
                Duration = duration,
                OnComplete = onComplete,
                Token = _nextToken++,
                Unscaled = unscaled
            };

            _jobs.Add(job);
            return job.Token;
        }

        public int Value(float from, float to, float duration, AnimationCurve curve,
            Action<float> onValue, Action onComplete = null, bool unscaled = false)
        {
            Job job = new Job
            {
                Target = null,
                Channel = TweenChannel.Float,
                From = new Vector3(from, 0f, 0f),
                To = new Vector3(to, 0f, 0f),
                Curve = curve,
                Duration = duration,
                OnFloat = onValue,
                OnComplete = onComplete,
                Token = _nextToken++,
                Unscaled = unscaled
            };

            _jobs.Add(job);
            return job.Token;
        }

        public int ActiveCount
        {
            get { return _jobs.Count; }
        }
    }
}
