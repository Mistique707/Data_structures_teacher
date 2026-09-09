using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pivot.VFX
{
    /// <summary>
    /// Live tuning for the bubble look, on device, without a rebuild.
    ///
    /// Every value in Bubble.shader and ThemeSO was chosen against sRGB screenshots on
    /// a desktop monitor. A Quest panel is dimmer and lower contrast, and in passthrough
    /// the bubbles sit over a lit room rather than over the dusk sky, which changes what
    /// the rim, the glint and the guard need to be doing. The committed values are a
    /// starting point, not an answer, and this exists so the real answer can be found in
    /// one headset session instead of one rebuild per tweak.
    ///
    ///   F7 / F8   previous / next parameter
    ///   [ and ]   nudge down / up      (hold Shift for ten times the step)
    ///   F9        dump every value, to the log and to a file
    ///   F10       reset everything to the values the build shipped with
    ///
    /// These write to shared Materials, which in the Editor are files under version
    /// control, so the same guard RenderTuner uses applies: originals are captured on
    /// enable, restored on disable and on quit, and edits are refused in the Editor
    /// unless explicitly allowed.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public sealed class BubbleTuner : MonoBehaviour
    {
        [Header("Targets")]
        [Tooltip("The shared bubble material. Properties here affect every node at once.")]
        [SerializeField] Material _bubbleMaterial;

        [Tooltip("The TextMeshPro material node labels use. Only the outline is tuned.")]
        [SerializeField] Material _labelMaterial;

        [Header("Editor safety")]
        [Tooltip("Off by default so a tuning session cannot dirty the material assets " +
                 "on disk. Builds ignore this.")]
        [SerializeField] bool _allowMaterialEditsInEditor;

        [Header("Readout")]
        [SerializeField] bool _showOverlay = true;

        /// <summary>One tunable float on one material.</summary>
        struct Knob
        {
            public string Name;
            public Material Target;
            public string Property;
            public int Id;
            public float Min;
            public float Max;
            public float Step;
            public float Original;
        }

        static BubbleTuner _instance;

        readonly StringBuilder _builder = new StringBuilder(1024);

        Knob[] _knobs = Array.Empty<Knob>();
        int _index;
        bool _touched;
        GUIStyle _style;

        public static BubbleTuner Instance
        {
            get { return _instance; }
        }

        public int KnobCount
        {
            get { return _knobs.Length; }
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
            BuildKnobs();
        }

        void OnEnable()
        {
            Application.quitting += RestoreAll;
        }

        void OnDisable()
        {
            Application.quitting -= RestoreAll;
            RestoreAll();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void BuildKnobs()
        {
            // Ordered the way tuning actually proceeds: silhouette separation first,
            // because it is what decides whether overlapping bubbles read at all, then
            // the gloss on top of it, then the legibility guard, then the label.
            Knob[] knobs =
            {
                Define("edge darken",    _bubbleMaterial, "_EdgeDarken",    0f,    1f,    0.02f),
                Define("edge saturate",  _bubbleMaterial, "_EdgeSaturate",  1f,    2.5f,  0.05f),
                Define("edge power",     _bubbleMaterial, "_EdgePower",     0.5f,  8f,    0.1f),

                Define("rim strength",   _bubbleMaterial, "_RimStrength",   0f,    2f,    0.02f),
                Define("rim power",      _bubbleMaterial, "_RimPower",      0.5f,  24f,   0.5f),

                Define("gloss",          _bubbleMaterial, "_Gloss",         0f,    1f,    0.02f),
                Define("gloss cap",      _bubbleMaterial, "_GlossCap",      0.2f,  1.5f,  0.02f),
                Define("spec power",     _bubbleMaterial, "_SpecPower",     4f,    256f,  4f),

                Define("glint size",     _bubbleMaterial, "_GlintSize",     0.01f, 0.3f,  0.005f),
                Define("glint strength", _bubbleMaterial, "_GlintStrength", 0f,    2f,    0.02f),

                Define("inner glow",     _bubbleMaterial, "_InnerGlow",     0f,    1.5f,  0.02f),
                Define("top lift",       _bubbleMaterial, "_TopLift",       0f,    1f,    0.02f),
                Define("bottom sink",    _bubbleMaterial, "_BottomSink",    0f,    1f,    0.02f),
                Define("saturation",     _bubbleMaterial, "_Saturate",      0.5f,  2f,    0.02f),

                Define("guard radius",   _bubbleMaterial, "_GuardRadius",   0f,    1f,    0.02f),
                Define("guard softness", _bubbleMaterial, "_GuardSoftness", 0.01f, 0.6f,  0.02f),
                Define("guard spec kill",_bubbleMaterial, "_GuardSpecKill", 0f,    1f,    0.02f),
                Define("guard darken",   _bubbleMaterial, "_GuardDarken",   0f,    0.6f,  0.01f),

                Define("label outline",  _labelMaterial,  "_OutlineWidth",  0f,    1f,    0.01f)
            };

            int valid = 0;
            for (int i = 0; i < knobs.Length; i++)
            {
                if (knobs[i].Target != null) valid++;
            }

            _knobs = new Knob[valid];
            int write = 0;
            for (int i = 0; i < knobs.Length; i++)
            {
                if (knobs[i].Target == null) continue;
                _knobs[write++] = knobs[i];
            }

            if (_knobs.Length == 0)
            {
                Debug.LogWarning("BubbleTuner: no materials assigned, nothing to tune.");
            }
        }

        static Knob Define(string name, Material target, string property,
            float min, float max, float step)
        {
            Knob knob = new Knob
            {
                Name = name,
                Target = target,
                Property = property,
                Id = Shader.PropertyToID(property),
                Min = min,
                Max = max,
                Step = step,
                Original = 0f
            };

            if (target != null && target.HasProperty(knob.Id))
            {
                knob.Original = target.GetFloat(knob.Id);
            }
            else if (target != null)
            {
                Debug.LogWarning("BubbleTuner: " + target.name + " has no property " + property);
                knob.Target = null;
            }

            return knob;
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || _knobs.Length == 0) return;

            if (keyboard.f7Key.wasPressedThisFrame) Step(-1);
            if (keyboard.f8Key.wasPressedThisFrame) Step(1);
            if (keyboard.f9Key.wasPressedThisFrame) Dump();
            if (keyboard.f10Key.wasPressedThisFrame) RestoreAll();

            bool coarse = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            float scale = coarse ? 10f : 1f;

            if (keyboard.leftBracketKey.wasPressedThisFrame) Adjust(-scale);
            if (keyboard.rightBracketKey.wasPressedThisFrame) Adjust(scale);
        }

        // -------------------------------------------------------------- public API

        /// <summary>Move the selection. Also the hook a controller binding calls.</summary>
        public void Step(int direction)
        {
            if (_knobs.Length == 0) return;
            _index = (_index + direction + _knobs.Length) % _knobs.Length;
        }

        /// <summary>Nudge the selected value by <paramref name="steps"/> of its step size.</summary>
        public void Adjust(float steps)
        {
            if (_knobs.Length == 0) return;
            if (!CanEdit()) return;

            Knob knob = _knobs[_index];
            float current = knob.Target.GetFloat(knob.Id);
            float next = Mathf.Clamp(current + knob.Step * steps, knob.Min, knob.Max);

            _touched = true;
            knob.Target.SetFloat(knob.Id, next);
        }

        public string CurrentName
        {
            get { return _knobs.Length == 0 ? "-" : _knobs[_index].Name; }
        }

        public float CurrentValue
        {
            get
            {
                if (_knobs.Length == 0) return 0f;
                Knob knob = _knobs[_index];
                return knob.Target.GetFloat(knob.Id);
            }
        }

        public void RestoreAll()
        {
            if (!_touched) return;

            for (int i = 0; i < _knobs.Length; i++)
            {
                _knobs[i].Target.SetFloat(_knobs[i].Id, _knobs[i].Original);
            }

            _touched = false;
        }

        bool CanEdit()
        {
            if (!Application.isEditor || _allowMaterialEditsInEditor) return true;

            Debug.LogWarning(
                "BubbleTuner: material edits are disabled in the Editor so a tuning " +
                "session cannot dirty Bubble.mat on disk. Tick 'Allow Material Edits In " +
                "Editor' to override; on device they always work.");
            return false;
        }

        // ------------------------------------------------------------------- dump

        /// <summary>
        /// Prints every value in a form that can be pasted straight back into the
        /// shader's Properties block, and writes the same text next to the player data
        /// so it can be pulled off a headset with adb rather than read off a log.
        /// </summary>
        public string Dump()
        {
            _builder.Length = 0;
            _builder.AppendLine("=== Pivot bubble tuning ===");
            _builder.Append("captured ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            _builder.Append("   platform ").Append(Application.platform);
            _builder.AppendLine();
            _builder.AppendLine();

            _builder.AppendLine("-- changed from the shipped values --");
            bool anyChanged = false;
            for (int i = 0; i < _knobs.Length; i++)
            {
                float value = _knobs[i].Target.GetFloat(_knobs[i].Id);
                if (Mathf.Approximately(value, _knobs[i].Original)) continue;

                anyChanged = true;
                _builder.Append("  ").Append(_knobs[i].Property.PadRight(18));
                _builder.Append(_knobs[i].Original.ToString("0.####")).Append("  ->  ");
                _builder.AppendLine(value.ToString("0.####"));
            }

            if (!anyChanged) _builder.AppendLine("  (nothing changed)");
            _builder.AppendLine();

            _builder.AppendLine("-- paste into Bubble.shader Properties --");
            for (int i = 0; i < _knobs.Length; i++)
            {
                if (_knobs[i].Target != _bubbleMaterial) continue;
                float value = _knobs[i].Target.GetFloat(_knobs[i].Id);
                _builder.Append("  ").Append(_knobs[i].Property.PadRight(18));
                _builder.Append("(\"").Append(_knobs[i].Name).Append("\", Range(");
                _builder.Append(_knobs[i].Min.ToString("0.####")).Append(", ");
                _builder.Append(_knobs[i].Max.ToString("0.####")).Append(")) = ");
                _builder.AppendLine(value.ToString("0.####"));
            }

            for (int i = 0; i < _knobs.Length; i++)
            {
                if (_knobs[i].Target == _bubbleMaterial) continue;
                _builder.Append("  ").Append(_knobs[i].Target.name).Append('.');
                _builder.Append(_knobs[i].Property).Append(" = ");
                _builder.AppendLine(_knobs[i].Target.GetFloat(_knobs[i].Id).ToString("0.####"));
            }

            string text = _builder.ToString();
            Debug.Log(text);

            try
            {
                string path = Path.Combine(Application.persistentDataPath, "bubble-tuning.txt");
                File.WriteAllText(path, text);
                Debug.Log("BubbleTuner: written to " + path);
            }
            catch (Exception error)
            {
                Debug.LogWarning("BubbleTuner: could not write dump file: " + error.Message);
            }

            return text;
        }

        // ---------------------------------------------------------------- overlay

        void OnGUI()
        {
            if (!_showOverlay || _knobs.Length == 0) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = false };
                _style.normal.textColor = Color.white;
            }

            Knob knob = _knobs[_index];
            float value = knob.Target.GetFloat(knob.Id);
            float t = Mathf.InverseLerp(knob.Min, knob.Max, value);

            Rect box = new Rect(10f, 150f, 430f, 62f);

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = previous;

            string line =
                "tune  [" + (_index + 1) + "/" + _knobs.Length + "]  " + knob.Name +
                "   " + value.ToString("0.####") +
                (Mathf.Approximately(value, knob.Original) ? "" : "  *") +
                "\n" + knob.Property + "   range " + knob.Min.ToString("0.##") +
                " to " + knob.Max.ToString("0.##") + "   " + Mathf.RoundToInt(t * 100f) + "%" +
                "\nF7/F8 select   [ ] adjust   shift x10   F9 dump   F10 reset";

            GUI.Label(new Rect(box.x + 8f, box.y + 5f, box.width - 16f, box.height - 10f),
                line, _style);
        }
    }
}
