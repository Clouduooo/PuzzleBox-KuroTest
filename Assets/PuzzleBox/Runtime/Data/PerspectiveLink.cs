using System;
using UnityEngine;

namespace PuzzleBox.Data
{
    /// <summary>Standing cells joined across opposite surface edges in selected isometric views.</summary>
    [Serializable]
    public struct PerspectiveLink
    {
        [SerializeField] private Vector3Int cellA;
        [SerializeField] private Vector3Int cellB;
        [SerializeField] private GridDirection directionFromA;
        [SerializeField] private int viewMask;
        public Vector3Int CellA => cellA;
        public Vector3Int CellB => cellB;
        public GridDirection DirectionFromA => directionFromA;
        public int ViewMask => viewMask;
        public PerspectiveLink(Vector3Int a, Vector3Int b, GridDirection direction, int mask)
        { cellA = a; cellB = b; directionFromA = direction; viewMask = mask & 15; }
        public bool EnabledIn(int quarter) => (viewMask & (1 << IsometricProjection.Normalize(quarter))) != 0;
        public bool TryTraverse(Vector3Int source, GridDirection direction, out Vector3Int destination)
        {
            destination = source;
            if (source == cellA && direction == directionFromA) { destination = cellB; return true; }
            if (source == cellB && direction == directionFromA.Opposite()) { destination = cellA; return true; }
            return false;
        }
        public bool SamePair(PerspectiveLink other) =>
            (cellA == other.cellA && cellB == other.cellB && directionFromA == other.directionFromA) ||
            (cellA == other.cellB && cellB == other.cellA && directionFromA == other.directionFromA.Opposite());
    }

    /// <summary>Matches Unity Euler(35.26439, 45 + quarter * 90, 0), independent of zoom/resolution.</summary>
    public static class IsometricProjection
    {
        public const float Pitch = 35.26439f;
        public static int Normalize(int quarter) => (quarter % 4 + 4) % 4;
        public static Quaternion Rotation(int quarter) => Quaternion.Euler(Pitch, 45f + Normalize(quarter) * 90f, 0f);
        public static Vector2Int Project(Vector3Int p, int quarter)
        {
            int x, z;
            switch (Normalize(quarter))
            {
                case 1: x = -p.z; z = p.x; break;
                case 2: x = -p.x; z = -p.z; break;
                case 3: x = p.z; z = -p.x; break;
                default: x = p.x; z = p.z; break;
            }
            return new Vector2Int(x - z, x + z + 2 * p.y);
        }
        public static Vector3 Edge(Vector3Int cell, GridDirection outward) =>
            (Vector3)cell + (Vector3)outward.ToOffset() * .5f + Vector3.down * .5f;
        public static int AlignedViews(Vector3Int a, Vector3Int b, GridDirection direction)
        {
            if (a == b || b == a + direction.ToOffset()) return 0;
            var edgeA = 2 * a + direction.ToOffset() + Vector3Int.down;
            var edgeB = 2 * b - direction.ToOffset() + Vector3Int.down;
            var mask = 0;
            for (var q = 0; q < 4; q++) if (Project(edgeA, q) == Project(edgeB, q)) mask |= 1 << q;
            return mask;
        }
    }
}
