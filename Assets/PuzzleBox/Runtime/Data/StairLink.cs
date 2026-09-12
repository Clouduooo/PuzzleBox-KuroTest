using System;
using UnityEngine;

namespace PuzzleBox.Data
{
    [Serializable]
    public struct StairLink : IEquatable<StairLink>
    {
        [SerializeField] private Vector3Int lowerCell;
        [SerializeField] private GridDirection upDirection;

        public Vector3Int LowerCell => lowerCell;
        public GridDirection UpDirection => upDirection;
        public Vector3Int UpperCell => lowerCell + Vector3Int.up + upDirection.ToOffset();

        public StairLink(Vector3Int lowerCell, GridDirection upDirection)
        {
            this.lowerCell = lowerCell;
            this.upDirection = upDirection;
        }

        public bool TryTraverse(Vector3Int from, GridDirection direction, out Vector3Int destination)
        {
            if (from == LowerCell && direction == UpDirection)
            {
                destination = UpperCell;
                return true;
            }

            if (from == UpperCell && direction == UpDirection.Opposite())
            {
                destination = LowerCell;
                return true;
            }

            destination = default;
            return false;
        }

        public bool Equals(StairLink other)
        {
            return lowerCell.Equals(other.lowerCell) && upDirection == other.upDirection;
        }

        public override bool Equals(object obj)
        {
            return obj is StairLink other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (lowerCell.GetHashCode() * 397) ^ (int)upDirection;
            }
        }
    }
}
