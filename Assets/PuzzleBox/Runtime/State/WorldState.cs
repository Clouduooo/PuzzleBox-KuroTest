using System.Collections.Generic;
using System.Linq;
using PuzzleBox.Data;
using PuzzleBox.Rules;
using UnityEngine;

namespace PuzzleBox.State
{
    public enum FailureReason
    {
        None,
        BoxHitPlayer,
        BoxFellOutOfBounds,
        BoxLandingBlocked
    }

    public sealed class WorldState
    {
        private readonly Dictionary<int, Vector3Int> boxes = new Dictionary<int, Vector3Int>();

        public LevelLayout Layout { get; }
        public Vector3Int PlayerPosition { get; internal set; }
        public IReadOnlyDictionary<int, Vector3Int> Boxes => boxes;
        public int MoveCount { get; internal set; }
        public int PushCount { get; internal set; }
        public int ViewQuarter { get; internal set; }
        public int RotationCount { get; internal set; }
        public bool IsComplete { get; internal set; }
        public FailureReason Failure { get; internal set; }
        public PerspectiveNetwork PerspectiveNetwork { get; private set; }

        internal Dictionary<int, Vector3Int> MutableBoxes => boxes;

        public WorldState(LevelLayout layout)
        {
            Layout = layout;
            PlayerPosition = layout.PlayerSpawn ?? default;
            ViewQuarter = layout.InitialViewQuarter;
            for (var index = 0; index < layout.Boxes.Count; index++)
                boxes[index] = layout.Boxes[index];
            RefreshPerspectiveNetwork();
        }

        internal void RefreshPerspectiveNetwork() =>
            PerspectiveNetwork = new PerspectiveNetwork(Layout, boxes.Values);

        public bool TryGetBoxAt(Vector3Int cell, out int boxId)
        {
            foreach (var pair in boxes)
            {
                if (pair.Value != cell) continue;
                boxId = pair.Key;
                return true;
            }

            boxId = -1;
            return false;
        }

        public bool IsOccupiedByBox(Vector3Int cell)
        {
            return boxes.Values.Contains(cell);
        }

        internal WorldSnapshot Capture()
        {
            return new WorldSnapshot(this);
        }

        internal void Restore(WorldSnapshot snapshot)
        {
            boxes.Clear();
            foreach (var pair in snapshot.Boxes)
                boxes.Add(pair.Key, pair.Value);

            PlayerPosition = snapshot.PlayerPosition;
            MoveCount = snapshot.MoveCount;
            PushCount = snapshot.PushCount;
            ViewQuarter = snapshot.ViewQuarter;
            RotationCount = snapshot.RotationCount;
            IsComplete = snapshot.IsComplete;
            Failure = snapshot.Failure;
            RefreshPerspectiveNetwork();
        }
    }

    internal sealed class WorldSnapshot
    {
        public Dictionary<int, Vector3Int> Boxes { get; }
        public Vector3Int PlayerPosition { get; }
        public int MoveCount { get; }
        public int PushCount { get; }
        public int ViewQuarter { get; }
        public int RotationCount { get; }
        public bool IsComplete { get; }
        public FailureReason Failure { get; }

        public WorldSnapshot(WorldState state)
        {
            Boxes = new Dictionary<int, Vector3Int>(state.Boxes);
            PlayerPosition = state.PlayerPosition;
            MoveCount = state.MoveCount;
            PushCount = state.PushCount;
            ViewQuarter = state.ViewQuarter;
            RotationCount = state.RotationCount;
            IsComplete = state.IsComplete;
            Failure = state.Failure;
        }
    }
}
