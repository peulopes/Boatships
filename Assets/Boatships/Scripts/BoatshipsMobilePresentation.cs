using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Boatships
{
    public sealed class BoatshipsMobilePresentation : MonoBehaviour
    {
        [SerializeField] private Canvas canvas;
        [SerializeField] private OceanWaves ocean;
        [SerializeField] private WeaponGroupCombat weapons;
        [SerializeField] private ShipCombatTarget player;
        private Rect safeArea;
        private Vector2 screenSize;
        private AudioSource ambience;
        private AudioSource effects;
        private AudioClip seaClip;
        private AudioClip cannonClip;
        private AudioClip clickClip;
        private RenderPipelineAsset previousPipeline;
        private UniversalRenderPipelineAsset pipeline;
        public RectTransform SafeRoot { get; private set; }
        public Canvas Canvas => canvas;
        public int Quality { get; private set; }
        public int FrameRate { get; private set; }
        public float Volume { get; private set; }
        public float AverageFrameMilliseconds { get; private set; }
        /// <summary>PC frame-rate caps; 0 means uncapped.</summary>
        private static readonly int[] FrameRates = { 30, 60, 120, 0 };
        private const string FrameRateKey = "Boatships.FPS.PC";

        public void Configure(Canvas ui, OceanWaves waves, WeaponGroupCombat guns, ShipCombatTarget flagship)
        { canvas = ui; ocean = waves; weapons = guns; player = flagship; }

        private IEnumerator Start()
        {
            Quality = Mathf.Clamp(PlayerPrefs.GetInt("Boatships.Quality", 1), 0, 2);
            FrameRate = PlayerPrefs.GetInt(FrameRateKey, 60);
            if (System.Array.IndexOf(FrameRates, FrameRate) < 0) FrameRate = 60;
            Volume = Mathf.Clamp01(PlayerPrefs.GetFloat("Boatships.Volume", 0.6f));
            previousPipeline = QualitySettings.renderPipeline;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset source)
            {
                pipeline = Instantiate(source);
                QualitySettings.renderPipeline = pipeline;
            }
            ApplySettings();
            CreateAudio();
            if (weapons != null) weapons.GroupFired += OnFire;
            if (player != null) player.HullImpactReceived += OnImpact;
            // Existing prototype UI resolves its paths during Start before reparenting.
            for (int i = 0; i < 3; i++) yield return null;
            GameObject root = new GameObject("Safe Area", typeof(RectTransform));
            SafeRoot = root.GetComponent<RectTransform>();
            SafeRoot.SetParent(canvas.transform, false);
            var children = new System.Collections.Generic.List<Transform>();
            foreach (Transform child in canvas.transform) if (child != SafeRoot) children.Add(child);
            foreach (Transform child in children) child.SetParent(SafeRoot, false);
            ApplySafeArea();
        }

        private void Update()
        {
            AverageFrameMilliseconds = Mathf.Lerp(AverageFrameMilliseconds, Time.unscaledDeltaTime * 1000f, 0.05f);
            if (SafeRoot != null && (safeArea != Screen.safeArea || screenSize != new Vector2(Screen.width, Screen.height)))
                ApplySafeArea();
        }

        private void ApplySafeArea()
        {
            safeArea = Screen.safeArea;
            screenSize = new Vector2(Screen.width, Screen.height);
            SafeRoot.anchorMin = new Vector2(safeArea.xMin / Screen.width, safeArea.yMin / Screen.height);
            SafeRoot.anchorMax = new Vector2(safeArea.xMax / Screen.width, safeArea.yMax / Screen.height);
            SafeRoot.offsetMin = SafeRoot.offsetMax = Vector2.zero;
        }

        public void CycleQuality() { Quality = (Quality + 1) % 3; ApplySettings(); }
        public void CycleFrameRate()
        {
            FrameRate = FrameRates[(System.Array.IndexOf(FrameRates, FrameRate) + 1) % FrameRates.Length];
            ApplySettings();
        }
        public void SetVolume(float value) { Volume = Mathf.Clamp01(value); ApplySettings(); }
        public void Click() { if (effects != null) effects.PlayOneShot(clickClip, 0.3f); }

        private void ApplySettings()
        {
            Application.targetFrameRate = FrameRate > 0 ? FrameRate : -1;
            // "0" used to mean uncapped with V-Sync off, rendering hundreds of frames nobody sees; it now follows
            // the display's refresh rate instead.
            QualitySettings.vSyncCount = FrameRate > 0 ? 0 : 1;
            QualitySettings.shadowDistance = Quality == 0 ? 0f : Quality == 1 ? 45f : 85f;
            if (pipeline != null)
            {
                pipeline.renderScale = Quality == 0 ? 0.7f : Quality == 1 ? 0.85f : 1f;
                // Shadow cost scales with cascades: the low-poly scene reads fine with fewer.
                pipeline.shadowDistance = QualitySettings.shadowDistance;
                pipeline.shadowCascadeCount = Quality == 0 ? 1 : Quality == 1 ? 2 : 4;
            }
            // Soft shadow filtering only at High; hard shadows are much cheaper to sample.
            Light sun = RenderSettings.sun;
            if (sun == null)
                foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (light.type == LightType.Directional) { sun = light; break; }
            if (sun != null)
                sun.shadows = Quality == 0 ? LightShadows.None : Quality == 1 ? LightShadows.Hard : LightShadows.Soft;
            if (ocean != null)
            {
                ocean.SetResolution(Quality == 0 ? 24 : Quality == 1 ? 36 : 48);
                ocean.MeshUpdateInterval = Quality == 0 ? 1f / 20f : 0f;
            }
            if (ambience != null) ambience.volume = Volume * 0.25f;
            if (effects != null) effects.volume = Volume;
            PlayerPrefs.SetInt("Boatships.Quality", Quality);
            PlayerPrefs.SetInt(FrameRateKey, FrameRate);
            PlayerPrefs.SetFloat("Boatships.Volume", Volume);
            PlayerPrefs.Save();
        }

        private void CreateAudio()
        {
            seaClip = MakeClip("Ocean ambience", 4f, 0);
            cannonClip = MakeClip("Cannon report", 0.45f, 1);
            clickClip = MakeClip("Menu click", 0.07f, 2);
            ambience = gameObject.AddComponent<AudioSource>();
            ambience.clip = seaClip; ambience.loop = true; ambience.spatialBlend = 0f;
            ambience.volume = Volume * 0.25f; ambience.Play();
            effects = gameObject.AddComponent<AudioSource>();
            effects.spatialBlend = 0f; effects.volume = Volume;
        }

        private static AudioClip MakeClip(string name, float seconds, int kind)
        {
            const int sampleRate = 22050;
            float[] data = new float[Mathf.RoundToInt(seconds * sampleRate)];
            var random = new System.Random(142 + kind);
            float smooth = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)sampleRate;
                smooth = smooth * 0.95f + ((float)random.NextDouble() * 2f - 1f) * 0.05f;
                float envelope = kind == 0 ? 0.65f + Mathf.Sin(t / seconds * Mathf.PI * 2f) * 0.25f :
                    Mathf.Exp(-t * (kind == 1 ? 11f : 65f));
                data[i] = (kind == 2 ? Mathf.Sin(t * 440f * Mathf.PI * 2f) * 0.3f : smooth * 2f) * envelope;
            }
            AudioClip clip = AudioClip.Create(name, data.Length, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private void OnFire(int count) { if (effects != null) effects.PlayOneShot(cannonClip, 0.7f); }
        private void OnImpact(Vector3 point, float damage) { if (effects != null) effects.PlayOneShot(cannonClip, 0.4f); }

        private void OnDestroy()
        {
            if (weapons != null) weapons.GroupFired -= OnFire;
            if (player != null) player.HullImpactReceived -= OnImpact;
            if (pipeline != null) { QualitySettings.renderPipeline = previousPipeline; Destroy(pipeline); }
            if (seaClip != null) Destroy(seaClip);
            if (cannonClip != null) Destroy(cannonClip);
            if (clickClip != null) Destroy(clickClip);
        }
    }
}
