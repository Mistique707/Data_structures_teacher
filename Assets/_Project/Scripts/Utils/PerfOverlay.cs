using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pivot.Utils
{
    /// <summary>
    /// F3 toggles a frame time, draw call and allocation readout. It exists so the
    /// performance budget can be checked at any moment rather than trusted.
    ///
    /// The counters come from <see cref="ProfilerRecorder"/> rather than UnityStats,
    /// because UnityStats is editor-only and the numbers that matter are the ones
    /// coming off the headset. Recorders that a given build does not expose simply
    /// report as unavailable instead of breaking the overlay.
    /// </summary>
    public sealed class PerfOverlay : MonoBehaviour
    {
        [SerializeField] bool _visibleAtStart;
        [SerializeField] float _refreshInterval = 0.25f;

        readonly StringBuilder _text = new StringBuilder(320);

        ProfilerRecorder _batches;
        ProfilerRecorder _setPass;
        ProfilerRecorder _drawCalls;
        ProfilerRecorder _triangles;
        ProfilerRecorder _gcPerFrame;

        GUIStyle _style;
        GUIContent _content;
        bool _visible;

        float _accumulated;
        int _frames;
        float _fps;
        float _worstFrameMs;
        float _worstResetAt;

        void Awake()
        {
            _visible = _visibleAtStart;
            _content = new GUIContent(string.Empty);
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

            if (!_visible) return;

            float frameMs = Time.unscaledDeltaTime * 1000f;
            if (frameMs > _worstFrameMs) _worstFrameMs = frameMs;

            if (Time.unscaledTime >= _worstResetAt)
            {
                _worstResetAt = Time.unscaledTime + 3f;
                _worstFrameMs = frameMs;
            }

            _accumulated += Time.unscaledDeltaTime;
            _frames++;
            if (_accumulated < _refreshInterval) return;

            _fps = _frames / _accumulated;
            _accumulated = 0f;
            _frames = 0;

            Rebuild();
        }

        static void AppendCounter(StringBuilder into, string label, ProfilerRecorder recorder)
        {
            into.Append(label).Append(' ');
            if (recorder.Valid) into.Append(recorder.LastValue);
            else into.Append('-');
        }

        void Rebuild()
        {
            _text.Length = 0;

            _text.Append("FPS ").Append(Mathf.RoundToInt(_fps));
            _text.Append("   frame ").Append((1000f / Mathf.Max(_fps, 0.001f)).ToString("F1")).Append(" ms");
            _text.Append("   worst ").Append(_worstFrameMs.ToString("F1")).Append(" ms\n");

            _text.Append("target ").Append(Application.targetFrameRate);
            _text.Append("   vsync ").Append(QualitySettings.vSyncCount);
            _text.Append("   quality ").Append(QualitySettings.names[QualitySettings.GetQualityLevel()]).Append('\n');

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
            _text.Append('\n');

            _text.Append("tweens ").Append(TweenRunner.Exists ? TweenRunner.Instance.ActiveCount : 0);

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
            Rect box = new Rect(pad, pad, 380f, 108f);

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = previous;

            GUI.Label(new Rect(box.x + 8f, box.y + 6f, box.width - 16f, box.height - 12f), _content, _style);
        }
    }
}
