using System.Collections.Generic;
using System.Linq;
using PuzzleBox.Data;
using PuzzleBox.Rules;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuzzleBox.Editor
{
    public sealed partial class PuzzleBoxLevelEditorWindow
    {
        [SerializeField] private int previewQuarter;
        [SerializeField] private bool puzzleViewLocked;
        private bool pairRoads;
        private bool showCandidates;
        private bool showReachable;
        [SerializeField] private bool showActionPreview = true;
        private string actionPreviewKey;
        private string[] actionDescriptions;
        private ViewStep[] actionTargets;
        [SerializeField] private bool showDynamicRoads = true;
        private Vector3Int? firstRoad;
        private string perspectiveHash;
        private LevelLayout perspectiveLayout;
        private PerspectiveNetwork perspectiveNetwork;
        private List<PerspectiveLink> candidates = new List<PerspectiveLink>();
        private string perspectiveMessage = "点击平台顶面选道路 A，再点击道路 B；或扫描候选后点「添加」。";

        private void RefreshPerspectiveCache()
        {
            var hash = EditorJsonUtility.ToJson(level);
            if (hash == perspectiveHash) return;
            perspectiveHash = hash;
            perspectiveLayout = level.CreateLayout();
            candidates = PerspectiveRules.FindCandidates(perspectiveLayout);
            perspectiveNetwork = new PerspectiveNetwork(perspectiveLayout, perspectiveLayout.Boxes);
            if (firstRoad.HasValue && !PerspectiveRules.IsSurface(perspectiveLayout, firstRoad.Value)) firstRoad = null;
        }

        private void DrawPerspectivePanel()
        {
            EditorGUILayout.LabelField("视角道路 / Perspective Roads", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("◀ Q")) SetPuzzleView(previewQuarter - 1);
                for (var q = 0; q < 4; q++)
                {
                    if (GUILayout.Toggle(puzzleViewLocked && previewQuarter == q, "V" + q, EditorStyles.miniButton))
                        if (!puzzleViewLocked || previewQuarter != q) SetPuzzleView(q);
                }
                if (GUILayout.Button("E ▶")) SetPuzzleView(previewQuarter + 1);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(puzzleViewLocked ? "切换自由观察" : "锁定等距视角"))
                {
                    if (puzzleViewLocked) SetFreeSceneView(SceneView.lastActiveSceneView);
                    else SetPuzzleView(previewQuarter);
                }
                if (GUILayout.Button($"设 V{previewQuarter} 为初始视角"))
                {
                    Undo.RecordObject(level, "Set Initial Puzzle View");
                    level.SetInitialView(previewQuarter);
                    MarkLevelChanged();
                }
            }
            EditorGUILayout.LabelField(puzzleViewLocked
                ? $"锁定预览 V{previewQuarter} · 关卡初始 V{level.InitialViewQuarter}"
                : $"自由观察 · 连通参考 V{previewQuarter} · 关卡初始 V{level.InitialViewQuarter}", EditorStyles.miniLabel);
            if (!puzzleViewLocked)
                EditorGUILayout.HelpBox("Scene 原生导航：右键 + WASD/QE 飞行，中键平移，Alt + 左键环绕，滚轮缩放。点击 V0–V3 返回锁定预览。", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("四视角美术对比")) PuzzleBoxLevelPreviewWindow.OpenComparison(level);
                if (GUILayout.Button("生成此关卡试玩场景…")) PuzzleBoxPlaySceneCreator.CreateSceneFor(level);
            }
            var pairing = EditorGUILayout.ToggleLeft("连接配对：点击两个平台顶面（暂停普通绘制）", pairRoads);
            if (pairing != pairRoads)
            {
                pairRoads = pairing;
                firstRoad = null;
                if (pairRoads) { showCandidates = true; layerDisplay = LayerDisplay.All; SetPuzzleView(previewQuarter); }
                SceneView.RepaintAll();
            }
            showCandidates = EditorGUILayout.ToggleLeft("显示 / 扫描投影连接候选", showCandidates);
            showDynamicRoads = EditorGUILayout.ToggleLeft("预览箱顶动态连接（运行时自动生成，无需添加）", showDynamicRoads);
            showReachable = EditorGUILayout.ToggleLeft("显示出生点当前可达区域（不推箱）", showReachable);
            EditorGUILayout.HelpBox("绿：可见步行连接　黄：未确认候选　红：遮挡、占用或非前景目标。\n" +
                "Solid 与箱子统一遮挡；先选当前视角最前方的目标，再验证行走/推动。\n" +
                "前景不可通行时不改选后方道路；箱子占据入口时用下方行动预览检查推动。", MessageType.None);
            if (pairRoads) EditorGUILayout.HelpBox(firstRoad.HasValue ? $"道路 A：{firstRoad.Value}，继续点击道路 B。" : perspectiveMessage, MessageType.Info);

            RefreshPerspectiveCache();
            showActionPreview = EditorGUILayout.ToggleLeft("可见目标 / 行动预览（不修改草稿）", showActionPreview);
            if (showActionPreview) DrawActionPreview();
            if (showDynamicRoads && perspectiveLayout.Boxes.Count > 0) DrawDynamicRoadPanel();
            for (var i = 0; i < level.PerspectiveLinks.Count; i++)
            {
                var link = level.PerspectiveLinks[i];
                var valid = PerspectiveRules.ValidViews(perspectiveLayout, link);
                var active = perspectiveNetwork.IsStaticActive(link, previewQuarter);
                EditorGUILayout.LabelField($"连接 {i + 1} · {ViewsText(link.ViewMask)} · {(active ? "接通" : "未接通")}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"{link.CellA} {link.DirectionFromA} ↔ {link.CellB}", EditorStyles.miniLabel);
                if ((valid & link.ViewMask) != link.ViewMask || link.ViewMask == 0)
                    EditorGUILayout.HelpBox("道路被改动：请修正地形或删除后重新配对。", MessageType.Error);
                else if (link.EnabledIn(previewQuarter) &&
                         PerspectiveRules.TryGetOccludingSolid(perspectiveNetwork.DynamicGeometry, link, previewQuarter, out var blocker))
                    EditorGUILayout.HelpBox($"V{previewQuarter} 被 {(perspectiveLayout.Solids.Contains(blocker) ? "Solid" : "Box")} {blocker} 遮挡。", MessageType.Warning);
                else if (link.EnabledIn(previewQuarter) && !active)
                    EditorGUILayout.HelpBox("端点被箱子占用或被更靠前的目标替代；查看行动预览判断是否能推。", MessageType.Info);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("定位 / 查看生效视角")) FocusLink(link);
                    if (GUILayout.Button("删除", GUILayout.Width(55)))
                    {
                        Undo.RecordObject(level, "Remove Perspective Road");
                        level.RemovePerspectiveLink(i);
                        MarkLevelChanged();
                        GUIUtility.ExitGUI();
                    }
                }
            }
            if (showCandidates)
            {
                var pending = candidates.Where(c => !level.PerspectiveLinks.Any(c.SamePair) &&
                    (!firstRoad.HasValue || c.CellA == firstRoad || c.CellB == firstRoad)).ToList();
                EditorGUILayout.LabelField($"候选：{pending.Count}（所有视角）", EditorStyles.boldLabel);
                foreach (var link in pending.Take(60))
                {
                    EditorGUILayout.LabelField($"{link.CellA} ↔ {link.CellB} · {ViewsText(link.ViewMask)}", EditorStyles.miniLabel);
                    var candidateQuarter = link.EnabledIn(previewQuarter) ? previewQuarter :
                        Enumerable.Range(0, 4).First(link.EnabledIn);
                    if (PerspectiveRules.TryGetOccludingSolid(perspectiveNetwork.DynamicGeometry, link, candidateQuarter, out var blocker))
                        EditorGUILayout.LabelField($"V{candidateQuarter} 遮挡：{(perspectiveLayout.Solids.Contains(blocker) ? "Solid" : "Box")} {blocker}", EditorStyles.miniLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("查看")) FocusLink(link);
                        if (GUILayout.Button("添加连接")) { AddRoadLink(link); GUIUtility.ExitGUI(); }
                    }
                }
                if (pending.Count > 60) EditorGUILayout.HelpBox("候选较多，先点击一个平台可缩小列表。", MessageType.Info);
            }
            EditorGUILayout.Space();
        }

        private void DrawActionPreview()
        {
            Vector3Int? source = firstRoad;
            if (!source.HasValue && selectedCell.HasValue)
                source = selectedCell.Value + (selectedKind == LevelElementKind.Solid || selectedKind == LevelElementKind.Box ? Vector3Int.up : Vector3Int.zero);
            if (!source.HasValue && level.HasPlayerSpawn) source = level.PlayerSpawn;
            if (!source.HasValue) return;
            EditorGUILayout.LabelField($"预览起点 {source.Value} · V{previewQuarter}", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Picker 选平台/箱子时取其顶面，或配对模式先点道路 A；未选中时使用出生点。方向为世界方向。青色可达区域只算行走，下面同时检查推动和落点。", MessageType.None);
            if (!PerspectiveRules.IsSurface(perspectiveNetwork.DynamicGeometry, source.Value))
            { EditorGUILayout.HelpBox("此处没有空出的站立格，请选择未被覆盖的平台或箱顶。", MessageType.Info); return; }
            var key = perspectiveHash + ":" + source + ":" + previewQuarter;
            if (key != actionPreviewKey)
            {
                actionPreviewKey = key;
                actionDescriptions = new string[4];
                actionTargets = new ViewStep[4];
                for (var d = 0; d < 4; d++)
                {
                    var result = PuzzleBoxRuleEngine.PreviewMove(perspectiveLayout, source.Value, previewQuarter,
                        (GridDirection)d, out actionTargets[d], out var settled);
                    var target = actionTargets[d];
                    var prefix = $"{(GridDirection)d} → {target.Destination} · {(target.IsBox ? "接触箱子" : "站立目标")}";
                    if (!result.Succeeded)
                        actionDescriptions[d] = prefix + "\n阻挡：" + PuzzleBox.View.PuzzleBoxFeedbackMessage.Blocked(result.BlockReason).Body;
                    else if (result.Pushed)
                        actionDescriptions[d] = prefix + "\n推动：" + string.Join("；", result.MovedBoxIds.Select(id =>
                            settled.Boxes.TryGetValue(id, out var cell) ? $"箱 {id} 落到 {cell}" : $"箱 {id} 坠落出界")) +
                            (settled.Failure == PuzzleBox.State.FailureReason.None ? "" : "\n结果失败：" + settled.Failure);
                    else actionDescriptions[d] = prefix + (target.UsesLink ? "\n可跨视角连接" : "\n可行走");
                }
            }
            for (var d = 0; d < 4; d++)
            {
                EditorGUILayout.HelpBox(actionDescriptions[d], MessageType.None);
                if (GUILayout.Button($"定位 {(GridDirection)d} 目标"))
                    SceneView.lastActiveSceneView?.Frame(new Bounds(ToWorld(actionTargets[d].Destination), Vector3.one * 3f * level.CellSize), true);
            }
        }

        private void DrawDynamicRoadPanel()
        {
            var roads = perspectiveNetwork.DynamicCandidates;
            EditorGUILayout.LabelField($"箱顶动态候选：{roads.Count}（所有视角）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("D / 青绿线：当前可见步行连接；红色：遮挡、占用或被前景目标替代。按草稿箱子位置预览；" +
                "运行时按推动、重力后的实际位置重算。堆叠只用露出的箱顶，不会写入已保存连接。", MessageType.None);
            foreach (var link in roads.Take(40))
            {
                var q = link.EnabledIn(previewQuarter) ? previewQuarter :
                    Enumerable.Range(0, 4).First(link.EnabledIn);
                EditorGUILayout.LabelField($"{link.CellA} ↔ {link.CellB} · {ViewsText(link.ViewMask)}", EditorStyles.miniLabel);
                if (PerspectiveRules.TryGetOccludingSolid(perspectiveNetwork.DynamicGeometry, link, q, out var blocker))
                    EditorGUILayout.LabelField($"V{q} 遮挡：{(perspectiveLayout.Solids.Contains(blocker) ? "Solid" : "Box")} {blocker}", EditorStyles.miniLabel);
                if (GUILayout.Button("查看动态连接")) FocusLink(link);
            }
            if (roads.Count > 40) EditorGUILayout.LabelField("仅列出前 40 条；Scene 显示当前视角的连接。", EditorStyles.miniLabel);
        }

        private static string ViewsText(int mask) => string.Join(" / ", Enumerable.Range(0, 4)
            .Where(q => (mask & (1 << q)) != 0).Select(q => "V" + q));

        private void SetPuzzleView(int quarter)
        {
            previewQuarter = IsometricProjection.Normalize(quarter);
            puzzleViewLocked = true;
            var scene = SceneView.lastActiveSceneView;
            if (scene != null)
            {
                var center = new Vector3(level.Size.x - 1, level.Size.y - 1, level.Size.z - 1) * .5f * level.CellSize;
                scene.in2DMode = false;
                scene.orthographic = true;
                scene.LookAtDirect(center, IsometricProjection.Rotation(previewQuarter),
                    Mathf.Max(level.Size.x, level.Size.z) * level.CellSize * .65f);
            }
            sceneArt?.SetViewQuarter(previewQuarter);
            Repaint();
            SceneView.RepaintAll();
        }

        private void SetFreeSceneView(SceneView scene)
        {
            puzzleViewLocked = false;
            if (scene != null)
            {
                // Free means native 3D Scene navigation, not an unlocked orthographic camera.
                // Keep the current framing; do not jump back to the level center.
                scene.in2DMode = false;
                scene.isRotationLocked = false;
                scene.orthographic = false;
                scene.LookAtDirect(scene.pivot, scene.rotation, scene.size);
                scene.Repaint();
            }
            Repaint();
            SceneView.RepaintAll();
        }

        private bool ShouldHandlePuzzleViewShortcut(Event evt, bool nativeNavigation)
        {
            return puzzleViewLocked && !nativeNavigation && evt.type == EventType.KeyDown &&
                !evt.alt && !evt.control && !evt.command && !evt.shift &&
                !EditorGUIUtility.editingTextField && (evt.keyCode == KeyCode.Q || evt.keyCode == KeyCode.E);
        }

        private static bool IsNativeSceneNavigation(Event evt)
        {
            // An idle hand tool owns mouse gestures, not the locked-view Q/E shortcuts.
            // During a real navigation gesture Unity owns hotControl, including fly keys.
            return evt.alt || GUIUtility.hotControl != 0 || (evt.isMouse && evt.button != 0) ||
                   (Tools.current == Tool.View ? !evt.isKey : Tools.viewToolActive);
        }

        private void FocusLink(PerspectiveLink link)
        {
            var q = Enumerable.Range(0, 4).FirstOrDefault(link.EnabledIn);
            SetPuzzleView(q);
            SceneView.lastActiveSceneView?.Frame(new Bounds((ToWorld(link.CellA) + ToWorld(link.CellB)) * .5f,
                Vector3.one * Mathf.Max(4f * level.CellSize, Vector3.Distance(ToWorld(link.CellA), ToWorld(link.CellB)) + level.CellSize)), true);
        }

        private void AddRoadLink(PerspectiveLink link)
        {
            Undo.RecordObject(level, "Connect Perspective Roads");
            if (!level.AddPerspectiveLink(link))
            { perspectiveMessage = "无法添加：连接已存在或地形/方向不合法。"; return; }
            firstRoad = null;
            perspectiveMessage = "连接已保存，可继续配对。旋转到其他视角检查断开效果。";
            MarkLevelChanged();
        }

        private bool HandlePerspectiveScene(SceneView scene, bool nativeNavigation)
        {
            var evt = Event.current;
            if (ShouldHandlePuzzleViewShortcut(evt, nativeNavigation))
            { SetPuzzleView(previewQuarter + (evt.keyCode == KeyCode.Q ? -1 : 1)); evt.Use(); return true; }
            if (puzzleViewLocked)
            {
                scene.rotation = IsometricProjection.Rotation(previewQuarter);
                scene.orthographic = true;
            }
            if (!pairRoads && !showCandidates && !showReachable && !showDynamicRoads && level.PerspectiveLinks.Count == 0) return false;
            RefreshPerspectiveCache();
            sceneArt?.SetViewQuarter(previewQuarter);
            var oldTest = Handles.zTest;
            Handles.zTest = CompareFunction.LessEqual;
            if (showReachable)
                foreach (var cell in PerspectiveRules.Reachable(perspectiveLayout, previewQuarter, perspectiveNetwork))
                {
                    Handles.color = new Color(.1f, .9f, .9f, .22f);
                    Handles.DrawSolidDisc(ToWorld(cell) + Vector3.down * .47f * level.CellSize, Vector3.up, .27f * level.CellSize);
                }
            for (var i = 0; i < level.PerspectiveLinks.Count; i++)
            {
                var link = level.PerspectiveLinks[i];
                var invalid = (PerspectiveRules.ValidViews(perspectiveLayout, link) & link.ViewMask) != link.ViewMask;
                var active = perspectiveNetwork.IsStaticActive(link, previewQuarter);
                var color = invalid || (link.EnabledIn(previewQuarter) && !active) ? Color.red :
                    active ? Color.green : new Color(.55f, .55f, .55f);
                DrawRoadLink(link, color, "L" + (i + 1));
            }
            if (showCandidates)
                foreach (var link in candidates.Where(c => c.EnabledIn(previewQuarter) && !level.PerspectiveLinks.Any(c.SamePair)).Take(100))
                    DrawRoadLink(link, perspectiveNetwork.IsStaticActive(link, previewQuarter) ? Color.yellow : Color.red, "?");
            if (showDynamicRoads)
                foreach (var link in perspectiveNetwork.DynamicCandidates.Where(c => c.EnabledIn(previewQuarter)).Take(100))
                    DrawRoadLink(link, perspectiveNetwork.IsDynamicActive(link, previewQuarter)
                        ? new Color(.15f, 1f, .65f) : Color.red, "D");
            if (firstRoad.HasValue)
            {
                Handles.color = Color.cyan;
                Handles.DrawWireCube(ToWorld(firstRoad.Value), Vector3.one * level.CellSize);
            }
            Handles.zTest = oldTest;
            if (!pairRoads) return false;
            if (nativeNavigation) return true;
            if (evt.type == EventType.Layout) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            { firstRoad = null; evt.Use(); Repaint(); }
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                if (TryPickSurface(evt.mousePosition, out var cell))
                {
                    if (!firstRoad.HasValue) firstRoad = cell;
                    else
                    {
                        var a = firstRoad.Value;
                        var matches = candidates.Where(c => (c.CellA == a && c.CellB == cell) || (c.CellB == a && c.CellA == cell)).ToList();
                        if (matches.Count == 1) AddRoadLink(matches[0]);
                        else
                        {
                            firstRoad = null;
                            perspectiveMessage = matches.Count == 0 ? "这两个道路没有投影对齐的开放边缘。可查看黄色候选。" :
                                "这两个道路有多个出口组合，请在候选列表中选择。";
                        }
                    }
                    Repaint(); SceneView.RepaintAll();
                }
                evt.Use();
            }
            return true;
        }

        private void DrawRoadLink(PerspectiveLink link, Color color, string label)
        {
            Handles.color = color;
            var a = IsometricProjection.Edge(link.CellA, link.DirectionFromA) * level.CellSize;
            var b = IsometricProjection.Edge(link.CellB, link.DirectionFromA.Opposite()) * level.CellSize;
            var tangent = Vector3.Cross(Vector3.up, (Vector3)link.DirectionFromA.ToOffset()) * .46f * level.CellSize;
            var lift = Vector3.up * .04f * level.CellSize;
            Handles.DrawAAPolyLine(5f, a + lift - tangent, a + lift + tangent);
            Handles.DrawAAPolyLine(5f, b + lift - tangent, b + lift + tangent);
            if (!puzzleViewLocked) Handles.DrawDottedLine(a + lift, b + lift, 4f);
            Handles.Label(a + Vector3.up * .12f * level.CellSize, label);
        }

        private bool TryPickSurface(Vector2 mouse, out Vector3Int standing)
        {
            standing = default;
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            var nearest = float.PositiveInfinity;
            Vector3Int? hitSolid = null;
            foreach (var solid in level.Solids)
            {
                if (!IsVisible(solid)) continue;
                if (new Bounds(ToWorld(solid), Vector3.one * level.CellSize).IntersectRay(ray, out var distance) && distance < nearest)
                { nearest = distance; hitSolid = solid; }
            }
            if (!hitSolid.HasValue) return false;
            standing = hitSolid.Value + Vector3Int.up;
            var point = ray.GetPoint(nearest);
            return Mathf.Abs(point.y - (hitSolid.Value.y + .5f) * level.CellSize) < .01f * level.CellSize &&
                   PerspectiveRules.IsSurface(perspectiveLayout, standing);
        }
    }
}
