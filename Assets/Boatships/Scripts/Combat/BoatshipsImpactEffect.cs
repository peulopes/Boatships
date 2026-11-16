using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Boatships
{
    /// <summary>Short lived world-space cannon impact visuals, without debris colliders.</summary>
    public sealed class BoatshipsImpactEffect : MonoBehaviour
    {
        private static int activeEffects;
        private Material explosionMaterial;

        public static void Spawn(VoxelHull hull, Vector3 point, List<Vector3> fragments)
        {
            // Bound the work when several broadsides land together.
            if (activeEffects >= 24) return;
            GameObject root = new GameObject("Cannon Impact");
            root.transform.position = point;
            BoatshipsImpactEffect effect = root.AddComponent<BoatshipsImpactEffect>();
            activeEffects++;
            effect.Build(hull, point, fragments);
            Destroy(root, 3.5f);
        }

        private void Build(VoxelHull hull, Vector3 point, List<Vector3> fragments)
        {
            // A referenced material keeps the particle shader available in player builds too.
            Material template = Resources.Load<Material>("Boatships/CannonImpact");
            Shader shader = template != null ? template.shader : Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader != null)
            {
                explosionMaterial = template != null ? new Material(template) : new Material(shader);
                explosionMaterial.name = "Cannon Impact Particles";
                // Configure transparency explicitly: runtime materials start opaque.
                if (explosionMaterial.HasProperty("_Surface")) explosionMaterial.SetFloat("_Surface", 1f);
                if (explosionMaterial.HasProperty("_Mode")) explosionMaterial.SetFloat("_Mode", 2f);
                explosionMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                explosionMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                explosionMaterial.SetInt("_ZWrite", 0);
                explosionMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                explosionMaterial.EnableKeyword("_ALPHABLEND_ON");
                explosionMaterial.renderQueue = (int)RenderQueue.Transparent;
                ParticleSystem blast = CreateParticles("Fire and smoke", explosionMaterial, PrimitiveType.Sphere, 64, 0.15f);
                var size = blast.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.25f, 1f, 2.5f));
                var color = blast.colorOverLifetime;
                color.enabled = true;
                Gradient gradient = new Gradient();
                gradient.SetKeys(new[] {
                    new GradientColorKey(new Color(1f, 0.85f, 0.25f), 0f),
                    new GradientColorKey(new Color(1f, 0.25f, 0.03f), 0.15f),
                    new GradientColorKey(new Color(0.15f, 0.15f, 0.15f), 0.5f),
                    new GradientColorKey(new Color(0.3f, 0.3f, 0.3f), 1f)
                }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                color.color = gradient;
                for (int i = 0; i < 28; i++)
                {
                    Vector3 direction = Random.onUnitSphere;
                    Emit(blast, point, direction * Random.Range(1.5f, 4f) + Vector3.up * 1.5f,
                        Random.Range(0.3f, 0.65f), Random.Range(0.5f, 1.3f));
                }
            }

            if (fragments.Count == 0) return;
            Material hullMaterial = hull.GetComponent<MeshRenderer>().sharedMaterial;
            ParticleSystem debris = CreateParticles("Flying hull fragments", hullMaterial, PrimitiveType.Cube, 48, 1f);
            var shrink = debris.sizeOverLifetime;
            shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0f)));
            var rotation = debris.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-7f, 7f);
            rotation.y = new ParticleSystem.MinMaxCurve(-9f, 9f);
            rotation.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            Vector3 scale = hull.transform.lossyScale;
            float blockSize = hull.Definition.BlockSize * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            foreach (Vector3 fragment in fragments)
            {
                Vector3 outward = fragment - point;
                if (outward.sqrMagnitude < 0.001f) outward = Random.onUnitSphere;
                Vector3 velocity = outward.normalized * Random.Range(3f, 7f) + Vector3.up * Random.Range(3f, 6f);
                Emit(debris, fragment, velocity, blockSize * Random.Range(0.35f, 0.75f), Random.Range(1.8f, 3f));
            }
        }

        private ParticleSystem CreateParticles(string title, Material material, PrimitiveType shape, int capacity, float gravity)
        {
            GameObject child = new GameObject(title);
            child.transform.SetParent(transform, false);
            ParticleSystem particles = child.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = capacity;
            main.gravityModifier = gravity;
            main.startRotation3D = true;
            var emission = particles.emission;
            emission.enabled = false;
            var emitter = particles.shape;
            emitter.enabled = false;
            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            GameObject template = GameObject.CreatePrimitive(shape);
            template.SetActive(false);
            renderer.mesh = template.GetComponent<MeshFilter>().sharedMesh;
            Destroy(template);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            particles.Play();
            return particles;
        }

        private static void Emit(ParticleSystem particles, Vector3 point, Vector3 velocity, float size, float life)
        {
            particles.Emit(new ParticleSystem.EmitParams {
                position = point, velocity = velocity, startSize = size, startLifetime = life,
                rotation3D = Random.insideUnitSphere * 180f, startColor = Color.white
            }, 1);
        }

        private void OnDestroy()
        {
            activeEffects = Mathf.Max(0, activeEffects - 1);
            if (explosionMaterial != null) Destroy(explosionMaterial);
        }
    }
}
