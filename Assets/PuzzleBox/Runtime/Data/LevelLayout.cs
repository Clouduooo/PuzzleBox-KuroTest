using System;
using System.Collections.Generic;
using UnityEngine;

namespace PuzzleBox.Data
{
    public sealed class LevelLayout
    {
        public Vector3Int Size { get; }
        public HashSet<Vector3Int> Solids { get; }
        public HashSet<Vector3Int> Targets { get; }
        public List<Vector3Int> Boxes { get; }
        public List<StairLink> Stairs { get; }
        public Vector3Int? PlayerSpawn { get; }
        public List<PerspectiveLink> PerspectiveLinks { get; }
        public int InitialViewQuarter { get; }

        public LevelLayout(
            Vector3Int size,
            IEnumerable<Vector3Int> solids,
            IEnumerable<Vector3Int> targets,
            IEnumerable<Vector3Int> boxes,
            IEnumerable<StairLink> stairs,
            Vector3Int? playerSpawn, IEnumerable<PerspectiveLink> perspectiveLinks = null, int initialViewQuarter = 0)
        {
            if (size.x <= 0 || size.y <= 0 || size.z <= 0)
                throw new ArgumentOutOfRangeException(nameof(size), "All grid dimensions must be positive.");

            Size = size;
            Solids = new HashSet<Vector3Int>(solids ?? Array.Empty<Vector3Int>());
            Targets = new HashSet<Vector3Int>(targets ?? Array.Empty<Vector3Int>());
            Boxes = new List<Vector3Int>(boxes ?? Array.Empty<Vector3Int>());
            Stairs = new List<StairLink>(stairs ?? Array.Empty<StairLink>());
            PlayerSpawn = playerSpawn;
            PerspectiveLinks = new List<PerspectiveLink>(perspectiveLinks ?? Array.Empty<PerspectiveLink>());
            InitialViewQuarter = IsometricProjection.Normalize(initialViewQuarter);
        }

        public bool Contains(Vector3Int cell)
        {
            return cell.x >= 0 && cell.x < Size.x &&
                   cell.y >= 0 && cell.y < Size.y &&
                   cell.z >= 0 && cell.z < Size.z;
        }
    }
}
