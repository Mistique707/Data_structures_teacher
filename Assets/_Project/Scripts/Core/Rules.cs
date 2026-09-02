using System.Collections.Generic;

namespace Pivot.Core
{
    /// <summary>
    /// The rules that decide whether a dragged node may be dropped where the user
    /// let go. They work entirely through <see cref="IStructureModel"/>, so a future
    /// structure reuses the ones that still apply and drops the ones that do not.
    /// Order matters: the cheap structural checks run before the value check, and
    /// the first refusal wins so the user only ever sees one explanation.
    /// </summary>
    public static class BstRules
    {
        public static readonly IReadOnlyList<IValidationRule> Default = new IValidationRule[]
        {
            new TargetExistsRule(),
            new NoCycleRule(),
            new SlotEmptyRule(),
            new SearchPropertyRule()
        };

        public static ValidationResult CheckAll(
            IReadOnlyList<IValidationRule> rules, IStructureModel model, in DropAttempt attempt)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                ValidationResult result = rules[i].Check(model, attempt);
                if (!result.Ok) return result;
            }

            return ValidationResult.Valid;
        }

        /// <summary>The open interval that everything dropped into this slot has to sit inside.</summary>
        public static void SlotBounds(
            IStructureModel model, int parentId, ChildSide side, int ignoringSubtreeRootId,
            out int low, out int high)
        {
            low = int.MinValue;
            high = int.MaxValue;

            int parentValue = model.ValueOf(parentId);
            int cur = model.RootId;

            while (cur != 0 && cur != parentId)
            {
                if (cur == ignoringSubtreeRootId) break;

                int curValue = model.ValueOf(cur);
                if (parentValue < curValue)
                {
                    high = curValue;
                    cur = model.ChildOf(cur, ChildSide.Left);
                }
                else
                {
                    low = curValue;
                    cur = model.ChildOf(cur, ChildSide.Right);
                }
            }

            if (side == ChildSide.Left) high = parentValue;
            else low = parentValue;
        }

        public static void SubtreeRange(IStructureModel model, int id, out int min, out int max)
        {
            min = int.MaxValue;
            max = int.MinValue;
            Walk(model, id, ref min, ref max);
        }

        static void Walk(IStructureModel model, int id, ref int min, ref int max)
        {
            if (id == 0) return;
            int value = model.ValueOf(id);
            if (value < min) min = value;
            if (value > max) max = value;
            Walk(model, model.ChildOf(id, ChildSide.Left), ref min, ref max);
            Walk(model, model.ChildOf(id, ChildSide.Right), ref min, ref max);
        }

        public static string SideWord(ChildSide side)
        {
            return side == ChildSide.Left ? "left" : "right";
        }

        /// <summary>
        /// Labels an empty slot with what it will accept, with no drag in progress.
        /// This is what makes pulling a node out a probe: the holes it leaves behind
        /// announce their own intervals, and it becomes obvious that they tile the
        /// number line with no overlap and no gap.
        /// </summary>
        public static string DescribeSlot(IStructureModel model, int parentId, ChildSide side)
        {
            int low, high;
            SlotBounds(model, parentId, side, 0, out low, out high);
            return DescribeInterval(low, high);
        }

        /// <summary>
        /// Puts a slot's accepted range into words. Every refusal quotes this, because
        /// the interval is the thing being taught: a slot is not "wrong for 45", it
        /// accepts a specific band of values and 45 is outside it.
        /// </summary>
        public static string DescribeInterval(int low, int high)
        {
            bool openBelow = low == int.MinValue;
            bool openAbove = high == int.MaxValue;

            if (openBelow && openAbove) return "any value";
            if (openBelow) return "values below " + high;
            if (openAbove) return "values above " + low;
            return "values between " + low + " and " + high;
        }
    }

    public sealed class TargetExistsRule : IValidationRule
    {
        public ValidationResult Check(IStructureModel model, in DropAttempt attempt)
        {
            if (attempt.NodeId == 0)
                return ValidationResult.Invalid("There is nothing here to place.");

            if (attempt.TargetParentId == 0)
                return ValidationResult.Invalid("Drop it onto a node to hang it there.");

            if (attempt.NodeId == attempt.TargetParentId)
                return ValidationResult.Invalid("A node cannot be its own parent.");

            return ValidationResult.Valid;
        }
    }

    public sealed class NoCycleRule : IValidationRule
    {
        public ValidationResult Check(IStructureModel model, in DropAttempt attempt)
        {
            int cur = attempt.TargetParentId;
            while (cur != 0)
            {
                if (cur == attempt.NodeId)
                {
                    return ValidationResult.Invalid(
                        model.ValueOf(attempt.TargetParentId) + " already sits under " +
                        model.ValueOf(attempt.NodeId) +
                        ", so it cannot hold it as well — that would make a loop.");
                }

                cur = model.ParentOf(cur);
            }

            return ValidationResult.Valid;
        }
    }

    public sealed class SlotEmptyRule : IValidationRule
    {
        public ValidationResult Check(IStructureModel model, in DropAttempt attempt)
        {
            int occupant = model.ChildOf(attempt.TargetParentId, attempt.Side);
            if (occupant == 0 || occupant == attempt.NodeId) return ValidationResult.Valid;

            return ValidationResult.Invalid(
                "The " + BstRules.SideWord(attempt.Side) + " slot under " +
                model.ValueOf(attempt.TargetParentId) + " already holds " +
                model.ValueOf(occupant) + ".");
        }
    }

    /// <summary>
    /// The one that actually teaches. Each empty slot accepts exactly one open
    /// interval of values, inherited from the ancestors above it, and those intervals
    /// partition the whole number line. Every value therefore has exactly one legal
    /// home in the tree at any moment. A refusal here always names the interval,
    /// because that interval is the lesson.
    /// </summary>
    public sealed class SearchPropertyRule : IValidationRule
    {
        public ValidationResult Check(IStructureModel model, in DropAttempt attempt)
        {
            int low, high;
            BstRules.SlotBounds(model, attempt.TargetParentId, attempt.Side, attempt.NodeId,
                out low, out high);

            int min, max;
            BstRules.SubtreeRange(model, attempt.NodeId, out min, out max);

            bool tooLow = low != int.MinValue && min <= low;
            bool tooHigh = high != int.MaxValue && max >= high;
            if (!tooLow && !tooHigh) return ValidationResult.Valid;

            string interval = BstRules.DescribeInterval(low, high);
            int offender = tooLow ? min : max;

            if (min == max)
            {
                return ValidationResult.Invalid(
                    offender + " can't go here — this slot only accepts " + interval + ".");
            }

            return ValidationResult.Invalid(
                "This branch can't go here — it holds " + offender +
                ", and this slot only accepts " + interval + ".");
        }
    }
}
