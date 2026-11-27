using UnityEngine;

namespace Boatships
{
    /// <summary>
    /// One of the flagship's installed cannons, seen as a post the captain can take command of in first person.
    /// While the player holds it, its gun crew stands aside: the player aims (mouse) and fires (click);
    /// the crew still loads it. Added to each cannon by <see cref="WeaponGroupCombat"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShipGunStation : MonoBehaviour, IMannedGun
    {
        private static Material arcMaterial;
        private WeaponGroupCombat battery;
        private Transform barrel;
        private LineRenderer arc;
        private readonly Vector3[] arcPoints = new Vector3[26];

        public float AimYaw { get; set; }
        public float AimPitch { get; set; } = 8f;
        public bool Ready => battery != null && battery.IsLoaded(transform);
        public bool Manned => BoatshipsDeckFighter.Player != null && ReferenceEquals(BoatshipsDeckFighter.Player.Mounted, this);
        public Vector3 WorldPosition => transform.position;

        private float MountYaw => transform.localEulerAngles.y;
        private Transform Hull => transform.parent != null ? transform.parent : transform;

        /// <summary>World direction the barrel points while the player aims it.</summary>
        public Vector3 AimDirection =>
            Hull.rotation * (Quaternion.Euler(-AimPitch, MountYaw + AimYaw, 0f) * Vector3.forward);

        public Vector3 ViewPosition
        {
            get
            {
                Vector3 flat = Vector3.ProjectOnPlane(AimDirection, Vector3.up).normalized;
                return transform.position + Vector3.up * 0.55f - flat * 0.7f;
            }
        }

        public Quaternion ViewRotation => Quaternion.LookRotation(AimDirection, Vector3.up) * Quaternion.Euler(6f, 0f, 0f);

        public void Bind(WeaponGroupCombat owner, Transform barrelPivot)
        {
            battery = owner;
            barrel = barrelPivot;
        }

        public bool Fire() => battery != null && battery.FireStation(this);

        private void LateUpdate()
        {
            bool manned = Manned;
            if (manned)
            {
                AimYaw = Mathf.Clamp(AimYaw, -55f, 55f);
                AimPitch = Mathf.Clamp(AimPitch, -5f, 40f);
                if (barrel != null) barrel.localRotation = Quaternion.Euler(90f - AimPitch, AimYaw, 0f);
            }
            if (manned && arc == null) CreateArc();
            if (arc == null) return;
            arc.enabled = manned && battery != null;
            if (!arc.enabled) return;
            Vector3 direction = AimDirection;
            Vector3 position = battery.MuzzlePoint(transform, direction);
            Vector3 velocity = direction * battery.MuzzleSpeed;
            for (int i = 0; i < arcPoints.Length; i++)
            {
                arcPoints[i] = position;
                position += velocity * 0.07f;
                velocity += Vector3.down * battery.ShotGravity * 0.07f;
            }
            arc.SetPositions(arcPoints);
        }

        private void CreateArc()
        {
            if (arcMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                if (shader == null) return;
                arcMaterial = new Material(shader) { color = new Color(0.4f, 1f, 1f, 0.8f) };
            }
            GameObject line = new GameObject("Gun Aim Arc");
            line.transform.SetParent(transform, false);
            arc = line.AddComponent<LineRenderer>();
            arc.sharedMaterial = arcMaterial;
            arc.useWorldSpace = true;
            arc.positionCount = arcPoints.Length;
            arc.widthMultiplier = 0.05f;
        }
    }
}
