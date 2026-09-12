using System.Collections;
using System.Collections.Generic;
using PuzzleBox.Data;
using PuzzleBox.Rules;
using PuzzleBox.State;
using UnityEngine;

namespace PuzzleBox.View
{
    /// <summary>Shared by Scene painting, the lit preview, and the playable scene. Never changes rule data.</summary>
    [ExecuteAlways]
    public sealed partial class PuzzleBoxLevelView : MonoBehaviour
    {
        [SerializeField] private LevelDefinition level;
        [SerializeField] private bool rebuildOnEnable = true;
        [Header("Play motion")]
        [SerializeField, Min(.08f)] private float hopDuration = .28f;
        [SerializeField, Min(.08f)] private float pushDuration = .30f;
        [SerializeField, Min(.04f)] private float fallDurationPerCell = .12f;
        [SerializeField, Range(.05f, .5f)] private float hopHeight = .24f;
        private Transform generatedRoot;
        private Transform scenery;
        private Transform actor;
        private PuzzleBoxMotionEffects motionEffects;
        private WorldState state;
        private GridDirection playerFacing = GridDirection.South;
        private readonly Dictionary<int, Transform> boxViews = new Dictionary<int, Transform>();
        private readonly List<KeyValuePair<int, GameObject>> layers = new List<KeyValuePair<int, GameObject>>();

        public LevelDefinition Level => level;
        public GridDirection PlayerFacing => playerFacing;
        public event System.Action CatLanded;
        public event System.Action<float> BoxesLanded;

        public void SetLevel(LevelDefinition value)
        {
            level = value;
            state = null;
            Rebuild();
        }

        public void ShowState(WorldState value)
        {
            state = value;
            if (generatedRoot == null) Rebuild();
            if (state == null) return;
            SetViewQuarter(state.ViewQuarter);
            // Static terrain and shared materials survive every move and undo.
            foreach (var pair in boxViews)
            {
                bool exists = state.Boxes.TryGetValue(pair.Key, out var cell);
                pair.Value.gameObject.SetActive(exists);
                if (exists) pair.Value.localPosition = (Vector3)cell * level.CellSize;
            }
            if (actor != null)
            {
                actor.localPosition = PlayerVisualPosition(state.PlayerPosition);
                ApplyPlayerFacing();
            }
        }

        public void SetPlayerFacing(GridDirection direction)
        {
            playerFacing = direction;
            ApplyPlayerFacing();
        }

        public IEnumerator AnimateToState(WorldState value, MoveResult result)
        {
            if (!Application.isPlaying || value == null || actor == null || !result.Succeeded)
            {
                ShowState(value);
                yield break;
            }

            state = value;
            // Do not leave a stale road floating over a sliding/falling box. The settled
            // network is shown by ShowState after the existing movement animation finishes.
            foreach (var marker in linkMarkers) marker.SetActive(false);
            var actorStart = actor.localPosition;
            var actorEnd = PlayerVisualPosition(value.PlayerPosition);
            var actorScale = actor.localScale;
            var travel = actorEnd - actorStart;
            var movingIds = new HashSet<int>(result.MovedBoxIds);
            var boxes = new List<BoxMotion>();
            var horizontalTravel = new Vector3(travel.x, 0f, travel.z);
            if (result.UsedPerspectiveLink) horizontalTravel = (Vector3)result.TravelDirection.ToOffset() * level.CellSize;

            foreach (var pair in boxViews)
            {
                if (!movingIds.Contains(pair.Key))
                {
                    var exists = value.Boxes.TryGetValue(pair.Key, out var stationaryCell);
                    pair.Value.gameObject.SetActive(exists);
                    if (exists) pair.Value.localPosition = (Vector3)stationaryCell * level.CellSize;
                    continue;
                }

                var motion = new BoxMotion { View = pair.Value, Start = pair.Value.localPosition };
                motion.PushEnd = motion.Start + (Vector3)result.BoxTravelOffset * level.CellSize;
                motion.ExistsAtEnd = value.Boxes.TryGetValue(pair.Key, out var endCell);
                motion.End = motion.ExistsAtEnd
                    ? (Vector3)endCell * level.CellSize
                    : motion.PushEnd + Vector3.down * level.CellSize;
                // A depth transfer can change real Y in either direction while its
                // projection falls straight down. Time the fall by SCREEN height.
                motion.DropCells = PerspectiveGravity.DropDistance(motion.PushEnd, motion.End, value.ViewQuarter) / level.CellSize;
                pair.Value.gameObject.SetActive(true);
                boxes.Add(motion);
            }

            var directionWorld = generatedRoot.TransformDirection(horizontalTravel.normalized);
            motionEffects?.PlayHopPuff(PlayerFeetWorld(actorStart), directionWorld, level.CellSize, false);
            if (result.Pushed && boxes.Count > 0)
            {
                var lowest = boxes[0];
                foreach (var item in boxes)
                    if (item.Start.y < lowest.Start.y) lowest = item;
                var pushOrigin = lowest.Start - horizontalTravel.normalized * (.43f * level.CellSize) +
                                 Vector3.down * (.38f * level.CellSize);
                motionEffects?.PlayPushPuff(generatedRoot.TransformPoint(pushOrigin), directionWorld, level.CellSize);
            }

            var horizontalSeconds = result.Pushed ? pushDuration : hopDuration;
            var maxDropCells = 0f;
            foreach (var item in boxes) maxDropCells = Mathf.Max(maxDropCells, item.DropCells);
            var fallSeconds = result.Pushed && maxDropCells > 0f
                ? Mathf.Max(.10f, maxDropCells * fallDurationPerCell)
                : 0f;
            var totalSeconds = horizontalSeconds + fallSeconds;
            var elapsed = 0f;
            var catLanded = false;

            while (elapsed < totalSeconds)
            {
                elapsed += Time.deltaTime;
                var actorT = Mathf.Clamp01(elapsed / horizontalSeconds);
                actor.localPosition = EvaluateHop(actorStart, actorEnd, actorT,
                    (result.Pushed ? hopHeight * .36f : hopHeight) * level.CellSize);
                if (result.PlayerUsedPerspectiveLink)
                    actor.localPosition = EvaluatePerspectiveHop(actorStart, actorEnd, horizontalTravel, actorT,
                        (result.Pushed ? hopHeight * .36f : hopHeight) * level.CellSize);
                if (result.Pushed)
                {
                    var brace = Mathf.Sin(actorT * Mathf.PI);
                    actor.localScale = Vector3.Scale(actorScale,
                        new Vector3(1f + brace * .04f, 1f - brace * .08f, 1f + brace * .04f));
                }

                foreach (var item in boxes)
                {
                    if (elapsed <= horizontalSeconds || fallSeconds <= 0f)
                    {
                        var pushT = EaseOutCubic(Mathf.Clamp01(elapsed / horizontalSeconds));
                        var pushPosition = result.BoxesUsedPerspectiveLink
                            ? EvaluatePerspectiveSlide(item.Start, item.PushEnd, horizontalTravel, pushT)
                            : Vector3.LerpUnclamped(item.Start, item.PushEnd, pushT);
                        item.View.localPosition = pushPosition +
                                                  Vector3.up * (Mathf.Sin(pushT * Mathf.PI) * .035f * level.CellSize);
                    }
                    else
                    {
                        var fallT = Mathf.Clamp01((elapsed - horizontalSeconds) / fallSeconds);
                        var position = Vector3.LerpUnclamped(item.PushEnd, item.End, fallT * fallT);
                        if (item.ExistsAtEnd)
                            position += Vector3.up * Mathf.Sin(fallT * Mathf.PI) * .025f * level.CellSize;
                        item.View.localPosition = position;
                    }
                }
                if (!catLanded && elapsed >= horizontalSeconds)
                {
                    catLanded = true;
                    CatLanded?.Invoke();
                    motionEffects?.PlayHopPuff(PlayerFeetWorld(actorEnd), directionWorld, level.CellSize, true);
                }
                yield return null;
            }

            actor.localScale = actorScale;
            ShowState(value);
            if (result.Pushed && maxDropCells > 0f && boxes.Count > 0)
            {
                var landed = false;
                foreach (var item in boxes)
                {
                    if (item.ExistsAtEnd && item.DropCells > .001f) landed = true;
                    if (item.ExistsAtEnd && Mathf.Approximately(item.End.y, boxes[0].End.y))
                        motionEffects?.PlayPushPuff(generatedRoot.TransformPoint(item.End + Vector3.down * .43f * level.CellSize),
                            -directionWorld, level.CellSize * .72f);
                }
                if (landed) BoxesLanded?.Invoke(maxDropCells);
            }
        }

        public static Vector3 EvaluateHop(Vector3 start, Vector3 end, float normalizedTime, float height)
        {
            var t = Mathf.Clamp01(normalizedTime);
            var eased = t * t * (3f - 2f * t);
            return Vector3.LerpUnclamped(start, end, eased) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * height);
        }

        private void OnEnable() { if (rebuildOnEnable) Rebuild(); }
        private void OnDisable() { ClearGenerated(); }

        [ContextMenu("Rebuild Park View")]
        public void Rebuild()
        {
            ClearGenerated();
            if (level == null || level.ArtTheme == null) return;
            var theme = level.ArtTheme;
            generatedRoot = new GameObject("Generated Park (not saved)").transform;
            generatedRoot.SetParent(transform, false);
            motionEffects = generatedRoot.gameObject.AddComponent<PuzzleBoxMotionEffects>();
            motionEffects.Configure(theme.hopEffectMaterial, theme.pushEffectMaterial);
            foreach (var cell in level.Solids)
                Tile(cell.y == 0 ? theme.groundTile : theme.platformTile, "Ground", cell);
            var solidCells = new HashSet<Vector3Int>(level.Solids);
            foreach (var cell in level.Targets)
            {
                var marker = Tile(theme.target, "Target", cell);
                // Upper stack goals are outlines, not floating patches of earth.
                if (marker != null && !solidCells.Contains(cell + Vector3Int.down))
                {
                    var soil = marker.Find("Delivery soil");
                    if (soil != null) soil.gameObject.SetActive(false);
                }
            }
            foreach (var stair in level.Stairs)
            {
                var model = Tile(theme.stair, "Stair", stair.LowerCell);
                if (model != null) model.localRotation = Quaternion.LookRotation((Vector3)stair.UpDirection.ToOffset());
            }
            for (int i = 0; i < level.Boxes.Count; i++)
            {
                var model = Tile(theme.box, "Box " + i, level.Boxes[i]);
                if (model != null) boxViews.Add(i, model);
            }
            if (level.HasPlayerSpawn)
            {
                actor = Tile(theme.player, "Player", level.PlayerSpawn);
                if (actor != null)
                {
                    actor.localPosition = PlayerVisualPosition(level.PlayerSpawn);
                    ApplyPlayerFacing();
                }
            }
            Decorate(theme);
            if (theme.grassTuft != null)
            {
                foreach (var cell in level.Solids)
                {
                    if (cell.y != 0) continue;
                    foreach (var direction in new[] { Vector3Int.left, Vector3Int.right, Vector3Int.forward, Vector3Int.back })
                    {
                        if (solidCells.Contains(cell + direction)) continue;
                        var p = (Vector3)cell + (Vector3)direction * .5f + Vector3.up * .49f;
                        Place(theme.grassTuft, scenery, "Grass edge", p, cell.x * 31 + cell.z * 47);
                    }
                }
            }
            MarkTransient(generatedRoot.gameObject);
            BuildLinkMarkers();
            if (state != null) ShowState(state);
        }

        private Transform Tile(GameObject prefab, string label, Vector3Int cell)
        {
            var model = Place(prefab, generatedRoot, label, cell);
            if (model != null) layers.Add(new KeyValuePair<int, GameObject>(cell.y, model.gameObject));
            return model;
        }

        private Vector3 PlayerVisualPosition(Vector3Int cell)
        {
            // The low stair's center tread is halfway up; this is only a visual foot offset.
            // Logical height, support, box pushing, and undo still use the unmodified grid cell.
            foreach (var stair in level.Stairs)
                if (stair.LowerCell == cell) return ((Vector3)cell + Vector3.up * .5f) * level.CellSize;
            return (Vector3)cell * level.CellSize;
        }

        private void ApplyPlayerFacing()
        {
            if (actor == null) return;
            // The generated cat's face is modelled toward local -Z.
            actor.localRotation = Quaternion.LookRotation((Vector3)playerFacing.ToOffset(), Vector3.up) *
                                  Quaternion.Euler(0f, 180f, 0f);
        }

        private Vector3 PlayerFeetWorld(Vector3 localPosition)
        {
            return generatedRoot.TransformPoint(localPosition + Vector3.down * (.46f * level.CellSize));
        }

        private static float EaseOutCubic(float value)
        {
            var inverse = 1f - value;
            return 1f - inverse * inverse * inverse;
        }

        private Transform Place(GameObject prefab, Transform parent, string label, Vector3 position, float angle = 0f)
        {
            if (prefab == null) return null;
            var model = Instantiate(prefab, parent, false).transform;
            model.name = label;
            model.localPosition = position * level.CellSize;
            model.localRotation = Quaternion.Euler(0, angle, 0);
            model.localScale = Vector3.one * level.CellSize;
            // Art must never introduce physical blockers to the grid-based rules.
            foreach (var collider in model.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
                Dispose(collider);
            }
            return model;
        }

        private void Decorate(ParkTheme theme)
        {
            scenery = new GameObject("Scenery (visual only)").transform;
            scenery.SetParent(generatedRoot, false);
            if (theme.meadowMaterial != null)
            {
                var meadow = GameObject.CreatePrimitive(PrimitiveType.Cube);
                meadow.name = "Meadow Backdrop";
                meadow.transform.SetParent(scenery, false);
                meadow.transform.localPosition = new Vector3((level.Size.x - 1) * .5f, .40f, (level.Size.z - 1) * .5f) * level.CellSize;
                meadow.transform.localScale = new Vector3(160, .14f, 160) * level.CellSize;
                meadow.GetComponent<Renderer>().sharedMaterial = theme.meadowMaterial;
                meadow.GetComponent<Collider>().enabled = false;
                Dispose(meadow.GetComponent<Collider>());
            }
            float far = level.Size.z + .1f;
            for (int x = 1; x < level.Size.x - 1; x += 2)
                Place(theme.hedge, scenery, "Border Hedge", new Vector3(x, .48f, far));
            Place(theme.bench, scenery, "Bench", new Vector3(-1.9f, .48f, 1.9f), 90);
            Place(theme.lamp, scenery, "Lamp", new Vector3(-1.9f, .48f, 3.3f));
            Place(theme.bush, scenery, "Bush", new Vector3(-1.1f, .48f, 4.6f));
            Place(theme.bush, scenery, "Bush", new Vector3(level.Size.x + .1f, .48f, far - 1.9f));
            for (int i = 0; i < 7; i++)
            {
                var p = i < 4 ? new Vector3(i * 1.7f - .4f, .48f, -1.25f - (i % 2) * .4f) :
                    new Vector3(level.Size.x + .8f + (i % 2) * .25f, .48f, (i - 4) * 1.65f);
                Place(theme.flowers, scenery, "Daisies", p, i * 73f);
                if (i % 3 == 0) Place(theme.rock, scenery, "Stone", p + new Vector3(.6f, 0, .1f), i * 37f);
            }
        }

        /// <param name="mode">-1: hidden; 0: current layer; 1: current and below; 2: all.</param>
        public void SetVisibleLayers(int current, int mode)
        {
            foreach (var pair in layers)
                pair.Value.SetActive(mode >= 0 && (mode == 2 || (mode == 0 ? pair.Key == current : pair.Key <= current)));
            if (scenery != null) scenery.gameObject.SetActive(mode == 2);
            if (linkMarkersRoot != null) linkMarkersRoot.gameObject.SetActive(mode == 2);
        }

        private static void MarkTransient(GameObject root)
        {
            root.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            foreach (Transform child in root.transform) MarkTransient(child.gameObject);
        }

        private void ClearGenerated()
        {
            if (generatedRoot != null)
            {
                generatedRoot.gameObject.SetActive(false);
                Dispose(generatedRoot.gameObject);
            }
            generatedRoot = scenery = actor = null;
            motionEffects = null;
            boxViews.Clear();
            layers.Clear();
            linkMarkers.Clear();
            linkMarkersRoot = null;
            previewNetwork = null;
            shownLinks = null;
        }

        private static void Dispose(Object target)
        {
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private sealed class BoxMotion
        {
            public Transform View;
            public Vector3 Start;
            public Vector3 PushEnd;
            public Vector3 End;
            public float DropCells;
            public bool ExistsAtEnd;
        }
    }
}
