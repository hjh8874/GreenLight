using UnityEngine;
using UnityEngine.SceneManagement;

namespace CityFlow.View
{
    internal sealed class VehicleWheelDustSystem : MonoBehaviour
    {
        private const int MaximumParticles = 512;

        private static VehicleWheelDustSystem instance;

        private ParticleSystem particles;
        private ParticleSystemRenderer particleRenderer;
        private int emissionSequence;

        public static VehicleWheelDustSystem GetOrCreate(
            Material material,
            Scene scene)
        {
            if (material == null)
            {
                return null;
            }

            if (instance == null)
            {
                GameObject root =
                    new("Vehicle Wheel Dust System");
                if (scene.IsValid())
                {
                    SceneManager.MoveGameObjectToScene(root, scene);
                }

                instance = root.AddComponent<VehicleWheelDustSystem>();
                instance.Initialize(material);
            }
            else if (instance.particleRenderer.sharedMaterial != material)
            {
                instance.particleRenderer.sharedMaterial = material;
            }

            return instance;
        }

        public void Emit(Vector3 position, float intensity)
        {
            if (particles == null)
            {
                return;
            }

            if (!particles.isPlaying)
            {
                particles.Play(false);
            }

            float clampedIntensity = Mathf.Clamp01(intensity);
            Color color = Color.Lerp(
                new Color(0.76f, 0.72f, 0.64f, 0.38f),
                new Color(0.94f, 0.88f, 0.76f, 0.65f),
                clampedIntensity);

            float phase = emissionSequence * 2.39996323f;
            emissionSequence++;
            Vector2 drift = new(
                Mathf.Cos(phase) * 0.035f,
                Mathf.Sin(phase) * 0.035f);
            var emit = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = new Vector3(drift.x, drift.y, -0.015f),
                startColor = color,
                startLifetime = Mathf.Lerp(0.24f, 0.35f, clampedIntensity),
                startSize = Mathf.Lerp(0.07f, 0.12f, clampedIntensity),
                rotation = phase % (Mathf.PI * 2f)
            };
            particles.Emit(emit, 1);
        }

        private void Initialize(Material material)
        {
            particles = gameObject.AddComponent<ParticleSystem>();
            particleRenderer =
                gameObject.GetComponent<ParticleSystemRenderer>();

            ParticleSystem.MainModule main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = MaximumParticles;
            main.startSpeed = 0f;
            main.startLifetime = 0.3f;
            main.startSize = 0.075f;
            main.scalingMode = ParticleSystemScalingMode.Local;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = false;

            ParticleSystem.SizeOverLifetimeModule size =
                particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(
                1f,
                AnimationCurve.EaseInOut(0f, 0.7f, 1f, 1.35f));

            ParticleSystem.ColorOverLifetimeModule color =
                particles.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(
                CreateFadeGradient());

            ParticleSystem.TextureSheetAnimationModule textureSheet =
                particles.textureSheetAnimation;
            textureSheet.enabled = true;
            textureSheet.mode =
                ParticleSystemAnimationMode.Grid;
            textureSheet.animation =
                ParticleSystemAnimationType.WholeSheet;
            textureSheet.numTilesX = 2;
            textureSheet.numTilesY = 2;
            textureSheet.cycleCount = 1;
            textureSheet.frameOverTime =
                new ParticleSystem.MinMaxCurve(
                    1f,
                    AnimationCurve.Linear(0f, 0f, 1f, 0.999f));

            particleRenderer.sharedMaterial = material;
            particleRenderer.renderMode =
                ParticleSystemRenderMode.Billboard;
            particleRenderer.alignment =
                ParticleSystemRenderSpace.View;
            particleRenderer.sortingFudge = 0.5f;
        }

        private static Gradient CreateFadeGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(0.7f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }
    }
}
