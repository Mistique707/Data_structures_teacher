namespace Pivot.Core
{
    /// <summary>Which slot under a parent a node occupies.</summary>
    public enum ChildSide
    {
        Left,
        Right
    }

    /// <summary>A position produced by a layout strategy. Y grows downward by depth.</summary>
    public readonly struct LayoutPoint
    {
        public readonly float X;
        public readonly float Y;

        public LayoutPoint(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    public enum TraversalKind
    {
        PreOrder,
        InOrder,
        PostOrder,
        BreadthFirst
    }

    /// <summary>Outcome of a rule check, with a sentence the UI can show verbatim.</summary>
    public readonly struct ValidationResult
    {
        public readonly bool Ok;
        public readonly string Reason;

        ValidationResult(bool ok, string reason)
        {
            Ok = ok;
            Reason = reason;
        }

        public static readonly ValidationResult Valid = new ValidationResult(true, null);

        public static ValidationResult Invalid(string reason)
        {
            return new ValidationResult(false, reason);
        }
    }

    /// <summary>
    /// One beat of an operation. Operations emit a list of these so the view can
    /// play them back one at a time in step mode.
    /// </summary>
    public enum StepKind
    {
        Compare,
        Descend,
        Attach,
        Detach,
        Transplant,
        Rotate,
        Remove,
        Visit,
        Reflow,
        Reject
    }

    public readonly struct TreeStep
    {
        public readonly StepKind Kind;
        public readonly int NodeId;
        public readonly int OtherId;
        public readonly string Caption;

        public TreeStep(StepKind kind, int nodeId, int otherId, string caption)
        {
            Kind = kind;
            NodeId = nodeId;
            OtherId = otherId;
            Caption = caption;
        }

        public TreeStep(StepKind kind, int nodeId, string caption)
            : this(kind, nodeId, 0, caption)
        {
        }
    }
}
