using UnityEngine;
using PuzzleBox.Data;
using UnityEngine.Rendering.Universal;

namespace PuzzleBox.View
{
    [CreateAssetMenu(menuName = "Puzzle Box/Art Theme", fileName = "ParkTheme")]
    public sealed class ParkTheme : ScriptableObject
    {
        public const string DefaultResource = "PuzzleBox/ParkTheme";
        public const float IsometricPitch = IsometricProjection.Pitch;
        public GameObject groundTile;
        public GameObject platformTile;
        public GameObject box;
        public GameObject player;
        public GameObject target;
        public GameObject stair;
        public GameObject hedge;
        public GameObject bush;
        public GameObject flowers;
        public GameObject rock;
        public GameObject bench;
        public GameObject lamp;
        public GameObject grassTuft;
        public Material meadowMaterial;
        [Header("Motion effects")]
        public Material hopEffectMaterial;
        public Material pushEffectMaterial;
        public Color background = new Color(0.55f, 0.72f, 0.51f);
        [Tooltip("Y is the base yaw. X and Z are forced to a true isometric projection.")]
        public Vector3 cameraAngles = new Vector3(IsometricPitch, 45f, 0f);
        public Vector3 sunAngles = new Vector3(40f, -32f, 0f);
        [Range(0.5f, 2f)] public float framing = 1.05f;

        public void ConfigureCamera(Camera camera, Vector3Int size, float cellSize, int? viewQuarter = null)
        {
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 150f;
            camera.allowHDR = false;
            camera.transform.rotation = viewQuarter.HasValue ? IsometricProjection.Rotation(viewQuarter.Value) :
                Quaternion.Euler(IsometricPitch, cameraAngles.y, 0f);
            var center = GetFocusPoint(size, cellSize);
            camera.transform.position = center - camera.transform.forward * 35f * cellSize;
            // Include a small decorative border, independent of the logical grid.
            var extent = (size.x + size.z) * 0.28f + 1.6f;
            camera.orthographicSize = extent * framing * cellSize * Mathf.Max(1f, 1.55f / camera.aspect);
        }

        public Vector3 GetFocusPoint(Vector3Int size, float cellSize)
        {
            return new Vector3((size.x - 1) * .5f, .85f, (size.z - 1) * .5f) * cellSize;
        }

        public void ConfigureSun(Light light)
        {
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(sunAngles);
            light.color = Color.white;
            light.intensity = 1f;
            light.shadows = LightShadows.Hard;
            light.shadowStrength = 1f;
            light.shadowBias = 0.025f;
            light.shadowNormalBias = 0.08f;
            var data = light.GetComponent<UniversalAdditionalLightData>();
            if (data == null) data = light.gameObject.AddComponent<UniversalAdditionalLightData>();
            data.usePipelineSettings = false;
        }
    }
}
