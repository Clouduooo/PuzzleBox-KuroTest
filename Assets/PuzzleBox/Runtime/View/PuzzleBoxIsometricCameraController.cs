using System;
using PuzzleBox.Data;
using UnityEngine;

namespace PuzzleBox.View
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class PuzzleBoxIsometricCameraController : MonoBehaviour
    {
        [SerializeField, Min(30f)] private float rotationSpeed = 360f;
        [SerializeField, Range(0.02f, 0.5f)] private float zoomStep = 0.12f;
        [SerializeField, Range(0.25f, 1f)] private float minimumZoomRatio = 0.55f;
        [SerializeField, Range(1f, 3f)] private float maximumZoomRatio = 1.8f;

        private Camera controlledCamera;
        private Vector3 focusPoint;
        private float focusDistance;
        private float baseYaw;
        private float currentYaw;
        private float targetYaw;
        private float targetSize;
        private float zoomVelocity;
        private float baseSize;
        private bool initialized;

        /// <summary>Clockwise 90-degree camera turns from the theme's base yaw, in the range 0..3.</summary>
        public int QuarterTurns { get; private set; }
        public float TargetOrthographicSize => targetSize;
        public bool IsRotating => initialized && Mathf.Abs(targetYaw - currentYaw) > .05f;
        public event Action<int> QuarterTurnChanged;

        public void Initialize(LevelDefinition level)
        {
            if (level == null || level.ArtTheme == null) return;
            controlledCamera = GetComponent<Camera>();
            var theme = level.ArtTheme;
            theme.ConfigureCamera(controlledCamera, level.Size, level.CellSize);
            focusPoint = theme.GetFocusPoint(level.Size, level.CellSize);
            focusDistance = Vector3.Distance(controlledCamera.transform.position, focusPoint);
            baseYaw = level.PerspectiveLinks.Count > 0 || level.Boxes.Count > 0 ? 45f : theme.cameraAngles.y;
            currentYaw = targetYaw = baseYaw + level.InitialViewQuarter * 90f;
            baseSize = targetSize = controlledCamera.orthographicSize;
            QuarterTurns = level.InitialViewQuarter;
            zoomVelocity = 0f;
            initialized = true;
            ApplyTransform();
        }

        public void ReadInput()
        {
            if (!initialized) return;
            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > .001f) ZoomBy(scroll);
        }

        public void RotateQuarter(int turns)
        {
            if (!initialized || turns == 0) return;
            QuarterTurns = ((QuarterTurns + turns) % 4 + 4) % 4;
            // Keep an accumulated target so repeated inputs during an animation never reverse at 0/360.
            targetYaw += turns * 90f;
            QuarterTurnChanged?.Invoke(QuarterTurns);
        }

        public void SetViewQuarter(int quarter, bool immediate = false)
        {
            if (!initialized) return;
            quarter = IsometricProjection.Normalize(quarter);
            targetYaw = currentYaw + Mathf.DeltaAngle(currentYaw, baseYaw + quarter * 90f);
            QuarterTurns = quarter;
            if (immediate) { currentYaw = targetYaw; ApplyTransform(); }
            QuarterTurnChanged?.Invoke(quarter);
        }

        public void ZoomBy(float scrollSteps)
        {
            if (!initialized || Mathf.Abs(scrollSteps) < .001f) return;
            targetSize *= Mathf.Exp(-scrollSteps * zoomStep);
            targetSize = Mathf.Clamp(targetSize, baseSize * minimumZoomRatio, baseSize * maximumZoomRatio);
        }

        public GridDirection ViewToWorldDirection(GridDirection viewDirection)
        {
            return RotateDirection(viewDirection, QuarterTurns);
        }

        public static GridDirection RotateDirection(GridDirection direction, int clockwiseQuarterTurns)
        {
            var turns = ((clockwiseQuarterTurns % 4) + 4) % 4;
            return (GridDirection)(((int)direction + turns) % 4);
        }

        private void LateUpdate()
        {
            if (!initialized) return;
            currentYaw = Mathf.MoveTowards(currentYaw, targetYaw, rotationSpeed * Time.unscaledDeltaTime);
            controlledCamera.orthographicSize = Mathf.SmoothDamp(controlledCamera.orthographicSize,
                targetSize, ref zoomVelocity, .10f, Mathf.Infinity, Time.unscaledDeltaTime);
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            controlledCamera.orthographic = true;
            controlledCamera.transform.rotation = Quaternion.Euler(ParkTheme.IsometricPitch, currentYaw, 0f);
            controlledCamera.transform.position = focusPoint - controlledCamera.transform.forward * focusDistance;
        }
    }
}
