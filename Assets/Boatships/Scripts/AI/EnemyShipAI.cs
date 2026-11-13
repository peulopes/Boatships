using UnityEngine;

namespace Boatships
{
    /// <summary>
    /// Single-enemy naval AI for Milestone 5. Reuses OceanWaves, VoxelHull,
    /// ShipCombatTarget and BoatshipsProjectile from the existing project.
    /// This prototype uses a kinematic Rigidbody and does not simulate sinking.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class EnemyShipAI : MonoBehaviour
    {
        public enum EnemyState { Waiting, Pursuing, Broadside, Repositioning, Retreating, Defeated }

        [SerializeField] private ShipCombatTarget player;
        [SerializeField] private ShipCombatTarget self;
        [SerializeField] private OceanWaves ocean;
        [SerializeField] private ShipCustomization playerShipyard;
        [SerializeField] private Transform visualPivot;
        [SerializeField] private Transform[] portMuzzles;
        [SerializeField] private Transform[] starboardMuzzles;
        [SerializeField] private Material cannonballMaterial;

        [Header("Navigation")]
        [SerializeField, Min(1f)] private float detectionRange = 38f;
        [SerializeField, Min(1f)] private float approachRange = 16f;
        [SerializeField, Min(1f)] private float closeRange = 8f;
        [SerializeField, Min(0f)] private float speed = 5.5f;
        [SerializeField, Min(1f)] private float turnRate = 85f;
        [SerializeField, Range(0.05f, 0.9f)] private float retreatAtHullFraction = 0.28f;
        [SerializeField] private float heightAboveWaves = 0.55f;

        [Header("Cannon Battery")]
        [SerializeField, Min(1f)] private float muzzleSpeed = 22f;
        [SerializeField, Min(1f)] private float shotGravity = 18f;
        [SerializeField, Min(0.2f)] private float reloadSeconds = 3.5f;
        [SerializeField, Min(0f)] private float damage = 24f;
        [SerializeField, Min(0.05f)] private float impactRadius = 0.38f;
        [SerializeField, Min(1f)] private float fireRange = 22f;
        [SerializeField, Range(5f, 65f)] private float broadsideTolerance = 28f;

        private Rigidbody body;
        private float[] portReady;
        private float[] starboardReady;
        private Vector3 previousTargetPosition;
        private Vector3 targetVelocity;
        private bool targetPositionSet;
        private int preferredSide = 1; // 1 starboard, -1 port; selected when entering range.
        private EnemyState state;
        private float nextFireAllowedAt;

        public EnemyState State => state;
        public float HullFraction => self != null && self.TotalVoxels > 0
            ? (float)self.RemainingVoxels / self.TotalVoxels : 0f;

        public void Configure(ShipCombatTarget playerShip, ShipCombatTarget enemyShip,
            OceanWaves waves, ShipCustomization playerCustomization, Transform shipVisuals,
            Transform[] portCannons, Transform[] starboardCannons, Material projectileMaterial)
        {
            player = playerShip;
            self = enemyShip;
            ocean = waves;
            playerShipyard = playerCustomization;
            visualPivot = shipVisuals;
            portMuzzles = portCannons;
            starboardMuzzles = starboardCannons;
            cannonballMaterial = projectileMaterial;
            SetupReloadArrays();
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            SetupReloadArrays();
        }

        private void SetupReloadArrays()
        {
            portReady = new float[portMuzzles != null ? portMuzzles.Length : 0];
            starboardReady = new float[starboardMuzzles != null ? starboardMuzzles.Length : 0];
        }

        private void FixedUpdate()
        {
            if (body == null || player == null || self == null || ocean == null) return;
            if (playerShipyard != null && playerShipyard.InShipyard)
            {
                state = EnemyState.Waiting;
                return;
            }
            if (self.RemainingVoxels <= 0)
            {
                state = EnemyState.Defeated;
                return;
            }
            if (player.RemainingVoxels <= 0)
            {
                state = EnemyState.Waiting;
                return;
            }

            Vector3 toPlayer = player.transform.position - body.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;
            float dt = Time.fixedDeltaTime;
            TrackPlayerVelocity(dt);

            if (distance > detectionRange)
            {
                state = EnemyState.Waiting;
                FollowOcean(dt, 0f, body.rotation.eulerAngles.y);
                return;
            }

            float targetHeading = Mathf.Atan2(toPlayer.x, toPlayer.z) * Mathf.Rad2Deg;
            float course = targetHeading;
            float throttle = 0f;
            bool inAttackBand = distance >= closeRange && distance <= approachRange;

            if (HullFraction < retreatAtHullFraction)
            {
                state = EnemyState.Retreating;
                course = targetHeading + 180f;
                throttle = speed;
            }
            else if (distance > approachRange)
            {
                state = EnemyState.Pursuing;
                course = targetHeading;
                throttle = speed;
            }
            else if (distance < closeRange)
            {
                state = EnemyState.Repositioning;
                course = targetHeading + 180f;
                throttle = speed * 0.75f;
            }
            else
            {
                if (state != EnemyState.Broadside)
                {
                    float starboardCourse = targetHeading - 90f;
                    float portCourse = targetHeading + 90f;
                    preferredSide = Mathf.Abs(Mathf.DeltaAngle(body.rotation.eulerAngles.y, starboardCourse)) <=
                        Mathf.Abs(Mathf.DeltaAngle(body.rotation.eulerAngles.y, portCourse)) ? 1 : -1;
                }
                state = EnemyState.Broadside;
                course = targetHeading + (preferredSide == 1 ? -90f : 90f);
                throttle = speed * 0.15f;
            }

            FollowOcean(dt, throttle, course);
            if (inAttackBand && state == EnemyState.Broadside && Time.time >= nextFireAllowedAt)
                TryFireBroadside(distance);
        }

        private void TrackPlayerVelocity(float dt)
        {
            Vector3 now = player.transform.position;
            if (targetPositionSet && dt > 0f)
            {
                Vector3 sample = (now - previousTargetPosition) / dt;
                sample.y = 0f;
                // Smooth rapid direction changes so shots remain fair on mobile.
                targetVelocity = Vector3.Lerp(targetVelocity, sample, 0.2f);
            }
            previousTargetPosition = now;
            targetPositionSet = true;
        }

        private void FollowOcean(float dt, float forwardSpeed, float heading)
        {
            float yaw = Mathf.MoveTowardsAngle(body.rotation.eulerAngles.y, heading, turnRate * dt);
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 p = body.position + (rotation * Vector3.forward) * forwardSpeed * dt;
            float targetY = ocean.SampleHeight(p.x, p.z, Time.fixedTime) + heightAboveWaves;
            p.y = Mathf.Lerp(p.y, targetY, 1f - Mathf.Exp(-6f * dt));
            body.MoveRotation(rotation);
            body.MovePosition(p);
        }

        private void TryFireBroadside(float distance)
        {
            if (distance > fireRange) return;
            Vector3 towardPlayer = player.transform.position - transform.position;
            towardPlayer.y = 0f;
            if (towardPlayer.sqrMagnitude < 0.01f) return;

            Vector3 broadsideDirection = transform.right * preferredSide;
            float angle = Vector3.Angle(broadsideDirection, towardPlayer);
            if (angle > broadsideTolerance) return;

            Transform[] muzzles = preferredSide == 1 ? starboardMuzzles : portMuzzles;
            float[] ready = preferredSide == 1 ? starboardReady : portReady;
            if (muzzles == null || ready == null) return;

            int shots = 0;
            for (int i = 0; i < muzzles.Length && i < ready.Length; i++)
            {
                Transform muzzle = muzzles[i];
                if (muzzle == null || Time.time < ready[i]) continue;
                if (!TryFindBallisticVelocity(muzzle.position, out Vector3 velocity)) continue;

                GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.name = "Enemy Cannonball";
                ball.transform.position = muzzle.position;
                ball.transform.localScale = Vector3.one * 0.22f;
                if (cannonballMaterial != null)
                    ball.GetComponent<MeshRenderer>().sharedMaterial = cannonballMaterial;
                Collider collider = ball.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                BoatshipsProjectile projectile = ball.AddComponent<BoatshipsProjectile>();
                projectile.Launch(velocity, self, ocean, damage, impactRadius, shotGravity);
                ready[i] = Time.time + reloadSeconds;
                shots++;
            }
            if (shots > 0) nextFireAllowedAt = Time.time + 0.15f;
        }

        private bool TryFindBallisticVelocity(Vector3 muzzle, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            Vector3 delta = player.transform.position - muzzle;
            Vector3 flat = Vector3.ProjectOnPlane(delta, Vector3.up);
            float d = flat.magnitude;
            if (d < 0.1f || d > fireRange) return false;

            // Approximate moving-target lead, capped to avoid overly precise AI.
            float flightTime = d / Mathf.Max(1f, muzzleSpeed * 0.88f);
            Vector3 prediction = player.transform.position +
                Vector3.ClampMagnitude(targetVelocity * flightTime * 0.7f, 2.5f);
            delta = prediction - muzzle;
            flat = Vector3.ProjectOnPlane(delta, Vector3.up);
            d = flat.magnitude;
            if (d < 0.1f) return false;

            float v2 = muzzleSpeed * muzzleSpeed;
            float g = shotGravity;
            float discriminant = v2 * v2 - g * (g * d * d + 2f * delta.y * v2);
            if (discriminant < 0f) return false;
            float tangent = (v2 - Mathf.Sqrt(discriminant)) / (g * d);
            float elevation = Mathf.Atan(tangent);
            if (elevation < -0.06f || elevation > 0.80f) return false;
            Vector3 horizontalDirection = flat / d;
            velocity = muzzleSpeed * (horizontalDirection * Mathf.Cos(elevation) +
                Vector3.up * Mathf.Sin(elevation));
            return true;
        }

        private void LateUpdate()
        {
            if (visualPivot == null || ocean == null) return;
            Vector3 p = transform.position;
            Vector3 f = transform.forward * 1.5f;
            Vector3 r = transform.right * 1.5f;
            float t = Time.time;
            float pitch = -Mathf.Atan2(
                ocean.SampleHeight(p.x + f.x, p.z + f.z, t) -
                ocean.SampleHeight(p.x - f.x, p.z - f.z, t), 3f) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2(
                ocean.SampleHeight(p.x + r.x, p.z + r.z, t) -
                ocean.SampleHeight(p.x - r.x, p.z - r.z, t), 3f) * Mathf.Rad2Deg;
            Quaternion tilt = Quaternion.Euler(Mathf.Clamp(pitch, -12f, 12f), 0f,
                Mathf.Clamp(roll, -12f, 12f));
            visualPivot.localRotation = Quaternion.Slerp(visualPivot.localRotation, tilt,
                1f - Mathf.Exp(-5f * Time.deltaTime));
        }
    }
}
