using System.Collections.Generic;
using System.Linq;
using PuzzleBox.Data;
using PuzzleBox.State;
using UnityEngine;

namespace PuzzleBox.Rules
{
    public sealed partial class PuzzleBoxRuleEngine
    {
        private void ResolveGravity()
        {
            // Lower SCREEN columns settle first, so a falling stack never rests on a
            // different, still-floating stack. Each group lands once on real support.
            var orderedIds = State.Boxes.OrderBy(pair => IsometricProjection.Project(pair.Value, State.ViewQuarter).y)
                .ThenBy(pair => pair.Key).Select(pair => pair.Key).ToList();
            foreach (var id in orderedIds)
            {
                if (!State.Boxes.TryGetValue(id, out var start) || IsSupported(start)) continue;
                var stack = GetPushStack(start).ToList();
                var moving = new HashSet<int>(stack);
                var stationary = State.Boxes.Where(pair => !moving.Contains(pair.Key)).Select(pair => pair.Value).ToList();
                var found = PerspectiveGravity.TryFindLanding(State.Layout, stationary, start, State.ViewQuarter,
                    out var landing);
                var startScreen = IsometricProjection.Project(start, State.ViewQuarter);
                var endScreen = found ? IsometricProjection.Project(landing, State.ViewQuarter) : new Vector2Int(startScreen.x, int.MinValue);

                if (State.Layout.PlayerSpawn.HasValue)
                {
                    var player = State.PlayerPosition;
                    var p = IsometricProjection.Project(player, State.ViewQuarter);
                    if (p.x == startScreen.x && p.y < startScreen.y && p.y >= endScreen.y &&
                        ((player.x == start.x && player.z == start.z) ||
                         PerspectiveGravity.IsPointVisible(State.Layout.Solids.Concat(stationary), player, State.ViewQuarter)))
                    {
                        // Stop a screen-cell above the cat. The depth transfer has zero
                        // projected displacement, and the entire action remains undoable.
                        var stop = player + Vector3Int.up;
                        if (CanPlaceFallingStack(stack, moving, stop - start))
                            foreach (var boxId in stack) State.MutableBoxes[boxId] += stop - start;
                        State.Failure = FailureReason.BoxHitPlayer;
                        return;
                    }
                }

                if (!found)
                {
                    foreach (var boxId in stack) State.MutableBoxes.Remove(boxId);
                    State.Failure = FailureReason.BoxFellOutOfBounds;
                    return;
                }
                var offset = landing - start;
                if (!CanPlaceFallingStack(stack, moving, offset))
                {
                    State.Failure = FailureReason.BoxLandingBlocked;
                    return;
                }
                foreach (var boxId in stack) State.MutableBoxes[boxId] += offset;
            }
        }

        private bool CanPlaceFallingStack(IEnumerable<int> stack, HashSet<int> moving, Vector3Int offset)
        {
            foreach (var id in stack)
            {
                var cell = State.Boxes[id] + offset;
                if (!State.Layout.Contains(cell) || State.Layout.Solids.Contains(cell) ||
                    (State.TryGetBoxAt(cell, out var other) && !moving.Contains(other))) return false;
            }
            return true;
        }
    }
}
