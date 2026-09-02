using System.Text;
using Pivot.VFX;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pivot.Utils
{
    /// <summary>
    /// F3 toggles the readout. Frame time in milliseconds is the headline, not FPS:
    /// 72 against 71 says nothing, whereas 11.2 ms against 13.9 ms against a 13.9 ms
    /// budget says exactly how much room is left.
    ///
    /// Counters come from <see cref="ProfilerRecorder"/> rather than UnityStats, which
    /// is editor-only, so these numbers are the ones the headset actually produces.
    /// The current render configuration is printed alongside them, so a screenshot of
    /// this overlay is self-documenting.
    /// </summary>
    public sealed class PerfOverlay : MonoBehaviour
    {
        [SerializeField] bool _visibleAtStart;
        [SerializeField] float _refreshInterval = 0.25f;

        [Tooltip("Frames kept for the percentile. 300 at 72 Hz is about four seconds.")]
        [SerializeField] int _windowFrames = 300;

        readonly StringBuilder _text = new StringBuilder(420);

        ProfilerRecorder _batches;
        ProfilerRecorder _setPass;
        ProfilerRecorder _drawCalls;
        ProfilerRecorder _triangles;
        ProfilerRecorder _gcPerFrame;

        GUIStyle _style;
        GUIContent _content;
        bool _visible;

        float[] _window;
        int _windowCount;
        int _windowHead;

        float _accumulated;
        int _frames;

        float _meanMs;
        float _worstMs;
        float _p95Ms;

        float[] _sortScratch;

        void Awake()
        {
            _visible = _visibleAtStart;
            _content = new GUIContent(string.Empty);
            _window = new float[Mathf.Max(30, _windowFrames)];
            _sortScratch = new float[_window.Length];
        }

        void OnEnable()
        {
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            _gcPerFrame = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        }

        void OnDisable()
        {
            _batches.Dispose();
            _setPass.Dispose();
            _drawCalls.Dispose();
            _triangles.Dispose();
            _gcPerFrame.Dispose();
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame) _visible = !_visible;

            float frameMs = Time.unscaledDeltaTime * 1000f;

            _window[_windowHead] = frameMs;
            _windowHead = (_windowHead + 1) % _window.Length;
            if (_windowCount < _window.Length) _windowCount++;

            if (!_visible) return;

            _accumulated += Time.unscaledDeltaTime;
            _frames++;
            if (_accumulated < _refreshInterval) return;

            _meanMs = _accumulated / _frames * 1000f;
            _accumulated = 0f;
            _frames = 0;

            ComputeWindow();
            Rebuild();
        }

        /// <summary>
        /// Worst and 95th percentile over the rolling window. A single spike matters
        /// more than an average on a headset, where one dropped frame is felt.
        /// </summary>
        void ComputeWindow()
        {
            if (_windowCount == 0) return;

            for (int i = 0; i < _windowCount; i++) _sortScratch[i] = _window[i];
            System.Array.Sort(_sortScratch, 0, _windowCount);

            _worstMs = _sortScratch[_windowCount - 1];
            int index = Mathf.Clamp(Mathf.CeilToInt(_windowCount * 0.95f) - 1, 0, _windowCount - 1);
            _p95Ms = _sortScratch[index];
        }

        static void AppendCounter(StringBuilder into, string label, ProfilerRecorder recorder)
        {
            into.Append(label).Append(' ');
            if (recorder.Valid) into.Append(recorder.LastValue);
            else into.Append('-');
        }

        void Rebuild()
        {
            int target = Application.targetFrameRate;
            float budgetMs = target > 0 ? 1000f / target : 0f;

            _text.Length = 0;

            _text.Append("frame ").Append(_meanMs.ToString("F2")).Append(" ms");
            if (budgetMs > 0f)
            {
                float headroom = budgetMs - _meanMs;
                _text.Append("   budget ").Append(budgetMs.ToString("F2")).Append(" ms");
                _text.Append("   headroom ").Append(headroom >= 0f ? "+" : "");
                _text.Append(headroom.ToString("F2")).Append(" ms");
            }

            _text.Append('\n');

            _text.Append("p95 ").Append(_p95Ms.ToString("F2")).Append(" ms");
            _text.Append("   worst ").Append(_worstMs.ToString("F2")).Append(" ms");
            _text.Append("   (").Append(Mathf.RoundToInt(_meanMs > 0f ? 1000f / _meanMs : 0f)).Append(" fps)");
            _text.Append('\n');

            AppendCounter(_text, "batches", _batches);
            _text.Append("   ");
            AppendCounter(_text, "setpass", _setPass);
            _text.Append("   ");
            AppendCounter(_text, "draws", _drawCalls);
            _text.Append('\n');

            AppendCounter(_text, "tris", _triangles);
            _text.Append("   gc/frame ");
            if (_gcPerFrame.Valid) _text.Append(_gcPerFrame.LastValue).Append(" B");
            else _text.Append('-');
            _text.Append("   tweens ").Append(TweenRunner.Exists ? TweenRunner.Instance.ActiveCount : 0);
            _text.Append('\n');

            RenderTuner tuner = RenderTuner.Instance;
            if (tuner != null) _text.Append(tuner.DescribeState());
            else _text.Append("quality ").Append(QualitySettings.names[QualitySettings.GetQualityLevel()]);

            _content.text = _text.ToString();
        }

        void OnGUI()
        {
            if (!_visible) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    alignment = TextAnchor.UpperLeft,
                    richText = false,
                    wordWrap = false
                };
                _style.normal.textColor = Color.white;
            }

            const float pad = 10f;
            Rect box = new Rect(pad, pad, 430f, 126f);

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = previous;

            GUI.Label(new Rect(box.x + 8f, box.y + 6f, box.width - 16f, box.height - 12f), _content, _style);
        }
    }
}
