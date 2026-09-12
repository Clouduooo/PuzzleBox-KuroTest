using System.Collections.Generic;
using System.Linq;
using PuzzleBox.Data;
using PuzzleBox.Rules;
using PuzzleBox.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuzzleBox.Editor
{
    public sealed partial class PuzzleBoxLevelEditorWindow : EditorWindow
    {
        private enum PaintTool
        {
            Solid,
            Target,
            Box,
            Player,
            Stair,
            Picker,
            Erase
        }

        private enum PaintShape
        {
            Brush,
            Rectangle,
            Volume
        }

        private enum PlacementMode
        {
            HeightPlane,
            SolidSurface
        }

        private enum LayerDisplay
        {
            CurrentOnly,
            CurrentAndBelow,
            All
        }

        private static readonly string[] ToolNames =
            { "Solid", "Target", "Box", "Player", "Stair", "Picker / Select", "Erase" };
        private static readonly Color SolidEdgeColor = new Color(0.56f, 0.84f, 1f, 1f);
        private static readonly Color BoxPreviewColor = new Color(1f, 0.55f, 0.12f, 1f);

        [SerializeField] private LevelDefinition level;
        [SerializeField] private bool showArt = true;
        [SerializeField] private bool showGrid = true;
        private PuzzleBoxLevelView sceneArt;
        private bool ownsSceneArt;
        private string artContentHash;
        private readonly List<PuzzleBoxLevelView> mutedSceneViews = new List<PuzzleBoxLevelView>();
        private Material opaquePreviewMaterial;
        private Material transparentPreviewMaterial;
        private Mesh opaquePreviewCube;
        private Mesh opaquePreviewSlope;
        private Vector3Int? selectedCell;
        private LevelElementKind selectedKind = LevelElementKind.Any;
        private PaintTool paintTool = PaintTool.Solid;
        private PaintShape paintShape;
        private PlacementMode placementMode;
        private LevelElementKind eraseKind = LevelElementKind.Any;
        private GridDirection stairDirection = GridDirection.North;
        private int boxStackHeight = 1;
        private int volumeHeight = 1;
        private int activeLayer;
        private LayerDisplay layerDisplay = LayerDisplay.All;
        private int copyDestinationLayer = 1;
        private bool replaceCopiedLayer;
        private Vector3Int resizeCandidate = new Vector3Int(8, 6, 8);
        private bool enableScenePainting = true;
        private Vector2 scroll;
        private List<ValidationIssue> issues = new List<ValidationIssue>();
        private Vector3Int? dragStart;
        private Vector3Int? dragEnd;
        private Vector3Int? lastBrushCell;
        private int undoGroup = -1;

        [MenuItem("Tools/Puzzle Box/Level Editor")]
        private static void Open()
        {
            GetWindow<PuzzleBoxLevelEditorWindow>("Puzzle Box Level");
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Selection.selectionChanged += OnSelectionChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            TryUseSelectedLevel();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Selection.selectionChanged -= OnSelectionChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            ClearSceneArt();
            DestroyPreviewRenderingResources();
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) ClearSceneArt();
        }

        private void OnUndoRedo()
        {
            UpdateUnsavedState();
            RefreshValidation();
            Repaint();
            SceneView.RepaintAll();
        }

        private void OnSelectionChanged()
        {
            TryUseSelectedLevel();
            Repaint();
        }

        private void TryUseSelectedLevel()
        {
            if (Selection.activeObject is LevelDefinition selected && selected != level)
            {
                if (!TrySwitchLevel(selected)) Selection.activeObject = level;
            }
        }

        private void OnGUI()
        {
            DrawLevelToolbar();

            var newLevel = (LevelDefinition)EditorGUILayout.ObjectField("Current Level", level,
                typeof(LevelDefinition), false);
            if (newLevel != level)
            {
                TrySwitchLevel(newLevel);
            }

            if (level == null)
            {
                EditorGUILayout.HelpBox("Create or select a Puzzle Box level asset to begin.", MessageType.Info);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawPerspectivePanel();
            DrawLevelProperties();
            EditorGUILayout.Space();
            DrawPaintSettings();
            EditorGUILayout.Space();
            DrawValidation();
            EditorGUILayout.EndScrollView();
        }

        private void DrawLevelToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("New", EditorStyles.toolbarButton)) CreateLevel();
                using (new EditorGUI.DisabledScope(level == null))
                {
                    if (GUILayout.Button("Duplicate", EditorStyles.toolbarButton)) DuplicateLevel();
                    if (GUILayout.Button("Save", EditorStyles.toolbarButton)) SaveLevel();
                    if (GUILayout.Button("Frame", EditorStyles.toolbarButton)) FrameLevel();
                }
                GUILayout.FlexibleSpace();
            }
        }

        private void DrawLevelProperties()
        {
            EditorGUILayout.LabelField("Level", EditorStyles.boldLabel);
            var serializedLevel = new SerializedObject(level);
            serializedLevel.Update();
            EditorGUILayout.PropertyField(serializedLevel.FindProperty("displayName"));
            EditorGUILayout.PropertyField(serializedLevel.FindProperty("designNotes"));
            EditorGUILayout.PropertyField(serializedLevel.FindProperty("cellSize"));
            EditorGUILayout.PropertyField(serializedLevel.FindProperty("artTheme"), new GUIContent("美术主题（空 = 默认公园）"));
            if (serializedLevel.ApplyModifiedProperties())
            {
                UpdateUnsavedState();
                ClampLayer();
                RefreshValidation();
                SceneView.RepaintAll();
            }
            EditorGUILayout.LabelField("Level ID", level.LevelId);

            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField("Grid Size", level.Size.ToString());
            resizeCandidate = EditorGUILayout.Vector3IntField("New Size", resizeCandidate);
            resizeCandidate.x = Mathf.Max(1, resizeCandidate.x);
            resizeCandidate.y = Mathf.Max(1, resizeCandidate.y);
            resizeCandidate.z = Mathf.Max(1, resizeCandidate.z);
            using (new EditorGUI.DisabledScope(resizeCandidate == level.Size))
            {
                if (GUILayout.Button("Apply Grid Size")) ApplyResize();
            }
        }

        private void DrawPaintSettings()
        {
            EditorGUILayout.LabelField("Scene Painting", EditorStyles.boldLabel);
            enableScenePainting = EditorGUILayout.Toggle("Enable Painting", enableScenePainting);
            paintTool = (PaintTool)EditorGUILayout.Popup("Tool", (int)paintTool, ToolNames);
            placementMode = (PlacementMode)EditorGUILayout.EnumPopup("Placement", placementMode);
            using (new EditorGUI.DisabledScope(paintTool == PaintTool.Player || paintTool == PaintTool.Stair ||
                                               paintTool == PaintTool.Picker))
                paintShape = (PaintShape)EditorGUILayout.EnumPopup("Shape", paintShape);
            if (paintTool == PaintTool.Player || paintTool == PaintTool.Stair || paintTool == PaintTool.Picker)
                paintShape = PaintShape.Brush;
            if (paintTool == PaintTool.Stair)
                stairDirection = (GridDirection)EditorGUILayout.EnumPopup("Stair Up Direction", stairDirection);
            if (paintTool == PaintTool.Box)
                boxStackHeight = EditorGUILayout.IntSlider("Stack Height", boxStackHeight, 1,
                    Mathf.Max(1, level.Size.y));
            if (paintShape == PaintShape.Volume)
                volumeHeight = EditorGUILayout.IntSlider("Volume Height", volumeHeight, 1,
                    Mathf.Max(1, level.Size.y));
            if (paintTool == PaintTool.Erase)
                eraseKind = (LevelElementKind)EditorGUILayout.EnumPopup("Erase", eraseKind);

            DrawSelectionControls();

            activeLayer = EditorGUILayout.IntSlider("Active Height (Y)", activeLayer, 0,
                Mathf.Max(0, level.Size.y - 1));
            layerDisplay = (LayerDisplay)EditorGUILayout.EnumPopup("Layer Display", layerDisplay);

            EditorGUI.BeginChangeCheck();
            showArt = EditorGUILayout.Toggle("模型预览", showArt);
            showGrid = EditorGUILayout.Toggle("显示编辑网格", showGrid);
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("打开光照美术预览")) PuzzleBoxLevelPreviewWindow.Open(level);
                if (GUILayout.Button("刷新模型", GUILayout.Width(78))) { artContentHash = null; SceneView.RepaintAll(); }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Top View")) SetSceneView(true);
                if (GUILayout.Button("Isometric View")) SetSceneView(false);
                if (GUILayout.Button("Frame Layer")) FrameLayer();
            }

            EditorGUILayout.Space(3f);
            copyDestinationLayer = EditorGUILayout.IntSlider("Copy To Y（目标层）", copyDestinationLayer, 0,
                Mathf.Max(0, level.Size.y - 1));
            replaceCopiedLayer = EditorGUILayout.Toggle("Replace Destination（先清空）", replaceCopiedLayer);
            using (new EditorGUI.DisabledScope(copyDestinationLayer == activeLayer))
            {
                if (GUILayout.Button("Copy Active Layer（复制当前层）")) CopyActiveLayer();
            }

            EditorGUILayout.HelpBox(
                "Picker / Select：点击格子会吸取该元素的画笔类型并记录选择；选中 Stair 后可旋转。\n" +
                "Copy To Y：指定目标高度；Copy Active Layer：把当前层的 Solid、Target、Box 和楼梯复制到目标层。" +
                "开启 Replace Destination 会先清理目标层内容。玩家出生点不会被复制。\n" +
                "Scene 左键绘制，Alt 观察；Solid Surface 会贴已有实体表面。Y=1 的 Box 由 Y=0 的 Solid 支撑。",
                MessageType.None);
        }

        private void DrawSelectionControls()
        {
            if (!selectedCell.HasValue) return;
            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField("Selected", $"{selectedKind}  {selectedCell.Value}");
            using (new EditorGUI.DisabledScope(selectedKind != LevelElementKind.Stair))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("↶ Rotate Left  [")) RotateSelectedStair(-1);
                if (GUILayout.Button("Rotate Right ]  ↷")) RotateSelectedStair(1);
            }
            if (selectedKind != LevelElementKind.Stair)
                EditorGUILayout.LabelField("当前元素没有方向；第一版旋转作用于 Stair。", EditorStyles.miniLabel);
        }

        private void DrawValidation()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
                if (GUILayout.Button("Refresh", GUILayout.Width(70))) RefreshValidation();
            }

            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("No validation issues found.", MessageType.Info);
                return;
            }

            foreach (var issue in issues)
            {
                var type = issue.Severity == ValidationSeverity.Error ? MessageType.Error : MessageType.Warning;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox(issue.Message, type);
                    if (issue.Cell.HasValue && GUILayout.Button("Go", GUILayout.Width(36), GUILayout.Height(38)))
                        FrameCell(issue.Cell.Value);
                }
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (level == null || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ClearSceneArt();
                return;
            }

            if (level.ArtTheme != null)
            {
                RefreshSceneArt();
                if (showArt) DrawStackLabels();
                else DrawLevel();
            }
            else
            {
                ClearSceneArt();
                DrawLevel();
            }
            DrawSelectionHighlight();
            if (showGrid) DrawGrid();

            var nativeNavigation = IsNativeSceneNavigation(Event.current);
            if (HandlePerspectiveScene(sceneView, nativeNavigation)) return;
            // Let Unity own orbit, pan, fly and the hand tool; neither painting nor road
            // pairing should consume those gestures or their keyboard input.
            if (nativeNavigation) return;

            if (HandleRotationShortcut()) return;

            var hasCell = TryGetMouseCell(Event.current.mousePosition, out var mouseCell);
            if (hasCell)
                DrawPaintPreview(mouseCell);

            if (!enableScenePainting || Event.current.alt) return;
            var controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (Event.current.type == EventType.Layout)
                HandleUtility.AddDefaultControl(controlId);

            HandlePaintInput(hasCell, mouseCell);
        }

        private void RefreshSceneArt()
        {
            if (sceneArt != null && sceneArt.Level != level) ClearSceneArt();
            // Reuse an existing scene view of this level, avoiding a second overlapping park.
            if (sceneArt == null)
            {
                sceneArt = FindObjectsOfType<PuzzleBoxLevelView>().FirstOrDefault(v =>
                    v.Level == level && !EditorSceneManager.IsPreviewScene(v.gameObject.scene));
                ownsSceneArt = sceneArt == null;
                if (ownsSceneArt)
                {
                    var root = new GameObject("Puzzle Box Editor Preview") { hideFlags = HideFlags.HideAndDontSave };
                    sceneArt = root.AddComponent<PuzzleBoxLevelView>();
                }
                artContentHash = null;
            }
            var hash = EditorJsonUtility.ToJson(level) + EditorJsonUtility.ToJson(level.ArtTheme);
            if (artContentHash != hash)
            {
                sceneArt.SetLevel(level);
                artContentHash = hash;
            }
            sceneArt.SetVisibleLayers(activeLayer, showArt ? (int)layerDisplay : -1);
            // Selecting a different asset must not draw two different levels on top of each other.
            foreach (var other in FindObjectsOfType<PuzzleBoxLevelView>())
            {
                if (other == sceneArt || EditorSceneManager.IsPreviewScene(other.gameObject.scene)) continue;
                if (!mutedSceneViews.Contains(other)) mutedSceneViews.Add(other);
                other.SetVisibleLayers(0, -1);
            }
        }

        private void ClearSceneArt()
        {
            foreach (var other in mutedSceneViews)
                if (other != null) other.SetVisibleLayers(0, 2);
            mutedSceneViews.Clear();
            if (sceneArt != null)
            {
                if (ownsSceneArt) DestroyImmediate(sceneArt.gameObject);
                else sceneArt.SetVisibleLayers(0, 2);
            }
            sceneArt = null;
            ownsSceneArt = false;
            artContentHash = null;
        }

        private void HandlePaintInput(bool hasCell, Vector3Int cell)
        {
            var current = Event.current;
            if (current.button != 0) return;

            if (current.type == EventType.MouseDown && hasCell)
            {
                dragStart = cell;
                dragEnd = cell;
                lastBrushCell = null;
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Paint Puzzle Box Level");
                Undo.RecordObject(level, "Paint Puzzle Box Level");
                if (paintShape == PaintShape.Brush)
                    ApplyBrushCell(cell);
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDrag && dragStart.HasValue && hasCell)
            {
                dragEnd = cell;
                if (paintShape == PaintShape.Brush)
                    ApplyBrushCell(cell);
                SceneView.RepaintAll();
                current.Use();
                return;
            }

            if (current.type == EventType.MouseUp && dragStart.HasValue)
            {
                if (hasCell) dragEnd = cell;
                if (paintShape != PaintShape.Brush && dragEnd.HasValue)
                    ApplyRegion(dragStart.Value, dragEnd.Value);
                FinishPaintGesture();
                current.Use();
                return;
            }

            if (current.type == EventType.MouseMove || current.type == EventType.MouseDrag)
                SceneView.RepaintAll();
        }

        private void ApplyBrushCell(Vector3Int cell)
        {
            if (lastBrushCell == cell) return;
            lastBrushCell = cell;
            ApplyPaintAt(cell);
        }

        private void ApplyRegion(Vector3Int first, Vector3Int second)
        {
            var minX = Mathf.Min(first.x, second.x);
            var maxX = Mathf.Max(first.x, second.x);
            var minZ = Mathf.Min(first.z, second.z);
            var maxZ = Mathf.Max(first.z, second.z);
            var startY = Mathf.Min(first.y, second.y);
            var endY = paintShape == PaintShape.Volume
                ? Mathf.Min(level.Size.y - 1, startY + volumeHeight - 1)
                : startY;

            for (var y = startY; y <= endY; y++)
            for (var x = minX; x <= maxX; x++)
            for (var z = minZ; z <= maxZ; z++)
                ApplyPaintAt(new Vector3Int(x, y, z), false);

            MarkLevelChanged();
        }

        private void FinishPaintGesture()
        {
            if (undoGroup >= 0)
                Undo.CollapseUndoOperations(undoGroup);
            dragStart = null;
            dragEnd = null;
            lastBrushCell = null;
            undoGroup = -1;
            RefreshValidation();
            Repaint();
            SceneView.RepaintAll();
        }

        private void DrawGrid()
        {
            var cellSize = level.CellSize;
            var y = PuzzleBoxScenePreviewStyle.LayerBottomY(activeLayer, cellSize);
            Handles.color = new Color(0.35f, 0.75f, 1f, 0.55f);
            for (var x = 0; x <= level.Size.x; x++)
            {
                var px = (x - 0.5f) * cellSize;
                Handles.DrawLine(new Vector3(px, y, -0.5f * cellSize),
                    new Vector3(px, y, (level.Size.z - 0.5f) * cellSize));
            }
            for (var z = 0; z <= level.Size.z; z++)
            {
                var pz = (z - 0.5f) * cellSize;
                Handles.DrawLine(new Vector3(-0.5f * cellSize, y, pz),
                    new Vector3((level.Size.x - 0.5f) * cellSize, y, pz));
            }
        }

        private void DrawLevel()
        {
            // Opaque active-layer geometry establishes depth first. Other visible layers
            // are blended afterwards, so they can be read without washing out the work layer.
            foreach (var cell in level.Solids.Where(cell => cell.y == activeLayer))
                DrawCube(cell, PuzzleBoxScenePreviewStyle.SolidColorForHeight(cell.y), 0.92f, SolidEdgeColor);
            foreach (var cell in level.Boxes.Where(cell => cell.y == activeLayer))
                DrawCube(cell, BoxPreviewColor, 0.72f, null);
            foreach (var stair in level.Stairs.Where(stair => IsStairOnActiveLayer(stair) &&
                         (IsVisible(stair.LowerCell) || IsVisible(stair.UpperCell))))
                DrawStairSlope(stair.LowerCell, stair.UpDirection, new Color(0.20f, 0.88f, 0.45f));

            foreach (var cell in level.Solids.Where(cell => cell.y != activeLayer))
                DrawCube(cell, PuzzleBoxScenePreviewStyle.SolidColorForHeight(cell.y), 0.92f, SolidEdgeColor);
            foreach (var cell in level.Boxes.Where(cell => cell.y != activeLayer))
                DrawCube(cell, BoxPreviewColor, 0.72f, null);
            foreach (var stair in level.Stairs.Where(stair => !IsStairOnActiveLayer(stair) &&
                         (IsVisible(stair.LowerCell) || IsVisible(stair.UpperCell))))
                DrawStairSlope(stair.LowerCell, stair.UpDirection, new Color(0.20f, 0.88f, 0.45f));

            foreach (var cell in level.Targets) DrawTarget(cell);
            DrawStackLabels();

            if (level.HasPlayerSpawn && IsVisible(level.PlayerSpawn))
            {
                Handles.color = LayerColor(level.PlayerSpawn, new Color(0.15f, 0.85f, 1f, 1f));
                Handles.SphereHandleCap(0, ToWorld(level.PlayerSpawn), Quaternion.identity,
                    level.CellSize * 0.65f, EventType.Repaint);
            }

        }

        private bool IsStairOnActiveLayer(StairLink stair)
        {
            return stair.LowerCell.y == activeLayer || stair.UpperCell.y == activeLayer;
        }

        private void DrawCube(Vector3Int cell, Color color, float scale, Color? brightEdgeColor)
        {
            if (!IsVisible(cell)) return;
            EnsurePreviewRenderingResources();
            var center = ToWorld(cell);
            var size = level.CellSize * scale;
            var active = cell.y == activeLayer;
            color = PuzzleBoxScenePreviewStyle.WithLayerOpacity(color, active);
            DrawPreviewMesh(opaquePreviewCube, center, Quaternion.identity, Vector3.one * size, color, active);
            if (brightEdgeColor.HasValue)
            {
                var edge = PuzzleBoxScenePreviewStyle.WithLayerOpacity(brightEdgeColor.Value, active);
                DrawCubeEdges(center, size, edge, active ? 2.2f : 1.5f);
            }
            else if (cell.y != activeLayer)
            {
                Handles.color = new Color(color.r * .75f, color.g * .75f, color.b * .75f, color.a);
                Handles.DrawWireCube(center, Vector3.one * size);
            }
        }

        private static void DrawCubeEdges(Vector3 center, float size, Color color, float width)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            var segments = PuzzleBoxScenePreviewStyle.CreateCubeEdgeSegments(center, size);
            var oldZTest = Handles.zTest;
            Handles.zTest = CompareFunction.LessEqual;
            Handles.color = color;
            for (var i = 0; i < segments.Length; i += 2)
                Handles.DrawAAPolyLine(width, segments[i], segments[i + 1]);
            Handles.zTest = oldZTest;
        }

        private void DrawPreviewMesh(Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale, Color color,
            bool opaque)
        {
            EnsurePreviewRenderingResources();
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            var material = opaque ? opaquePreviewMaterial : transparentPreviewMaterial;
            material.SetColor("_Color", color);
            if (!material.SetPass(0)) return;
            Graphics.DrawMeshNow(mesh, Matrix4x4.TRS(position, rotation, scale));
        }

        private void DrawStairSlope(Vector3Int lowerCell, GridDirection direction, Color color)
        {
            EnsurePreviewRenderingResources();
            var rotation = Quaternion.LookRotation((Vector3)direction.ToOffset(), Vector3.up);
            var matrix = Matrix4x4.TRS(ToWorld(lowerCell), rotation, Vector3.one * level.CellSize);
            var active = lowerCell.y == activeLayer || lowerCell.y + 1 == activeLayer;
            color = PuzzleBoxScenePreviewStyle.WithLayerOpacity(color, active);
            DrawPreviewMesh(opaquePreviewSlope, ToWorld(lowerCell), rotation,
                Vector3.one * level.CellSize, color, active);
            DrawStairSlopeOutline(matrix, color);
        }

        private static void DrawStairSlopeOutline(Matrix4x4 matrix, Color color)
        {
            var corners = new[]
            {
                matrix.MultiplyPoint3x4(new Vector3(-.4f,-.48f,-.48f)),
                matrix.MultiplyPoint3x4(new Vector3(.4f,-.48f,-.48f)),
                matrix.MultiplyPoint3x4(new Vector3(.4f,.48f,.48f)),
                matrix.MultiplyPoint3x4(new Vector3(-.4f,.48f,.48f)),
                matrix.MultiplyPoint3x4(new Vector3(-.4f,-.48f,-.48f))
            };
            var oldZTest = Handles.zTest;
            Handles.zTest = CompareFunction.LessEqual;
            Handles.color = color;
            Handles.DrawAAPolyLine(4f, corners);
            Handles.zTest = oldZTest;
        }

        private void EnsurePreviewRenderingResources()
        {
            if (opaquePreviewMaterial == null)
            {
                var shader = Shader.Find("Hidden/Puzzle Box/Scene Preview Lit") ?? Shader.Find("Hidden/Internal-Colored");
                opaquePreviewMaterial = new Material(shader)
                {
                    name = "Puzzle Box Opaque Scene Preview",
                    hideFlags = HideFlags.HideAndDontSave
                };
                PuzzleBoxScenePreviewStyle.ConfigureOpaqueMaterial(opaquePreviewMaterial);
            }
            if (transparentPreviewMaterial == null)
            {
                var shader = Shader.Find("Hidden/Puzzle Box/Scene Preview Lit") ?? Shader.Find("Hidden/Internal-Colored");
                transparentPreviewMaterial = new Material(shader)
                {
                    name = "Puzzle Box Transparent Scene Preview",
                    hideFlags = HideFlags.HideAndDontSave
                };
                PuzzleBoxScenePreviewStyle.ConfigureTransparentMaterial(transparentPreviewMaterial);
            }
            if (opaquePreviewCube == null)
            {
                opaquePreviewCube = PuzzleBoxScenePreviewStyle.CreateCubeMesh();
                opaquePreviewCube.hideFlags = HideFlags.HideAndDontSave;
            }
            if (opaquePreviewSlope == null)
            {
                opaquePreviewSlope = PuzzleBoxScenePreviewStyle.CreateStairSlopeMesh();
                opaquePreviewSlope.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        private void DestroyPreviewRenderingResources()
        {
            if (opaquePreviewMaterial != null) DestroyImmediate(opaquePreviewMaterial);
            if (transparentPreviewMaterial != null) DestroyImmediate(transparentPreviewMaterial);
            if (opaquePreviewCube != null) DestroyImmediate(opaquePreviewCube);
            if (opaquePreviewSlope != null) DestroyImmediate(opaquePreviewSlope);
            opaquePreviewMaterial = null;
            transparentPreviewMaterial = null;
            opaquePreviewCube = null;
            opaquePreviewSlope = null;
        }

        private void DrawSelectionHighlight()
        {
            if (!selectedCell.HasValue || !IsVisible(selectedCell.Value)) return;
            if (selectedKind == LevelElementKind.Stair)
            {
                foreach (var stair in level.Stairs)
                {
                    if (stair.LowerCell != selectedCell.Value) continue;
                    var rotation = Quaternion.LookRotation((Vector3)stair.UpDirection.ToOffset(), Vector3.up);
                    var matrix = Matrix4x4.TRS(ToWorld(stair.LowerCell), rotation, Vector3.one * level.CellSize);
                    DrawStairSlopeOutline(matrix, new Color(1f, .82f, .12f));
                    break;
                }
                return;
            }
            Handles.color = new Color(1f, .82f, .12f, 1f);
            Handles.DrawWireCube(ToWorld(selectedCell.Value), Vector3.one * level.CellSize * 1.04f);
        }

        private void DrawTarget(Vector3Int cell)
        {
            if (!IsVisible(cell)) return;
            Handles.color = LayerColor(cell, Color.yellow);
            Handles.DrawWireDisc(ToWorld(cell), Vector3.up, level.CellSize * 0.32f);
            Handles.DrawWireDisc(ToWorld(cell), Vector3.up, level.CellSize * 0.18f);
        }

        private void DrawStackLabels()
        {
            var boxes = new HashSet<Vector3Int>(level.Boxes);
            foreach (var bottom in boxes)
            {
                if (boxes.Contains(bottom + Vector3Int.down)) continue;
                var height = 1;
                while (boxes.Contains(bottom + Vector3Int.up * height)) height++;
                if (height <= 1 || !IsVisible(bottom)) continue;
                Handles.color = LayerColor(bottom, Color.white);
                Handles.Label(ToWorld(bottom + Vector3Int.up * (height - 1)) + Vector3.up * level.CellSize * 0.48f,
                    $"Stack x{height}");
            }
        }

        private bool IsVisible(Vector3Int cell)
        {
            switch (layerDisplay)
            {
                case LayerDisplay.CurrentOnly: return cell.y == activeLayer;
                case LayerDisplay.CurrentAndBelow: return cell.y <= activeLayer;
                default: return true;
            }
        }

        private Color LayerColor(Vector3Int cell, Color color)
        {
            return PuzzleBoxScenePreviewStyle.WithLayerOpacity(color, cell.y == activeLayer);
        }

        private Vector3 ToWorld(Vector3Int cell)
        {
            return (Vector3)cell * level.CellSize;
        }

        private bool TryGetMouseCell(Vector2 mousePosition, out Vector3Int cell)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            if (placementMode == PlacementMode.SolidSurface && TryGetSolidSurfaceCell(ray, out cell))
                return level.Contains(cell);

            var plane = new Plane(Vector3.up,
                new Vector3(0f, PuzzleBoxScenePreviewStyle.LayerBottomY(activeLayer, level.CellSize), 0f));
            if (!plane.Raycast(ray, out var distance))
            {
                cell = default;
                return false;
            }

            var point = ray.GetPoint(distance) / level.CellSize;
            cell = new Vector3Int(Mathf.RoundToInt(point.x), activeLayer, Mathf.RoundToInt(point.z));
            return level.Contains(cell);
        }

        private bool TryGetSolidSurfaceCell(Ray ray, out Vector3Int cell)
        {
            var bestDistance = float.PositiveInfinity;
            var hitCell = default(Vector3Int);
            var hitPoint = default(Vector3);
            var found = false;
            foreach (var solid in level.Solids)
            {
                if (!IsVisible(solid)) continue;
                var bounds = new Bounds(ToWorld(solid), Vector3.one * level.CellSize);
                if (!bounds.IntersectRay(ray, out var distance) || distance >= bestDistance) continue;
                bestDistance = distance;
                hitCell = solid;
                hitPoint = ray.GetPoint(distance);
                found = true;
            }

            if (!found)
            {
                cell = default;
                return false;
            }

            if (paintTool == PaintTool.Erase)
            {
                cell = hitCell;
                return true;
            }

            var delta = hitPoint - ToWorld(hitCell);
            var normal = DominantAxis(delta);
            cell = hitCell + normal;
            return true;
        }

        private static Vector3Int DominantAxis(Vector3 value)
        {
            var x = Mathf.Abs(value.x);
            var y = Mathf.Abs(value.y);
            var z = Mathf.Abs(value.z);
            if (x >= y && x >= z) return new Vector3Int(value.x >= 0f ? 1 : -1, 0, 0);
            if (y >= x && y >= z) return new Vector3Int(0, value.y >= 0f ? 1 : -1, 0);
            return new Vector3Int(0, 0, value.z >= 0f ? 1 : -1);
        }

        private bool ApplyPaintAt(Vector3Int cell, bool markChanged = true)
        {
            var changed = false;
            switch (paintTool)
            {
                case PaintTool.Solid: changed = level.AddSolid(cell); break;
                case PaintTool.Target: changed = level.AddTarget(cell); break;
                case PaintTool.Box:
                    var stackHeight = paintShape == PaintShape.Volume ? 1 : boxStackHeight;
                    for (var y = cell.y; y < Mathf.Min(level.Size.y, cell.y + stackHeight); y++)
                        changed |= level.AddBox(new Vector3Int(cell.x, y, cell.z));
                    break;
                case PaintTool.Player: changed = level.SetPlayer(cell); break;
                case PaintTool.Stair: changed = level.AddStair(cell, stairDirection); break;
                case PaintTool.Picker: PickElementAt(cell); return false;
                case PaintTool.Erase: changed = level.RemoveAt(cell, eraseKind); break;
            }

            if (!changed) return false;
            if (markChanged) MarkLevelChanged();
            return true;
        }

        private void MarkLevelChanged()
        {
            EditorUtility.SetDirty(level);
            UpdateUnsavedState();
            RefreshValidation();
            Repaint();
            SceneView.RepaintAll();
        }

        private void DrawPaintPreview(Vector3Int mouseCell)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (paintTool == PaintTool.Stair)
            {
                DrawStairSlope(mouseCell, stairDirection, new Color(.16f, 1f, .66f));
                return;
            }
            var first = dragStart ?? mouseCell;
            var second = dragEnd ?? mouseCell;
            if (dragStart.HasValue && paintShape != PaintShape.Brush)
                second = mouseCell;

            var minX = paintShape == PaintShape.Brush ? mouseCell.x : Mathf.Min(first.x, second.x);
            var maxX = paintShape == PaintShape.Brush ? mouseCell.x : Mathf.Max(first.x, second.x);
            var minZ = paintShape == PaintShape.Brush ? mouseCell.z : Mathf.Min(first.z, second.z);
            var maxZ = paintShape == PaintShape.Brush ? mouseCell.z : Mathf.Max(first.z, second.z);
            var startY = paintShape == PaintShape.Brush ? mouseCell.y : Mathf.Min(first.y, second.y);
            var endY = paintShape == PaintShape.Volume
                ? Mathf.Min(level.Size.y - 1, startY + volumeHeight - 1)
                : startY;
            if (paintTool == PaintTool.Box && paintShape == PaintShape.Brush)
                endY = Mathf.Min(level.Size.y - 1, startY + boxStackHeight - 1);

            Handles.color = paintTool == PaintTool.Erase
                ? new Color(1f, 0.2f, 0.2f, 0.9f)
                : new Color(0.2f, 1f, 0.85f, 0.9f);
            for (var y = startY; y <= endY; y++)
            for (var x = minX; x <= maxX; x++)
            for (var z = minZ; z <= maxZ; z++)
                Handles.DrawWireCube(ToWorld(new Vector3Int(x, y, z)), Vector3.one * level.CellSize * 0.96f);
        }

        private void CreateLevel()
        {
            if (!ResolveUnsavedBeforeLeaving()) return;
            var path = EditorUtility.SaveFilePanelInProject("Create Puzzle Box Level", "PuzzleBoxLevel", "asset",
                "Choose where to save the new level asset.");
            if (string.IsNullOrEmpty(path)) return;

            var asset = CreateInstance<LevelDefinition>();
            asset.InitializeNew(System.IO.Path.GetFileNameWithoutExtension(path));
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            level = asset;
            selectedCell = null;
            selectedKind = LevelElementKind.Any;
            resizeCandidate = level.Size;
            hasUnsavedChanges = false;
            Selection.activeObject = asset;
            activeLayer = 0;
            RefreshValidation();
            FrameLevel();
        }

        private void DuplicateLevel()
        {
            if (!ResolveUnsavedBeforeLeaving()) return;
            var path = EditorUtility.SaveFilePanelInProject("Duplicate Puzzle Box Level",
                level.name + " Copy", "asset", "Choose where to save the copied level asset.");
            if (string.IsNullOrEmpty(path)) return;

            var copy = Instantiate(level);
            copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
            copy.PrepareDuplicate(copy.name);
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            level = copy;
            selectedCell = null;
            selectedKind = LevelElementKind.Any;
            resizeCandidate = level.Size;
            hasUnsavedChanges = false;
            Selection.activeObject = copy;
            RefreshValidation();
        }

        private void SaveLevel()
        {
            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssets();
            hasUnsavedChanges = false;
        }

        private void RefreshValidation()
        {
            issues = level == null ? new List<ValidationIssue>() : LevelValidator.Validate(level.CreateLayout());
        }

        private void ClampLayer()
        {
            activeLayer = level == null ? 0 : Mathf.Clamp(activeLayer, 0, Mathf.Max(0, level.Size.y - 1));
        }

        private void FrameLevel()
        {
            if (level == null || SceneView.lastActiveSceneView == null) return;
            var center = new Vector3(level.Size.x - 1, level.Size.y - 1, level.Size.z - 1) * level.CellSize * 0.5f;
            var size = Mathf.Max(level.Size.x, Mathf.Max(level.Size.y, level.Size.z)) * level.CellSize;
            SceneView.lastActiveSceneView.Frame(new Bounds(center, Vector3.one * Mathf.Max(2f, size)), false);
        }

        private void FrameCell(Vector3Int cell)
        {
            activeLayer = Mathf.Clamp(cell.y, 0, level.Size.y - 1);
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.Frame(new Bounds(ToWorld(cell), Vector3.one * level.CellSize * 2f), false);
            Repaint();
            SceneView.RepaintAll();
        }

        private void ApplyResize()
        {
            var outsideCount = level.CountElementsOutside(resizeCandidate);
            if (outsideCount > 0 && !EditorUtility.DisplayDialog("Resize Puzzle Box Level",
                    $"The new size excludes {outsideCount} element(s). They will be removed. " +
                    "You can restore them with Undo.", "Resize and Remove", "Cancel"))
                return;

            Undo.RecordObject(level, "Resize Puzzle Box Level");
            level.Resize(resizeCandidate, true);
            activeLayer = Mathf.Clamp(activeLayer, 0, level.Size.y - 1);
            copyDestinationLayer = Mathf.Clamp(copyDestinationLayer, 0, level.Size.y - 1);
            MarkLevelChanged();
        }

        private void CopyActiveLayer()
        {
            Undo.RecordObject(level, "Copy Puzzle Box Layer");
            var added = level.CopyLayer(activeLayer, copyDestinationLayer, replaceCopiedLayer);
            if (added == 0)
            {
                EditorUtility.DisplayDialog("Copy Layer", "No elements were copied. Check destination conflicts and bounds.", "OK");
                return;
            }

            activeLayer = copyDestinationLayer;
            MarkLevelChanged();
        }

        private void SetSceneView(bool topDown)
        {
            puzzleViewLocked = false;
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null) return;
            sceneView.in2DMode = false;
            sceneView.isRotationLocked = false;
            var center = new Vector3(level.Size.x - 1, activeLayer * 2f, level.Size.z - 1) * level.CellSize * 0.5f;
            var rotation = topDown ? Quaternion.Euler(90f, 0f, 0f) : IsometricProjection.Rotation(previewQuarter);
            var size = Mathf.Max(level.Size.x, level.Size.z) * level.CellSize * 0.65f;
            sceneView.LookAt(center, rotation, Mathf.Max(2f, size), true);
            sceneView.Repaint();
        }

        private void FrameLayer()
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null) return;
            var center = new Vector3((level.Size.x - 1) * 0.5f, activeLayer,
                (level.Size.z - 1) * 0.5f) * level.CellSize;
            var boundsSize = new Vector3(level.Size.x, 1f, level.Size.z) * level.CellSize;
            sceneView.Frame(new Bounds(center, boundsSize), false);
        }

        private void PickElementAt(Vector3Int cell)
        {
            selectedCell = null;
            selectedKind = LevelElementKind.Any;
            if (level.HasPlayerSpawn && level.PlayerSpawn == cell)
            {
                paintTool = PaintTool.Player;
                selectedCell = cell;
                selectedKind = LevelElementKind.Player;
            }
            else if (level.Boxes.Contains(cell))
            {
                paintTool = PaintTool.Box;
                selectedCell = cell;
                selectedKind = LevelElementKind.Box;
            }
            else
            {
                var hasStair = false;
                var pickedStair = default(StairLink);
                foreach (var stair in level.Stairs)
                {
                    if (stair.LowerCell != cell && stair.UpperCell != cell) continue;
                    hasStair = true;
                    pickedStair = stair;
                    break;
                }
                if (hasStair)
                {
                    paintTool = PaintTool.Stair;
                    stairDirection = pickedStair.UpDirection;
                    selectedCell = pickedStair.LowerCell;
                    selectedKind = LevelElementKind.Stair;
                    activeLayer = pickedStair.LowerCell.y;
                }
                else if (level.Targets.Contains(cell))
                {
                    paintTool = PaintTool.Target;
                    selectedCell = cell;
                    selectedKind = LevelElementKind.Target;
                }
                else if (level.Solids.Contains(cell))
                {
                    paintTool = PaintTool.Solid;
                    selectedCell = cell;
                    selectedKind = LevelElementKind.Solid;
                }
            }
            Repaint();
            SceneView.RepaintAll();
        }

        private void RotateSelectedStair(int quarterTurns)
        {
            if (level == null || selectedKind != LevelElementKind.Stair || !selectedCell.HasValue) return;
            Undo.RecordObject(level, "Rotate Puzzle Box Stair");
            if (!level.RotateStair(selectedCell.Value, quarterTurns))
            {
                EditorUtility.DisplayDialog("Rotate Stair",
                    "The rotated stair would leave the grid or conflict with another stair.", "OK");
                return;
            }
            foreach (var stair in level.Stairs)
                if (stair.LowerCell == selectedCell.Value) { stairDirection = stair.UpDirection; break; }
            MarkLevelChanged();
        }

        private bool HandleRotationShortcut()
        {
            var current = Event.current;
            if (current.type != EventType.KeyDown || selectedKind != LevelElementKind.Stair) return false;
            if (current.keyCode != KeyCode.LeftBracket && current.keyCode != KeyCode.RightBracket) return false;
            RotateSelectedStair(current.keyCode == KeyCode.LeftBracket ? -1 : 1);
            current.Use();
            return true;
        }

        private bool TrySwitchLevel(LevelDefinition candidate)
        {
            if (candidate == level) return true;
            if (!ResolveUnsavedBeforeLeaving()) return false;

            level = candidate;
            selectedCell = null;
            selectedKind = LevelElementKind.Any;
            if (level != null) resizeCandidate = level.Size;
            ClampLayer();
            RefreshValidation();
            UpdateUnsavedState();
            SceneView.RepaintAll();
            return true;
        }

        private bool ResolveUnsavedBeforeLeaving()
        {
            if (level == null || !EditorUtility.IsDirty(level)) return true;
            var choice = EditorUtility.DisplayDialogComplex("Unsaved Puzzle Box Level",
                $"Save changes to '{level.DisplayName}' before continuing?",
                "Save", "Cancel", "Discard");
            if (choice == 1) return false;
            if (choice == 0)
            {
                SaveLevel();
                return true;
            }

            var path = AssetDatabase.GetAssetPath(level);
            if (!string.IsNullOrEmpty(path))
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            hasUnsavedChanges = false;
            return true;
        }

        private void UpdateUnsavedState()
        {
            hasUnsavedChanges = level != null && EditorUtility.IsDirty(level);
            saveChangesMessage = level == null
                ? "Save changes to the Puzzle Box level?"
                : $"Save changes to '{level.DisplayName}'?";
        }

        public override void SaveChanges()
        {
            if (level != null) SaveLevel();
            base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            if (level != null)
            {
                var path = AssetDatabase.GetAssetPath(level);
                if (!string.IsNullOrEmpty(path))
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            hasUnsavedChanges = false;
            base.DiscardChanges();
        }
    }
}
