using System.Collections.Generic;
using System.Linq;
using PuzzleBox.Data;
using UnityEngine;

namespace PuzzleBox.Rules
{
    public static class PerspectiveRules
    {
        public static bool IsSurface(LevelLayout layout, Vector3Int cell) => layout.Contains(cell) &&
            !layout.Solids.Contains(cell) && layout.Solids.Contains(cell + Vector3Int.down) &&
            !layout.Stairs.Any(s => s.LowerCell == cell);
        public static bool IsOpenEdge(LevelLayout layout, Vector3Int cell, GridDirection outward)
        {
            var adjacent = cell + outward.ToOffset();
            return IsSurface(layout, cell) && !layout.Solids.Contains(adjacent) &&
                   !IsSurface(layout, adjacent) && !layout.Stairs.Any(s => s.TryTraverse(cell, outward, out _));
        }
        public static int ValidViews(LevelLayout layout, PerspectiveLink link)
        {
            var aligned = IsometricProjection.AlignedViews(link.CellA, link.CellB, link.DirectionFromA);
            var valid = 0;
            for (var q = 0; q < 4; q++)
                if ((aligned & (1 << q)) != 0 && IsOpenEdge(layout, link.CellA, link.DirectionFromA, q) &&
                    IsOpenEdge(layout, link.CellB, link.DirectionFromA.Opposite(), q)) valid |= 1 << q;
            return valid;
        }

        public static Dictionary<Vector2Int, Vector3Int> FrontCells(LevelLayout geometry, int quarter)
        {
            var result = new Dictionary<Vector2Int, Vector3Int>();
            foreach (var c in geometry.Solids.Concat(geometry.Solids.Select(s => s + Vector3Int.up)))
            {
                if (!geometry.Contains(c)) continue;
                var p = IsometricProjection.Project(c, quarter);
                if (!result.TryGetValue(p, out var old) || PerspectiveNetwork.CameraDepth(c, quarter) > PerspectiveNetwork.CameraDepth(old, quarter))
                    result[p] = c;
            }
            return result;
        }

        public static bool IsOpenEdge(LevelLayout layout, Vector3Int cell, GridDirection outward, int quarter)
            => IsOpenEdge(layout, cell, outward, quarter, null);

        private static bool IsOpenEdge(LevelLayout layout, Vector3Int cell, GridDirection outward, int quarter,
            Dictionary<Vector2Int, Vector3Int> visible)
        {
            if (!IsSurface(layout, cell) || layout.Stairs.Any(s => s.TryTraverse(cell, outward, out _))) return false;
            var adjacent = cell + outward.ToOffset();
            if (!layout.Solids.Contains(adjacent) && !IsSurface(layout, adjacent)) return true;
            // A real continuation hidden behind the foreground landing is not the visible exit.
            var front = visible ?? FrontCells(layout, quarter);
            return front.TryGetValue(IsometricProjection.Project(adjacent, quarter), out var owner) && owner != adjacent;
        }
        public static bool IsVisible(LevelLayout layout, PerspectiveLink link, int quarter) =>
            !TryGetOccludingSolid(layout, link, quarter, out _);

        public static bool TryGetOccludingSolid(LevelLayout layout, PerspectiveLink link, int quarter,
            out Vector3Int blocker)
        {
            // Projection alignment alone is insufficient: every Solid, including either
            // endpoint's support, can hide the road and invalidate the connection.
            var towardCamera = -(IsometricProjection.Rotation(quarter) * Vector3.forward);
            var direction = (Vector3)link.DirectionFromA.ToOffset();
            var a = IsometricProjection.Edge(link.CellA, link.DirectionFromA);
            var b = IsometricProjection.Edge(link.CellB, link.DirectionFromA.Opposite());
            return TryGetRayBlocker(layout, a - direction * .24f + Vector3.up * .025f, towardCamera,
                       out blocker) ||
                   TryGetRayBlocker(layout, b + direction * .24f + Vector3.up * .025f, towardCamera,
                       out blocker);
        }
        private static bool TryGetRayBlocker(LevelLayout layout, Vector3 point, Vector3 direction,
            out Vector3Int blocker)
        {
            var ray = new Ray(point, direction);
            var nearestDistance = float.PositiveInfinity;
            var found = false;
            blocker = default;
            foreach (var solid in layout.Solids)
            {
                if (!new Bounds(solid, Vector3.one * .998f).IntersectRay(ray, out var distance) ||
                    distance <= .001f || distance >= nearestDistance) continue;
                nearestDistance = distance;
                blocker = solid;
                found = true;
            }
            return found;
        }
        public static bool IsActive(LevelLayout layout, PerspectiveLink link, int quarter) =>
            link.EnabledIn(quarter) && (ValidViews(layout, link) & (1 << IsometricProjection.Normalize(quarter))) != 0 &&
            IsVisible(layout, link, quarter);
        public static List<Vector3Int> Surfaces(LevelLayout layout) => layout.Solids
            .Select(s => s + Vector3Int.up).Where(c => IsSurface(layout, c))
            .OrderBy(c => c.x).ThenBy(c => c.y).ThenBy(c => c.z).ToList();
        public static List<PerspectiveLink> FindCandidates(LevelLayout layout)
        {
            var result = new List<PerspectiveLink>();
            var cells = Surfaces(layout);
            for (var q = 0; q < 4; q++)
            for (var d = 0; d < 2; d++)
            {
                var visible = FrontCells(layout, q);
                var direction = (GridDirection)d;
                var reverse = direction.Opposite();
                var index = new Dictionary<Vector2Int, List<Vector3Int>>();
                foreach (var b in cells)
                {
                    if (!IsOpenEdge(layout, b, reverse, q, visible)) continue;
                    var key = IsometricProjection.Project(2 * b + reverse.ToOffset() + Vector3Int.down, q);
                    if (!index.TryGetValue(key, out var bucket)) index[key] = bucket = new List<Vector3Int>();
                    bucket.Add(b);
                }
                foreach (var a in cells)
                {
                    if (!IsOpenEdge(layout, a, direction, q, visible)) continue;
                    var key = IsometricProjection.Project(2 * a + direction.ToOffset() + Vector3Int.down, q);
                    if (!index.TryGetValue(key, out var bucket)) continue;
                    foreach (var b in bucket)
                    {
                        var mask = ValidViews(layout, new PerspectiveLink(a, b, direction, 15));
                        if (mask == 0) continue;
                        var link = new PerspectiveLink(a, b, direction, mask);
                        if (!result.Any(existing => existing.SamePair(link))) result.Add(link);
                    }
                }
            }
            return result;
        }

        public static HashSet<Vector3Int> Reachable(LevelLayout layout, int quarter, PerspectiveNetwork network = null)
        {
            var reached = new HashSet<Vector3Int>();
            if (!layout.PlayerSpawn.HasValue) return reached;
            var queue = new Queue<Vector3Int>();
            reached.Add(layout.PlayerSpawn.Value);
            queue.Enqueue(layout.PlayerSpawn.Value);
            network = network ?? new PerspectiveNetwork(layout, layout.Boxes);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                for (var d = 0; d < 4; d++)
                {
                    var direction = (GridDirection)d;
                    var step = network.ResolveStep(cell, direction, quarter);
                    if (step.Allowed && !step.IsBox && reached.Add(step.Destination)) queue.Enqueue(step.Destination);
                }
            }
            return reached;
        }
    }
}
