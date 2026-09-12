using System.Collections.Generic;
using System.Linq;
using PuzzleBox.Data;
using UnityEngine;

namespace PuzzleBox.Rules
{
    /// <summary>Discrete screen-vertical gravity, independent of camera zoom and render frames.</summary>
    public static class PerspectiveGravity
    {
        public static bool TryFindLanding(LevelLayout layout, IEnumerable<Vector3Int> stationaryBoxes,
            Vector3Int start, int quarter, out Vector3Int landing)
        {
            var volumes = new HashSet<Vector3Int>(layout.Solids.Concat(stationaryBoxes));
            var screen = IsometricProjection.Project(start, quarter);
            var candidates = volumes.Select(s => s + Vector3Int.up)
                .Where(c => layout.Contains(c) && !volumes.Contains(c))
                .Select(c => new { Cell = c, Screen = IsometricProjection.Project(c, quarter) })
                // Zero-height matches are NOT gravity or a license to bypass road authoring.
                .Where(c => c.Screen.x == screen.x && c.Screen.y < screen.y)
                .OrderByDescending(c => c.Screen.y)
                .ThenByDescending(c => PerspectiveNetwork.CameraDepth(c.Cell, quarter))
                .ThenBy(c => c.Cell.x).ThenBy(c => c.Cell.y).ThenBy(c => c.Cell.z);
            foreach (var candidate in candidates)
            {
                var cell = candidate.Cell;
                // Real vertical collision cannot be bypassed just because a wall hides it.
                // Remote floors must expose their top at the projected landing point.
                if ((cell.x != start.x || cell.z != start.z) &&
                    !IsPointVisible(volumes, (Vector3)cell + Vector3.down * .475f, quarter)) continue;
                landing = cell;
                return true;
            }
            landing = default;
            return false;
        }

        public static bool IsPointVisible(IEnumerable<Vector3Int> volumes, Vector3 point, int quarter)
        {
            var ray = new Ray(point, -(IsometricProjection.Rotation(quarter) * Vector3.forward));
            foreach (var cell in volumes)
                if (new Bounds(cell, Vector3.one * .998f).IntersectRay(ray, out var distance) && distance > .001f)
                    return false;
            return true;
        }

        public static float DropDistance(Vector3 start, Vector3 end, int quarter)
        {
            var screenUp = IsometricProjection.Rotation(quarter) * Vector3.up;
            return Mathf.Max(0f, Vector3.Dot(start - end, screenUp) / Vector3.Dot(Vector3.up, screenUp));
        }
    }
}
