using System.Collections.Generic;
using PuzzleBox.State;
using PuzzleBox.Data;
using UnityEngine;

namespace PuzzleBox.Rules
{
    public enum MoveBlockReason
    {
        None,
        MissingPlayer,
        GameEnded,
        OutsideGrid,
        Solid,
        UnsupportedDestination,
        StairBlocked,
        BoxBlocked,
        PerspectiveBlocked,
        AmbiguousPerspectiveLink
    }

    public readonly struct MoveResult
    {
        public bool Succeeded { get; }
        public bool Pushed { get; }
        public MoveBlockReason BlockReason { get; }
        public IReadOnlyList<int> MovedBoxIds { get; }
        public FailureReason Failure { get; }
        public bool IsComplete { get; }
        public bool UsedPerspectiveLink => PlayerUsedPerspectiveLink || BoxesUsedPerspectiveLink;
        public bool PlayerUsedPerspectiveLink { get; }
        public bool BoxesUsedPerspectiveLink { get; }
        /// <summary>Shared stack displacement before gravity, including any seam height change.</summary>
        public Vector3Int BoxTravelOffset { get; }
        public GridDirection TravelDirection { get; }

        public MoveResult(
            bool succeeded,
            bool pushed,
            MoveBlockReason blockReason,
            IReadOnlyList<int> movedBoxIds,
            FailureReason failure,
            bool isComplete, bool usedPerspectiveLink = false, GridDirection travelDirection = GridDirection.North,
            bool boxesUsedPerspectiveLink = false, Vector3Int boxTravelOffset = default)
        {
            Succeeded = succeeded;
            Pushed = pushed;
            BlockReason = blockReason;
            MovedBoxIds = movedBoxIds ?? new int[0];
            Failure = failure;
            IsComplete = isComplete;
            PlayerUsedPerspectiveLink = usedPerspectiveLink;
            BoxesUsedPerspectiveLink = boxesUsedPerspectiveLink;
            BoxTravelOffset = boxTravelOffset;
            TravelDirection = travelDirection;
        }

        public static MoveResult Blocked(MoveBlockReason reason)
        {
            return new MoveResult(false, false, reason, new int[0], FailureReason.None, false);
        }
    }
}
