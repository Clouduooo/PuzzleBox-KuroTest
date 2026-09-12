using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuzzleBox.Editor
{
    /// <summary>Shared, testable geometry and render-state rules for Scene view editing aids.</summary>
    public static class PuzzleBoxScenePreviewStyle
    {
        public const float InactiveLayerAlpha = 0.55f;

        public static float LayerBottomY(int layer, float cellSize)
        {
            return (layer - 0.5f) * cellSize;
        }

        public static void ConfigureOpaqueMaterial(Material material)
        {
            material.SetOverrideTag("RenderType", "Opaque");
            material.SetInt("_SrcBlend", (int)BlendMode.One);
            material.SetInt("_DstBlend", (int)BlendMode.Zero);
            material.SetInt("_Cull", (int)CullMode.Back);
            material.SetInt("_ZWrite", 1);
            material.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            material.renderQueue = (int)RenderQueue.Geometry;
        }

        public static void ConfigureTransparentMaterial(Material material)
        {
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)CullMode.Back);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        public static Color WithLayerOpacity(Color color, bool isActiveLayer)
        {
            color.a = isActiveLayer ? 1f : InactiveLayerAlpha;
            return color;
        }

        public static Color SolidColorForHeight(int height)
        {
            // A golden-ratio hue step keeps neighbouring heights clearly distinct and
            // remains deterministic for grids taller than a fixed palette.
            var hue = Mathf.Repeat(0.58f + height * 0.13750777f, 1f);
            var cycle = Mathf.FloorToInt(Mathf.Max(0, height) / 7f);
            var value = Mathf.Clamp(0.60f + cycle * 0.035f, 0.60f, 0.74f);
            var color = Color.HSVToRGB(hue, 0.42f, value);
            color.a = 1f;
            return color;
        }

        public static Mesh CreateCubeMesh()
        {
            var mesh = new Mesh { name = "Puzzle Box Opaque Preview Cube" };
            var vertices = new List<Vector3>(24);
            var normals = new List<Vector3>(24);
            var triangles = new List<int>(36);
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                var first = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
                triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
                triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
            }
            Face(new Vector3(-.5f,-.5f,-.5f), new Vector3(-.5f,.5f,-.5f), new Vector3(.5f,.5f,-.5f), new Vector3(.5f,-.5f,-.5f), Vector3.back);
            Face(new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f), new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f), Vector3.forward);
            Face(new Vector3(-.5f,-.5f,-.5f), new Vector3(-.5f,-.5f,.5f), new Vector3(-.5f,.5f,.5f), new Vector3(-.5f,.5f,-.5f), Vector3.left);
            Face(new Vector3(.5f,-.5f,-.5f), new Vector3(.5f,.5f,-.5f), new Vector3(.5f,.5f,.5f), new Vector3(.5f,-.5f,.5f), Vector3.right);
            Face(new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f), new Vector3(.5f,-.5f,.5f), new Vector3(-.5f,-.5f,.5f), Vector3.down);
            Face(new Vector3(-.5f,.5f,-.5f), new Vector3(-.5f,.5f,.5f), new Vector3(.5f,.5f,.5f), new Vector3(.5f,.5f,-.5f), Vector3.up);
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh CreateStairSlopeMesh()
        {
            var mesh = new Mesh { name = "Puzzle Box Stair Slope Preview" };
            mesh.vertices = new[]
            {
                new Vector3(-.4f,-.48f,-.48f), new Vector3(.4f,-.48f,-.48f),
                new Vector3(.4f,.48f,.48f), new Vector3(-.4f,.48f,.48f)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Vector3[] CreateCubeEdgeSegments(Vector3 center, float size)
        {
            var half = size * .5f;
            var corners = new[]
            {
                center + new Vector3(-half,-half,-half), center + new Vector3(half,-half,-half),
                center + new Vector3(half,-half,half), center + new Vector3(-half,-half,half),
                center + new Vector3(-half,half,-half), center + new Vector3(half,half,-half),
                center + new Vector3(half,half,half), center + new Vector3(-half,half,half)
            };
            var edgeIndices = new[]
            {
                0,1, 1,2, 2,3, 3,0,
                4,5, 5,6, 6,7, 7,4,
                0,4, 1,5, 2,6, 3,7
            };
            var segments = new Vector3[edgeIndices.Length];
            for (var i = 0; i < edgeIndices.Length; i++) segments[i] = corners[edgeIndices[i]];
            return segments;
        }
    }
}
