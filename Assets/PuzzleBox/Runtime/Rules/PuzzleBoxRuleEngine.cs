using System.Collections.Generic;
using System.Linq;
using PuzzleBox.Data;
using PuzzleBox.State;
using UnityEngine;

namespace PuzzleBox.Rules
{
    public sealed partial class PuzzleBoxRuleEngine
    {
        private readonly Stack<WorldSnapshot> undoHistory = new Stack<WorldSnapshot>();

        public WorldState State { get; }
        public bool CanUndo => undoHistory.Count > 0;

        /// <summary>Read-only draft prediction. No initial gravity, audio, history or resource writes.</summary>
        public static MoveResult PreviewMove(LevelLayout layout, Vector3Int source, int quarter,
            GridDirection direction, out ViewStep target, out WorldState settled)
        {
            var preview = new PuzzleBoxRuleEngine(new LevelLayout(layout.Size, layout.Solids, layout.Targets,
                layout.Boxes, layout.Stairs, source, layout.PerspectiveLinks, quarter), false);
            target = preview.State.PerspectiveNetwork.ResolveStep(source, direction, quarter);
            var result = preview.TryMove(direction);
            settled = preview.State;
            return result;
        }

        public PuzzleBoxRuleEngine(LevelLayout layout, bool settleInitialGravity = true)
        {
            State = new WorldState(layout);
            if (settleInitialGravity)
            {
                ResolveGravity();
                State.RefreshPerspectiveNetwork();
            }
            EvaluateCompletion();
            undoHistory.Clear();
        }

        public MoveResult TryMove(GridDirection direction)
        {
            if (!State.Layout.PlayerSpawn.HasValue)
                return MoveResult.Blocked(MoveBlockReason.MissingPlayer);
            if (State.IsComplete || State.Failure != FailureReason.None)
                return MoveResult.Blocked(MoveBlockReason.GameEnded);

            var source = State.PlayerPosition;
            var step = State.PerspectiveNetwork.ResolveStep(source, direction, State.ViewQuarter);
            if (!step.Allowed) return MoveResult.Blocked(step.BlockReason);
            var destination = step.Destination;
            if (State.TryGetBoxAt(destination, out var firstBox))
                return TryPush(direction, destination, firstBox, step.UsesLink);

            undoHistory.Push(State.Capture());
            State.PlayerPosition = destination;
            State.MoveCount++;
            EvaluateCompletion();
            return new MoveResult(true, false, MoveBlockReason.None, new int[0], State.Failure, State.IsComplete, step.UsesLink, direction);
        }

        public bool Undo()
        {
            if (undoHistory.Count == 0) return false;
            State.Restore(undoHistory.Pop());
            return true;
        }

        public bool RotateView(int quarterTurns)
        {
            if (IsometricProjection.Normalize(quarterTurns) == 0 || State.Failure != FailureReason.None || State.IsComplete)
                return false;
            undoHistory.Push(State.Capture());
            State.ViewQuarter = IsometricProjection.Normalize(State.ViewQuarter + quarterTurns);
            State.RotationCount += Mathf.Abs(quarterTurns);
            return true;
        }

        public IReadOnlyList<int> GetPushStack(Vector3Int firstBoxCell)
        {
            var result = new List<int>();
            var cell = firstBoxCell;
            while (State.TryGetBoxAt(cell, out var boxId))
            {
                result.Add(boxId);
                cell += Vector3Int.up;
            }
            return result;
        }

        private MoveResult TryPush(GridDirection direction, Vector3Int firstBoxCell, int firstBoxId,
            bool playerUsedPerspectiveLink = false)
        {
            var moving = GetPushStack(firstBoxCell).ToList();
            if (moving.Count == 0 || moving[0] != firstBoxId)
                return MoveResult.Blocked(MoveBlockReason.BoxBlocked);

            var movingSet = new HashSet<int>(moving);
            var offset = direction.ToOffset();
            // The contacted bottom box chooses the route for the entire stack. Upper boxes
            // keep their relative positions; they never choose independent connections.
            var pushNetwork = new PerspectiveNetwork(State.Layout, State.Boxes
                .Where(pair => !movingSet.Contains(pair.Key)).Select(pair => pair.Value));
            var step = pushNetwork.ResolveStep(firstBoxCell, direction, State.ViewQuarter, true);
            if (!step.Allowed || step.IsBox)
                return MoveResult.Blocked(step.BlockReason == MoveBlockReason.AmbiguousPerspectiveLink
                    ? step.BlockReason : MoveBlockReason.BoxBlocked);
            offset = step.Destination - firstBoxCell;
            var boxesUsedPerspectiveLink = step.UsesLink;
            foreach (var boxId in moving)
            {
                var destination = State.Boxes[boxId] + offset;
                if (!State.Layout.Contains(destination) || State.Layout.Solids.Contains(destination))
                    return MoveResult.Blocked(MoveBlockReason.BoxBlocked);

                if (State.TryGetBoxAt(destination, out var blocker) && !movingSet.Contains(blocker))
                    return MoveResult.Blocked(MoveBlockReason.BoxBlocked);
                // Upper boxes share the bottom route, but may not disappear behind a higher
                // foreground surface/volume. They cannot choose an independent depth transfer.
                if (pushNetwork.TryGetFrontCell(destination, State.ViewQuarter, out var front) && front != destination &&
                    PerspectiveNetwork.CameraDepth(front, State.ViewQuarter) > PerspectiveNetwork.CameraDepth(destination, State.ViewQuarter))
                    return MoveResult.Blocked(MoveBlockReason.BoxBlocked);
            }

            var beforePush = State.Capture();
            foreach (var boxId in moving)
                State.MutableBoxes[boxId] += offset;

            State.PlayerPosition = firstBoxCell;
            State.MoveCount++;
            State.PushCount++;
            ResolveGravity();
            if (State.Failure == FailureReason.BoxLandingBlocked)
            {
                // Never skip an obstructed first landing and tunnel to a lower platform.
                State.Restore(beforePush);
                return MoveResult.Blocked(MoveBlockReason.BoxBlocked);
            }
            undoHistory.Push(beforePush);
            State.RefreshPerspectiveNetwork();
            EvaluateCompletion();

            return new MoveResult(true, true, MoveBlockReason.None, moving, State.Failure, State.IsComplete,
                playerUsedPerspectiveLink, direction, boxesUsedPerspectiveLink, offset);
        }

        private bool IsSupported(Vector3Int cell)
        {
            if (cell.y <= 0) return false;
            var below = cell + Vector3Int.down;
            return State.Layout.Solids.Contains(below) || State.IsOccupiedByBox(below);
        }

        private void EvaluateCompletion()
        {
            if (State.Failure != FailureReason.None)
            {
                State.IsComplete = false;
                return;
            }

            State.IsComplete = State.Layout.Targets.Count > 0 &&
                               State.Layout.Targets.Count == State.MutableBoxes.Count &&
                               State.MutableBoxes.Values.All(State.Layout.Targets.Contains);
        }
    }
}
