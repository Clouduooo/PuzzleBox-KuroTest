using System.Collections.Generic;
using System.Linq;
using PuzzleBox.Data;
using UnityEngine;

namespace PuzzleBox.Rules
{
    public readonly struct ViewStep
    {
        public readonly Vector3Int Destination;
        public readonly bool UsesLink;
        public readonly bool IsBox;
        public readonly MoveBlockReason BlockReason;
        public bool Allowed => BlockReason == MoveBlockReason.None;
        public ViewStep(Vector3Int destination, bool usesLink, bool isBox, MoveBlockReason reason)
        { Destination = destination; UsesLink = usesLink; IsBox = isBox; BlockReason = reason; }
    }

    /// <summary>One settled geometry and visibility policy for play, painting and reachability.</summary>
    public sealed class PerspectiveNetwork
    {
        private readonly LevelLayout authoredLayout;
        private readonly HashSet<Vector3Int> boxes;
        private readonly List<PerspectiveLink>[] activeByView = new List<PerspectiveLink>[4];
        private readonly Dictionary<Vector2Int, Vector3Int>[] frontByView = new Dictionary<Vector2Int, Vector3Int>[4];
        public LevelLayout DynamicGeometry { get; }
        public IReadOnlyList<PerspectiveLink> DynamicCandidates { get; }

        public PerspectiveNetwork(LevelLayout layout, IEnumerable<Vector3Int> boxPositions)
        {
            authoredLayout = layout;
            boxes = new HashSet<Vector3Int>(boxPositions ?? Enumerable.Empty<Vector3Int>());
            // Reuse EXACTLY the existing surface/open-edge/projection/occlusion rules.
            // A box is a unit volume here, including its supporting and occluding faces.
            // Covered box tops disappear naturally. No endpoint support is exempted.
            DynamicGeometry = new LevelLayout(layout.Size, layout.Solids.Concat(boxes), null, null,
                layout.Stairs, layout.PlayerSpawn);
            DynamicCandidates = boxes.Count == 0 ? new List<PerspectiveLink>() :
                PerspectiveRules.FindCandidates(DynamicGeometry).Where(link =>
                    boxes.Contains(link.CellA + Vector3Int.down) ||
                    boxes.Contains(link.CellB + Vector3Int.down)).ToList();
        }

        public static int CameraDepth(Vector3Int cell, int quarter)
        {
            switch (IsometricProjection.Normalize(quarter))
            {
                case 1: return -cell.x + cell.y + cell.z;
                case 2: return cell.x + cell.y + cell.z;
                case 3: return cell.x + cell.y - cell.z;
                default: return -cell.x + cell.y - cell.z;
            }
        }

        /// <summary>Select ownership BEFORE authoring, collision or pushability. Never fall back behind it.</summary>
        public bool TryGetFrontCell(Vector3Int projectedCell, int quarter, out Vector3Int cell)
        {
            quarter = IsometricProjection.Normalize(quarter);
            if (frontByView[quarter] == null)
            {
                // Volumes are side contacts/obstacles; cells above are standing surfaces.
                // Include occupied tops so the contact height is not replaced by the highest box top.
                frontByView[quarter] = PerspectiveRules.FrontCells(DynamicGeometry, quarter);
            }
            return frontByView[quarter].TryGetValue(IsometricProjection.Project(projectedCell, quarter), out cell);
        }

        private bool OwnsProjection(Vector3Int cell, int quarter) =>
            TryGetFrontCell(cell, quarter, out var front) && front == cell;

        public bool IsStaticActive(PerspectiveLink link, int quarter) =>
            PerspectiveRules.IsActive(DynamicGeometry, link, quarter) && IsWalkVisible(link, quarter);

        public bool IsDynamicActive(PerspectiveLink link, int quarter) =>
            PerspectiveRules.IsActive(DynamicGeometry, link, quarter) && IsWalkVisible(link, quarter);

        private bool IsWalkVisible(PerspectiveLink link, int quarter) =>
            !boxes.Contains(link.CellA) && !boxes.Contains(link.CellB) &&
            OwnsProjection(link.CellA, quarter) && OwnsProjection(link.CellB, quarter) &&
            PerspectiveRules.IsVisible(DynamicGeometry, link, quarter);

        public IReadOnlyList<PerspectiveLink> ActiveLinks(int quarter)
        {
            quarter = IsometricProjection.Normalize(quarter);
            if (activeByView[quarter] != null) return activeByView[quarter];
            var active = authoredLayout.PerspectiveLinks.Where(link =>
                IsStaticActive(link, quarter)).ToList();
            foreach (var link in DynamicCandidates)
                if (IsDynamicActive(link, quarter) && !active.Any(existing => existing.SamePair(link)))
                    active.Add(link);
            // Do not deduplicate authored exits: the rule engine must still reject ambiguity.
            return activeByView[quarter] = active;
        }

        public ViewStep ResolveStep(Vector3Int source, GridDirection direction, int quarter, bool allowAir = false)
        {
            var ordinary = source + direction.ToOffset();
            foreach (var stair in authoredLayout.Stairs)
                if (stair.TryTraverse(source, direction, out var stairEnd))
                {
                    if (allowAir) return Block(stairEnd, MoveBlockReason.StairBlocked); // Boxes never climb stairs.
                    if (TryGetFrontCell(stairEnd, quarter, out var frontStair) && frontStair != stairEnd)
                        return Block(stairEnd, MoveBlockReason.PerspectiveBlocked);
                    if (boxes.Contains(stairEnd)) return Block(stairEnd, MoveBlockReason.StairBlocked);
                    if (authoredLayout.Solids.Contains(stairEnd)) return Block(stairEnd, MoveBlockReason.Solid);
                    return new ViewStep(stairEnd, false, false, Supported(stairEnd) ? MoveBlockReason.None : MoveBlockReason.UnsupportedDestination);
                }
            if (!TryGetFrontCell(ordinary, quarter, out var target))
                return new ViewStep(ordinary, false, false, !authoredLayout.Contains(ordinary) ? MoveBlockReason.OutsideGrid :
                    allowAir ? MoveBlockReason.None : MoveBlockReason.UnsupportedDestination);
            if (authoredLayout.Solids.Contains(target))
                return WithForegroundDrop(Block(target, MoveBlockReason.Solid), ordinary, quarter, allowAir);
            if (boxes.Contains(target))
            {
                // Select the contact first; remove only that actor and its upper part for validation.
                // The lower support remains present and participates in every occlusion check.
                var actors = new HashSet<Vector3Int>();
                for (var c = target; boxes.Contains(c); c += Vector3Int.up) actors.Add(c);
                var withoutActor = new PerspectiveNetwork(authoredLayout, boxes.Where(c => !actors.Contains(c)));
                var contact = withoutActor.ResolveSurface(source, direction, target, quarter);
                return WithForegroundDrop(new ViewStep(target, target != ordinary, true, contact.BlockReason), ordinary, quarter, allowAir);
            }
            return WithForegroundDrop(ResolveSurface(source, direction, target, quarter), ordinary, quarter, allowAir);
        }

        private ViewStep WithForegroundDrop(ViewStep step, Vector3Int ordinary, int quarter, bool allowAir)
        {
            // A box can leave a ledge into REAL empty space IN FRONT of a distant road/wall.
            // This is not traversal of that road, and never falls back BEHIND a foreground target.
            if (allowAir && !step.Allowed && step.BlockReason != MoveBlockReason.AmbiguousPerspectiveLink &&
                authoredLayout.Contains(ordinary) && !DynamicGeometry.Solids.Contains(ordinary) && !Supported(ordinary) &&
                CameraDepth(ordinary, quarter) > CameraDepth(step.Destination, quarter))
                return new ViewStep(ordinary, false, false, MoveBlockReason.None);
            return step;
        }

        private ViewStep ResolveSurface(Vector3Int source, GridDirection direction, Vector3Int target, int quarter)
        {
            var ordinary = source + direction.ToOffset();
            if (!authoredLayout.Contains(target)) return Block(target, MoveBlockReason.OutsideGrid);
            if (!Supported(target)) return Block(target, MoveBlockReason.UnsupportedDestination);
            var edge = new PerspectiveLink(source, target, direction, 1 << IsometricProjection.Normalize(quarter));
            if (!PerspectiveRules.IsVisible(DynamicGeometry, edge, quarter)) return Block(target, MoveBlockReason.PerspectiveBlocked);
            if (target == ordinary) return new ViewStep(target, false, false, MoveBlockReason.None);
            // Keep the selected endpoint. A failed foreground route cannot choose a rear route.
            var count = ActiveLinks(quarter).Count(l => l.TryTraverse(source, direction, out var end) && end == target);
            if (count > 1) return Block(target, MoveBlockReason.AmbiguousPerspectiveLink);
            return new ViewStep(target, true, false, count == 1 ? MoveBlockReason.None : MoveBlockReason.PerspectiveBlocked);
        }

        private bool Supported(Vector3Int cell) => cell.y > 0 && DynamicGeometry.Solids.Contains(cell + Vector3Int.down);
        private static ViewStep Block(Vector3Int cell, MoveBlockReason reason) => new ViewStep(cell, false, false, reason);
    }
}
