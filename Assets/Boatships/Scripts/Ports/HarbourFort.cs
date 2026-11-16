using System.Collections.Generic;
using UnityEngine;

namespace Boatships
{
    /// <summary>A neutral coastal battery that retaliates against ships firing near its harbour.</summary>
    [DisallowMultipleComponent]
    public sealed class HarbourFort : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float protectedRadius = 45f;
        [SerializeField, Min(1f)] private float engagementRange = 85f;
        [SerializeField, Min(0.2f)] private float reloadSeconds = 2.2f;
        [SerializeField, Min(1f)] private float muzzleSpeed = 42f;
        [SerializeField, Min(0.1f)] private float gravity = 18f;
        [SerializeField, Min(1f)] private float damage = 120f;
        [SerializeField, Min(0.05f)] private float impactRadius = 0.65f;
        private readonly HashSet<ShipCombatTarget> aggressors = new HashSet<ShipCombatTarget>();
        private readonly List<ShipCombatTarget> stale = new List<ShipCombatTarget>();
        private Transform[] muzzles;
        private Material cannonballMaterial;
        private OceanWaves ocean;
        private Vector3 protectedCentre;
        private bool configured;
        private float nextFireAt;
        private int nextCannon;

        public ShipCombatTarget CurrentTarget { get; private set; }
        public int AggressorCount => aggressors.Count;
        public int ShotsFired { get; private set; }
        public float ProtectedRadius => protectedRadius;
        public float EngagementRange => engagementRange;
        public Vector3 ProtectedCentre => protectedCentre;

        public void Configure(Vector3 harbourCentre, Transform[] cannonMuzzles, OceanWaves waves)
        {
            protectedCentre = harbourCentre;
            muzzles = cannonMuzzles;
            ocean = waves;
            cannonballMaterial = Resources.Load<Material>("Boatships/FortCannonball");
            configured = true;
        }

        private void OnEnable()
        {
            BoatshipsProjectile.ShipFired += ObserveGunfire;
            BoatshipsProjectile.ShotImpacted += ObserveGunfire;
        }

        private void OnDisable()
        {
            BoatshipsProjectile.ShipFired -= ObserveGunfire;
            BoatshipsProjectile.ShotImpacted -= ObserveGunfire;
            aggressors.Clear();
            CurrentTarget = null;
        }

        private static float DistanceSquared(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return x * x + z * z;
        }

        private bool CanEngage(ShipCombatTarget ship)
        {
            if (ship == null || !ship.gameObject.activeInHierarchy || ship.RemainingVoxels <= 0) return false;
            if (DistanceSquared(ship.transform.position, transform.position) > engagementRange * engagementRange) return false;
            ShipFlooding flooding = ship.GetComponent<ShipFlooding>();
            return flooding == null || !flooding.IsSinking;
        }

        private void ObserveGunfire(ShipCombatTarget shooter, Vector3 position)
        {
            if (!configured || !CanEngage(shooter) ||
                DistanceSquared(position, protectedCentre) > protectedRadius * protectedRadius) return;
            // Fort cannonballs have no ship owner, so batteries cannot provoke one another.
            if (aggressors.Count == 0) nextFireAt = Time.time + 0.75f;
            aggressors.Add(shooter);
        }

        private void FixedUpdate()
        {
            if (!configured) return;
            stale.Clear();
            CurrentTarget = null;
            float closest = float.PositiveInfinity;
            foreach (ShipCombatTarget ship in aggressors)
            {
                if (!CanEngage(ship)) { stale.Add(ship); continue; }
                float distance = DistanceSquared(ship.transform.position, transform.position);
                if (distance >= closest) continue;
                closest = distance;
                CurrentTarget = ship;
            }
            foreach (ShipCombatTarget ship in stale) aggressors.Remove(ship);
            if (CurrentTarget == null || Time.time < nextFireAt || muzzles == null || muzzles.Length == 0) return;
            Transform muzzle = muzzles[nextCannon++ % muzzles.Length];
            if (muzzle == null) return;
            // The whole cannon pivot follows the ballistic elevation and bearing.
            if (!TryAim(muzzle.position, CurrentTarget, false, out Vector3 velocity) &&
                !TryAim(muzzle.position, CurrentTarget, true, out velocity)) return;
            muzzle.parent.rotation = Quaternion.LookRotation(velocity);
            if (!TryAim(muzzle.position, CurrentTarget, false, out velocity) &&
                !TryAim(muzzle.position, CurrentTarget, true, out velocity)) return;
            muzzle.parent.rotation = Quaternion.LookRotation(velocity);
            BoatshipsProjectile projectile = BoatshipsProjectile.Spawn(muzzle.position, cannonballMaterial);
            projectile.Launch(velocity, null, ocean, damage, impactRadius, gravity);
            ShotsFired++;
            nextFireAt = Time.time + reloadSeconds;
        }

        private bool TryAim(Vector3 muzzle, ShipCombatTarget target, bool highArc, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            HullDefinition definition = target.Hull.Definition;
            float alongHull = ((ShotsFired % 3) - 1) * definition.Length * definition.BlockSize * 0.28f;
            Vector3 aim = target.Hull.transform.TransformPoint(new Vector3(0f, 0f, alongHull));
            // Hit low on the hull to create leaks that the existing flooding system can sink.
            aim.y -= definition.BlockSize * 0.25f;
            Rigidbody body = target.GetComponent<Rigidbody>();
            Vector3 movement = body != null ? Vector3.ClampMagnitude(body.linearVelocity, 12f) : Vector3.zero;
            movement.y = 0f;
            float flightTime = Vector3.Distance(muzzle, aim) / muzzleSpeed;
            for (int i = 0; i < 3; i++)
            {
                Vector3 delta = aim + movement * flightTime - muzzle;
                Vector3 horizontal = Vector3.ProjectOnPlane(delta, Vector3.up);
                float distance = horizontal.magnitude;
                if (distance < 0.1f) return false;
                float speedSquared = muzzleSpeed * muzzleSpeed;
                float discriminant = speedSquared * speedSquared -
                    gravity * (gravity * distance * distance + 2f * delta.y * speedSquared);
                if (discriminant < 0f) return false;
                float tangent = (speedSquared + (highArc ? 1f : -1f) * Mathf.Sqrt(discriminant)) / (gravity * distance);
                float elevation = Mathf.Atan(tangent);
                velocity = muzzleSpeed * (horizontal.normalized * Mathf.Cos(elevation) + Vector3.up * Mathf.Sin(elevation));
                flightTime = distance / (muzzleSpeed * Mathf.Cos(elevation));
            }
            if (flightTime > 4.8f) return false;
            // Try a high arc if the low shot would hit the island or an innocent vessel.
            Vector3 previous = muzzle;
            for (int i = 1; i <= 20; i++)
            {
                float t = flightTime * i / 20f;
                Vector3 point = muzzle + velocity * t + Vector3.down * (0.5f * gravity * t * t);
                Vector3 step = point - previous;
                if (Physics.Raycast(previous, step.normalized, out RaycastHit hit, step.magnitude,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    return hit.collider.GetComponentInParent<ShipCombatTarget>() == target;
                previous = point;
            }
            return true;
        }
    }
}
