using PuzzleBox.Data;
using PuzzleBox.View;
using UnityEditor;
using UnityEngine;

namespace PuzzleBox.Editor
{
    /// <summary>Level Editor preview using the same models, lighting and camera as play.</summary>
    public sealed class PuzzleBoxLevelPreviewWindow : EditorWindow
    {
        [SerializeField] private LevelDefinition level;
        private PreviewRenderUtility preview;
        private PuzzleBoxLevelView view;
        private string contentHash;
        private double nextRepaint;
        [SerializeField] private bool compareViews;
        [SerializeField] private int viewQuarter;

        public static void Open(LevelDefinition value)
        {
            Open(value, false);
        }

        public static void OpenComparison(LevelDefinition value)
        {
            Open(value, true);
        }

        private static void Open(LevelDefinition value, bool comparison)
        {
            var window = GetWindow<PuzzleBoxLevelPreviewWindow>("关卡美术预览");
            window.level = value;
            window.compareViews = comparison;
            window.contentHash = null;
            window.Show();
        }

        private void OnEnable() { EditorApplication.update += Tick; }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            preview?.Cleanup();
            preview = null;
            view = null;
            contentHash = null;
        }

        private void Tick()
        {
            if (EditorApplication.timeSinceStartup < nextRepaint) return;
            nextRepaint = EditorApplication.timeSinceStartup + .2;
            Repaint();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                level = (LevelDefinition)EditorGUILayout.ObjectField("关卡", level, typeof(LevelDefinition), false);
                if (GUILayout.Button("刷新模型", GUILayout.Width(72))) contentHash = null;
            }
            EditorGUILayout.LabelField("与试玩共用模型、材质、光照和镜头；修改关卡后自动刷新。", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                compareViews = GUILayout.Toggle(compareViews, "四视角对比", EditorStyles.miniButton);
                viewQuarter = GUILayout.Toolbar(viewQuarter, new[] { "V0", "V1", "V2", "V3" });
            }
            var rect = GUILayoutUtility.GetRect(100, 10000, 100, 10000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (level == null || level.ArtTheme == null)
            {
                GUI.Label(rect, "请选择关卡。默认主题位于 Resources/PuzzleBox/ParkTheme。", EditorStyles.centeredGreyMiniLabel);
                return;
            }
            if (Event.current.type != EventType.Repaint || rect.width < 1 || rect.height < 1) return;
            if (preview == null)
            {
                preview = new PreviewRenderUtility();
                var root = new GameObject("Puzzle Box Level Preview");
                preview.AddSingleGO(root);
                view = root.AddComponent<PuzzleBoxLevelView>();
            }
            var hash = EditorJsonUtility.ToJson(level) + EditorJsonUtility.ToJson(level.ArtTheme);
            if (contentHash != hash)
            {
                view.SetLevel(level);
                contentHash = hash;
            }
            if (compareViews)
            {
                for (var q = 0; q < 4; q++)
                {
                    var tile = new Rect(rect.x + q % 2 * rect.width * .5f, rect.y + q / 2 * rect.height * .5f,
                        rect.width * .5f - 2, rect.height * .5f - 2);
                    RenderView(tile, q);
                }
            }
            else RenderView(rect, viewQuarter);
        }

        private void RenderView(Rect rect, int quarter)
        {
            view.SetViewQuarter(quarter);
            preview.BeginPreview(rect, GUIStyle.none);
            preview.camera.aspect = rect.width / rect.height;
            level.ArtTheme.ConfigureCamera(preview.camera, level.Size, level.CellSize, quarter);
            level.ArtTheme.ConfigureSun(preview.lights[0]);
            preview.lights[1].intensity = 0;
            preview.ambientColor = Color.white * .7f;
            preview.Render(true, false);
            GUI.DrawTexture(rect, preview.EndPreview(), ScaleMode.StretchToFill, false);
            GUI.Label(new Rect(rect.x + 8, rect.y + 6, 140, 25), $"V{quarter} · {quarter * 90}°", EditorStyles.whiteLargeLabel);
        }
    }
}
