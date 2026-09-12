using System;
using System.Collections.Generic;
using UnityEngine;

namespace PuzzleBox.Data
{
    [CreateAssetMenu(fileName = "PuzzleBoxLevel", menuName = "Puzzle Box/Level")]
    public sealed class LevelDefinition : ScriptableObject
    {
        [SerializeField] private string levelId = "";
        [SerializeField] private string displayName = "New Level";
        [SerializeField, TextArea] private string designNotes = "";
        [SerializeField] private int dataVersion = 1;
        [SerializeField] private Vector3Int size = new Vector3Int(8, 6, 8);
        [SerializeField, Min(0.1f)] private float cellSize = 1f;
        [SerializeField] private PuzzleBox.View.ParkTheme artTheme;
        [SerializeField] private List<Vector3Int> solids = new List<Vector3Int>();
        [SerializeField] private List<Vector3Int> targets = new List<Vector3Int>();
        [SerializeField] private List<Vector3Int> boxes = new List<Vector3Int>();
        [SerializeField] private List<StairLink> stairs = new List<StairLink>();
        [SerializeField] private List<PerspectiveLink> perspectiveLinks = new List<PerspectiveLink>();
        [SerializeField, Range(0, 3)] private int initialViewQuarter;
        [SerializeField] private bool hasPlayerSpawn;
        [SerializeField] private Vector3Int playerSpawn;

        public string LevelId => levelId;
        public string DisplayName => displayName;
        public string DesignNotes => designNotes;
        public int DataVersion => dataVersion;
        public Vector3Int Size => size;
        public float CellSize => cellSize;
        public PuzzleBox.View.ParkTheme ArtTheme => artTheme != null ? artTheme :
            Resources.Load<PuzzleBox.View.ParkTheme>(PuzzleBox.View.ParkTheme.DefaultResource);
        public IReadOnlyList<Vector3Int> Solids => solids;
        public IReadOnlyList<Vector3Int> Targets => targets;
        public IReadOnlyList<Vector3Int> Boxes => boxes;
        public IReadOnlyList<StairLink> Stairs => stairs;
        public IReadOnlyList<PerspectiveLink> PerspectiveLinks => perspectiveLinks;
        public int InitialViewQuarter => IsometricProjection.Normalize(initialViewQuarter);
        public bool HasPlayerSpawn => hasPlayerSpawn;
        public Vector3Int PlayerSpawn => playerSpawn;

        public LevelLayout CreateLayout()
        {
            return new LevelLayout(size, solids, targets, boxes, stairs,
                hasPlayerSpawn ? playerSpawn : (Vector3Int?)null, perspectiveLinks, InitialViewQuarter);
        }

        public void InitializeNew(string name)
        {
            levelId = Guid.NewGuid().ToString("N");
            displayName = string.IsNullOrWhiteSpace(name) ? "New Level" : name;
            dataVersion = 2;
            size = new Vector3Int(8, 6, 8);
            cellSize = 1f;
            solids.Clear();
            targets.Clear();
            boxes.Clear();
            stairs.Clear();
            perspectiveLinks.Clear();
            initialViewQuarter = 0;
            hasPlayerSpawn = false;
            playerSpawn = default;
        }

        public void PrepareDuplicate(string name)
        {
            levelId = Guid.NewGuid().ToString("N");
            displayName = string.IsNullOrWhiteSpace(name) ? displayName + " Copy" : name;
        }

        public bool Contains(Vector3Int cell)
        {
            return cell.x >= 0 && cell.x < size.x &&
                   cell.y >= 0 && cell.y < size.y &&
                   cell.z >= 0 && cell.z < size.z;
        }

        public bool AddSolid(Vector3Int cell)
        {
            if (!Contains(cell) || solids.Contains(cell) || targets.Contains(cell) || boxes.Contains(cell) ||
                (hasPlayerSpawn && playerSpawn == cell)) return false;
            if (stairs.Exists(stair => stair.LowerCell == cell || stair.UpperCell == cell)) return false;
            solids.Add(cell);
            return true;
        }

        public void SetInitialView(int quarter) { initialViewQuarter = IsometricProjection.Normalize(quarter); }
        public bool AddPerspectiveLink(PerspectiveLink link)
        {
            var valid = PuzzleBox.Rules.PerspectiveRules.ValidViews(CreateLayout(), link);
            if (link.ViewMask == 0 || (link.ViewMask & valid) != link.ViewMask ||
                perspectiveLinks.Exists(existing => existing.SamePair(link))) return false;
            perspectiveLinks.Add(link);
            dataVersion = 2;
            return true;
        }
        public void RemovePerspectiveLink(int index)
        {
            if (index >= 0 && index < perspectiveLinks.Count) perspectiveLinks.RemoveAt(index);
        }

        public bool AddTarget(Vector3Int cell)
        {
            if (!Contains(cell) || solids.Contains(cell) || targets.Contains(cell)) return false;
            targets.Add(cell);
            return true;
        }

        public bool AddBox(Vector3Int cell)
        {
            if (!Contains(cell) || solids.Contains(cell) || boxes.Contains(cell) ||
                (hasPlayerSpawn && playerSpawn == cell)) return false;
            boxes.Add(cell);
            return true;
        }

        public bool SetPlayer(Vector3Int cell)
        {
            if (!Contains(cell) || solids.Contains(cell) || boxes.Contains(cell)) return false;
            hasPlayerSpawn = true;
            playerSpawn = cell;
            return true;
        }

        public bool AddStair(Vector3Int lowerCell, GridDirection direction)
        {
            var stair = new StairLink(lowerCell, direction);
            if (!Contains(stair.LowerCell) || !Contains(stair.UpperCell) || stairs.Contains(stair)) return false;
            stairs.Add(stair);
            return true;
        }

        public bool RotateStair(Vector3Int lowerCell, int quarterTurns)
        {
            var index = stairs.FindIndex(stair => stair.LowerCell == lowerCell);
            if (index < 0) return false;
            var turns = ((quarterTurns % 4) + 4) % 4;
            if (turns == 0) return true;
            var direction = (GridDirection)(((int)stairs[index].UpDirection + turns) % 4);
            var rotated = new StairLink(lowerCell, direction);
            if (!Contains(rotated.UpperCell)) return false;
            for (var i = 0; i < stairs.Count; i++)
                if (i != index && stairs[i].Equals(rotated)) return false;
            stairs[index] = rotated;
            return true;
        }

        public bool RemoveAt(Vector3Int cell, LevelElementKind kind)
        {
            switch (kind)
            {
                case LevelElementKind.Solid: return solids.Remove(cell);
                case LevelElementKind.Target: return targets.Remove(cell);
                case LevelElementKind.Box: return boxes.Remove(cell);
                case LevelElementKind.Stair: return stairs.RemoveAll(s => s.LowerCell == cell || s.UpperCell == cell) > 0;
                case LevelElementKind.Player:
                    if (!hasPlayerSpawn || playerSpawn != cell) return false;
                    hasPlayerSpawn = false;
                    return true;
                case LevelElementKind.Any:
                    var changed = solids.Remove(cell) | targets.Remove(cell) | boxes.Remove(cell);
                    changed |= stairs.RemoveAll(s => s.LowerCell == cell || s.UpperCell == cell) > 0;
                    if (hasPlayerSpawn && playerSpawn == cell)
                    {
                        hasPlayerSpawn = false;
                        changed = true;
                    }
                    return changed;
                default: return false;
            }
        }

        public int CountElementsOutside(Vector3Int newSize)
        {
            bool Outside(Vector3Int cell)
            {
                return cell.x < 0 || cell.x >= newSize.x ||
                       cell.y < 0 || cell.y >= newSize.y ||
                       cell.z < 0 || cell.z >= newSize.z;
            }

            var count = solids.FindAll(Outside).Count + targets.FindAll(Outside).Count + boxes.FindAll(Outside).Count;
            count += stairs.FindAll(stair => Outside(stair.LowerCell) || Outside(stair.UpperCell)).Count;
            count += perspectiveLinks.FindAll(link => Outside(link.CellA) || Outside(link.CellB)).Count;
            if (hasPlayerSpawn && Outside(playerSpawn)) count++;
            return count;
        }

        public void Resize(Vector3Int newSize, bool trimOutside)
        {
            newSize.x = Mathf.Max(1, newSize.x);
            newSize.y = Mathf.Max(1, newSize.y);
            newSize.z = Mathf.Max(1, newSize.z);
            size = newSize;
            if (!trimOutside) return;

            solids.RemoveAll(cell => !Contains(cell));
            targets.RemoveAll(cell => !Contains(cell));
            boxes.RemoveAll(cell => !Contains(cell));
            stairs.RemoveAll(stair => !Contains(stair.LowerCell) || !Contains(stair.UpperCell));
            perspectiveLinks.RemoveAll(link => !Contains(link.CellA) || !Contains(link.CellB));
            if (hasPlayerSpawn && !Contains(playerSpawn)) hasPlayerSpawn = false;
        }

        public int CopyLayer(int sourceY, int destinationY, bool replaceDestination)
        {
            if (sourceY < 0 || sourceY >= size.y || destinationY < 0 || destinationY >= size.y ||
                sourceY == destinationY) return 0;

            var sourceSolids = solids.FindAll(cell => cell.y == sourceY);
            var sourceTargets = targets.FindAll(cell => cell.y == sourceY);
            var sourceBoxes = boxes.FindAll(cell => cell.y == sourceY);
            var sourceStairs = stairs.FindAll(stair => stair.LowerCell.y == sourceY);
            if (sourceSolids.Count + sourceTargets.Count + sourceBoxes.Count + sourceStairs.Count == 0) return 0;

            if (replaceDestination)
            {
                solids.RemoveAll(cell => cell.y == destinationY);
                targets.RemoveAll(cell => cell.y == destinationY);
                boxes.RemoveAll(cell => cell.y == destinationY);
                stairs.RemoveAll(stair => stair.LowerCell.y == destinationY || stair.UpperCell.y == destinationY);
            }

            var offset = Vector3Int.up * (destinationY - sourceY);
            var added = 0;
            foreach (var cell in sourceSolids) if (AddSolid(cell + offset)) added++;
            foreach (var cell in sourceTargets) if (AddTarget(cell + offset)) added++;
            foreach (var cell in sourceBoxes) if (AddBox(cell + offset)) added++;
            foreach (var stair in sourceStairs)
            {
                if (AddStair(stair.LowerCell + offset, stair.UpDirection)) added++;
            }

            return added;
        }

        private void OnValidate()
        {
            size.x = Mathf.Max(1, size.x);
            size.y = Mathf.Max(1, size.y);
            size.z = Mathf.Max(1, size.z);
            cellSize = Mathf.Max(0.1f, cellSize);
        }
    }

    public enum LevelElementKind
    {
        Any,
        Solid,
        Target,
        Box,
        Player,
        Stair
    }
}
