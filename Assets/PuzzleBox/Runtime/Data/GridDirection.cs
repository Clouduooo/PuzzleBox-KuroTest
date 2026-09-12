using System;
using UnityEngine;

namespace PuzzleBox.Data
{
    public enum GridDirection
    {
        North,
        East,
        South,
        West
    }

    public static class GridDirectionExtensions
    {
        public static Vector3Int ToOffset(this GridDirection direction)
        {
            switch (direction)
            {
                case GridDirection.North: return Vector3Int.forward;
                case GridDirection.East: return Vector3Int.right;
                case GridDirection.South: return Vector3Int.back;
                case GridDirection.West: return Vector3Int.left;
                default: throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
            }
        }

        public static GridDirection Opposite(this GridDirection direction)
        {
            return (GridDirection)(((int)direction + 2) % 4);
        }
    }
}
