using UnityEngine;
using UnityEngine.Rendering;

namespace PuzzleBox.View
{
    /// <summary>Small mesh-particle accents that share the park palette instead of using billboard textures.</summary>
    public sealed class PuzzleBoxMotionEffects : MonoBehaviour
    {
        private Material hopMaterial;
        private Material pushMaterial;
        private Mesh sphereMesh;

        public void Configure(Material whitePuffMaterial, Material parcelPuffMaterial)
        {
            hopMaterial = whitePuffMaterial;
            pushMaterial = parcelPuffMaterial != null ? parcelPuffMaterial : whitePuffMaterial;
            sphereMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        }

        public void PlayHopPuff(Vector3 worldPosition, Vector3 travelDirection, float cellSize, bool landing)
        {
            if (hopMaterial == null || sphereMesh == null) return;
            var particles = landing ? 5 : 4;
            var direction = HorizontalDirection(travelDirection);
            var system = CreateSystem("Cat hop · white puffs", hopMaterial);
            for (var i = 0; i < particles; i++)
            {
                var angle = (i / (float)particles) * Mathf.PI * 2f + (landing ? .35f : 0f);
                var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var velocity = (radial * (landing ? .34f : .24f) - direction * .13f + Vector3.up * .20f) * cellSize;
                Emit(system, worldPosition + radial * .05f * cellSize, velocity,
                    (.075f + (i % 2) * .025f) * cellSize, .30f + i * .018f);
            }
            system.Play();
        }

        public void PlayPushPuff(Vector3 worldPosition, Vector3 pushDirection, float cellSize)
        {
            if (pushMaterial == null || sphereMesh == null) return;
            var direction = HorizontalDirection(pushDirection);
            var side = Vector3.Cross(Vector3.up, direction);
            var system = CreateSystem("Parcel push · warm puffs", pushMaterial);
            for (var i = 0; i < 7; i++)
            {
                var sideSign = i % 2 == 0 ? -1f : 1f;
                var spread = side * sideSign * (.12f + (i % 3) * .07f);
                var velocity = (-direction * (.22f + i * .025f) + spread + Vector3.up * (.12f + (i % 3) * .07f)) * cellSize;
                Emit(system, worldPosition + side * sideSign * .08f * cellSize,
                    velocity, (.065f + (i % 3) * .018f) * cellSize, .34f + i * .018f);
            }
            system.Play();
        }

        private ParticleSystem CreateSystem(string label, Material material)
        {
            var go = new GameObject(label) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, true);
            var system = go.AddComponent<ParticleSystem>();
            // AddComponent starts the default system immediately in Play Mode.
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = .1f;
            main.startSpeed = 0f;
            main.startLifetime = .4f;
            main.startSize = .1f;
            main.gravityModifier = .18f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = sphereMesh;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        private static void Emit(ParticleSystem system, Vector3 position, Vector3 velocity, float size, float lifetime)
        {
            var emit = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startSize = size,
                startLifetime = lifetime,
                startColor = Color.white
            };
            system.Emit(emit, 1);
        }

        private static Vector3 HorizontalDirection(Vector3 value)
        {
            value.y = 0f;
            return value.sqrMagnitude < .0001f ? Vector3.forward : value.normalized;
        }
    }
}
