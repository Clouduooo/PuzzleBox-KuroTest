using System.Collections.Generic;
using System.Linq;
using PuzzleBox.Data;
using UnityEngine;

namespace PuzzleBox.Rules
{
    public enum ValidationSeverity
    {
        Warning,
        Error
    }

    public readonly struct ValidationIssue
    {
        public ValidationSeverity Severity { get; }
        public string Message { get; }
        public Vector3Int? Cell { get; }

        public ValidationIssue(ValidationSeverity severity, string message, Vector3Int? cell = null)
        {
            Severity = severity;
            Message = message;
            Cell = cell;
        }
    }

    public static class LevelValidator
    {
        public static List<ValidationIssue> Validate(LevelLayout layout)
        {
            var issues = new List<ValidationIssue>();

            if (!layout.PlayerSpawn.HasValue)
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "Player spawn is missing."));

            ValidateCells(layout, layout.Solids, "Solid", issues);
            ValidateCells(layout, layout.Targets, "Target", issues);
            ValidateCells(layout, layout.Boxes, "Box", issues);

            foreach (var duplicate in layout.Boxes.GroupBy(cell => cell).Where(group => group.Count() > 1))
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "Multiple boxes occupy the same cell.", duplicate.Key));

            foreach (var box in layout.Boxes.Where(layout.Solids.Contains))
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "A box overlaps a solid.", box));

            foreach (var target in layout.Targets.Where(layout.Solids.Contains))
                issues.Add(new ValidationIssue(ValidationSeverity.Error, "A target overlaps a solid.", target));

            if (layout.PlayerSpawn.HasValue)
            {
                var player = layout.PlayerSpawn.Value;
                if (!layout.Contains(player))
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, "Player spawn is outside the grid.", player));
                if (layout.Solids.Contains(player) || layout.Boxes.Contains(player))
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, "Player spawn overlaps an obstacle.", player));
            }

            if (layout.Boxes.Count != layout.Targets.Count)
                issues.Add(new ValidationIssue(ValidationSeverity.Warning,
                    $"Box count ({layout.Boxes.Count}) does not match target count ({layout.Targets.Count})."));

            foreach (var stair in layout.Stairs)
            {
                if (!layout.Contains(stair.LowerCell) || !layout.Contains(stair.UpperCell))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, "A stair endpoint is outside the grid.", stair.LowerCell));
                    continue;
                }

                ValidateStandingCell(layout, stair.LowerCell, "Stair lower endpoint", issues);
                ValidateStandingCell(layout, stair.UpperCell, "Stair upper endpoint", issues);
            }

            foreach (var target in layout.Targets)
            {
                if (!HasStaticSupport(layout, target))
                    issues.Add(new ValidationIssue(ValidationSeverity.Warning, "Target has no static support beneath it.", target));
            }

            for (var i = 0; i < layout.PerspectiveLinks.Count; i++)
            {
                var link = layout.PerspectiveLinks[i];
                var valid = PerspectiveRules.ValidViews(layout, link);
                if (link.ViewMask == 0 || (link.ViewMask & valid) != link.ViewMask)
                    issues.Add(new ValidationIssue(ValidationSeverity.Error,
                        $"视角连接 {i + 1}：端点缺少支撑、道路出口被占用或投影未对齐。", link.CellA));
                for (var q = 0; q < 4; q++)
                {
                    if (!link.EnabledIn(q) || (valid & (1 << q)) == 0) continue;
                    if (PerspectiveRules.TryGetOccludingSolid(layout, link, q, out var blocker))
                        issues.Add(new ValidationIssue(ValidationSeverity.Warning,
                            $"视角连接 {i + 1}：V{q} 被 Solid {blocker} 遮挡，当前不能通过（包含端点支撑块）。", blocker));
                    foreach (var source in new[] { link.CellA, link.CellB })
                    {
                        var direction = source == link.CellA ? link.DirectionFromA : link.DirectionFromA.Opposite();
                        if (layout.PerspectiveLinks.Skip(i + 1).Any(other => other.EnabledIn(q) &&
                            other.TryTraverse(source, direction, out _)))
                            issues.Add(new ValidationIssue(ValidationSeverity.Error,
                                $"视角连接 {i + 1}：V{q} 的同一出口存在重复连接。", source));
                    }
                }
            }

            return issues;
        }

        private static void ValidateCells(
            LevelLayout layout,
            IEnumerable<Vector3Int> cells,
            string label,
            ICollection<ValidationIssue> issues)
        {
            foreach (var cell in cells)
            {
                if (!layout.Contains(cell))
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, $"{label} is outside the grid.", cell));
            }
        }

        private static void ValidateStandingCell(
            LevelLayout layout,
            Vector3Int cell,
            string label,
            ICollection<ValidationIssue> issues)
        {
            if (layout.Solids.Contains(cell))
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"{label} is blocked by a solid.", cell));
            if (!HasStaticSupport(layout, cell))
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, $"{label} has no static support.", cell));
        }

        private static bool HasStaticSupport(LevelLayout layout, Vector3Int cell)
        {
            return cell.y > 0 && layout.Solids.Contains(cell + Vector3Int.down);
        }
    }
}
