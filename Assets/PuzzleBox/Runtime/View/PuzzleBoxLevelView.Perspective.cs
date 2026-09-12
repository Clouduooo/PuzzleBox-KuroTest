using System.Collections.Generic;
using PuzzleBox.Data;
using PuzzleBox.Rules;
using UnityEngine;
using UnityEngine.Rendering;

namespace PuzzleBox.View
{
    public sealed partial class PuzzleBoxLevelView
    {
        private Transform linkMarkersRoot;
        private readonly List<GameObject> linkMarkers = new List<GameObject>();
        private PerspectiveNetwork previewNetwork;
        private IReadOnlyList<PerspectiveLink> shownLinks;

        public static Vector3 EvaluatePerspectiveHop(Vector3 start, Vector3 end, Vector3 gridStep, float t, float height)
        {
            // The depth switch at the seam has zero screen displacement in the active view.
            var point = EvaluateHop(start, start + gridStep, t, height);
            if (t >= .5f) point += end - start - gridStep;
            return point;
        }

        public static Vector3 EvaluatePerspectiveSlide(Vector3 start, Vector3 end, Vector3 gridStep, float t)
        {
            t = Mathf.Clamp01(t);
            var point = start + gridStep * t;
            if (t >= .5f) point += end - start - gridStep;
            return point;
        }

        private void BuildLinkMarkers()
        {
            linkMarkers.Clear();
            shownLinks = null;
            previewNetwork = new PerspectiveNetwork(level.CreateLayout(), level.Boxes);
            linkMarkersRoot = new GameObject("Perspective seams (visual only)").transform;
            linkMarkersRoot.SetParent(generatedRoot, false);
            MarkTransient(linkMarkersRoot.gameObject);
            SetViewQuarter(level.InitialViewQuarter);
        }

        private void MakeEdgeMark(Transform parent, Vector3Int cell, GridDirection outward)
        {
            var go = new GameObject("Seam highlight");
            go.transform.SetParent(parent, false);
            PositionEdgeMark(go.transform, cell, outward);
            go.transform.localScale = new Vector3(.8f, .012f, .045f) * level.CellSize;
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = level.ArtTheme.hopEffectMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        public void SetViewQuarter(int quarter)
        {
            if (linkMarkersRoot == null || previewNetwork == null) return;
            var links = (state != null ? state.PerspectiveNetwork : previewNetwork).ActiveLinks(quarter);
            if (!ReferenceEquals(shownLinks, links))
            {
                // Pool edge markers: moving a box changes the derived graph, not the terrain.
                for (var i = 0; i < links.Count; i++)
                {
                    var link = links[i];
                    if (i == linkMarkers.Count)
                    {
                        var group = new GameObject("Perspective seam");
                        group.transform.SetParent(linkMarkersRoot, false);
                        MakeEdgeMark(group.transform, link.CellA, link.DirectionFromA);
                        MakeEdgeMark(group.transform, link.CellB, link.DirectionFromA.Opposite());
                        MarkTransient(group);
                        linkMarkers.Add(group);
                    }
                    PositionEdgeMark(linkMarkers[i].transform.GetChild(0), link.CellA, link.DirectionFromA);
                    PositionEdgeMark(linkMarkers[i].transform.GetChild(1), link.CellB, link.DirectionFromA.Opposite());
                }
                shownLinks = links;
            }
            for (var i = 0; i < linkMarkers.Count; i++)
                linkMarkers[i].SetActive(i < links.Count);
        }

        private void PositionEdgeMark(Transform mark, Vector3Int cell, GridDirection outward)
        {
            mark.localPosition = (IsometricProjection.Edge(cell, outward) +
                Vector3.up * .035f - (Vector3)outward.ToOffset() * .03f) * level.CellSize;
            mark.localRotation = Quaternion.LookRotation((Vector3)outward.ToOffset());
        }
    }
}
