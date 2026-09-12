using PuzzleBox.Data;
using PuzzleBox.Rules;
using PuzzleBox.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuzzleBox.Editor
{
    public static class PuzzleBoxPlaySceneCreator
    {
        public static void CreateSceneFor(LevelDefinition level)
        {
            if (level == null) return;
            var errors = LevelValidator.Validate(level.CreateLayout()).FindAll(issue => issue.Severity == ValidationSeverity.Error);
            if (errors.Count > 0)
            {
                EditorUtility.DisplayDialog("关卡有错误", "请先修正 Validation 中的错误，再生成试玩场景。", "知道了");
                return;
            }
            var path = EditorUtility.SaveFilePanelInProject("生成关卡试玩场景", level.name + " Play", "unity",
                "保存引用此关卡的试玩场景。关卡资源会一起保存。");
            if (string.IsNullOrEmpty(path) || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            AssetDatabase.SaveAssets();
            SaveScene(level, path);
        }

        private static void SaveScene(LevelDefinition level, string path)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Puzzle Box Level");
            root.AddComponent<PuzzleBoxLevelView>().SetLevel(level);
            root.AddComponent<PuzzleBoxDemoController>().SetLevel(level);

            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            level.ArtTheme.ConfigureCamera(camera, level.Size, level.CellSize, level.InitialViewQuarter);

            var sun = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>();
            level.ArtTheme.ConfigureSun(sun);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white * .7f;
            EditorSceneManager.SaveScene(scene, path);
        }
    }
}
