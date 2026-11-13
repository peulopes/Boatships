using UnityEngine;

namespace Boatships
{
    /// <summary>
    /// Lightweight ballistic projectile. Swept ray queries prevent fast shots from
    /// passing through the target's coarse BoxCollider. No Rigidbody per cannonball.
    /// </summary>
    public sealed class BoatshipsProjectile : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float lifetime = 5f;
        private readonly RaycastHit[] impacts = new RaycastHit[16];
        private Vector3 velocity;
        private ShipCombatTarget owner;
        private OceanWaves ocean;
        private float damage;
        private float radius;
        private float gravity;
        private float remaining;
        private bool launched;

        public void Launch(Vector3 initialVelocity, ShipCombatTarget firingShip,
            OceanWaves waveModel, float hullDamage, float impactRadius, float gravityAcceleration)
        {
            velocity = initialVelocity;
            owner = firingShip;
            ocean = waveModel;
            damage = hullDamage;
            radius = impactRadius;
            gravity = Mathf.Max(0f, gravityAcceleration);
            remaining = lifetime;
            launched = true;
        }

        private void FixedUpdate()
        {
            if (!launched) return;
            float dt = Time.fixedDeltaTime;
            remaining -= dt;
            if (remaining <= 0f) { Destroy(gameObject); return; }

            Vector3 start = transform.position;
            Vector3 nextVelocity = velocity + Vector3.down * (gravity * dt);
            Vector3 destination = start + (velocity + nextVelocity) * (0.5f * dt);
            Vector3 delta = destination - start;
            float distance = delta.magnitude;
            if (distance > 0.0001f)
            {
                int count = Physics.RaycastNonAlloc(start, delta / distance, impacts,
                    distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float closest = distance + 1f;
                ShipCombatTarget receiver = null;
                Vector3 point = destination;
                for (int i = 0; i < count; ++i)
                {
                    ShipCombatTarget candidate = impacts[i].collider.GetComponentInParent<ShipCombatTarget>();
                    if (candidate == owner) continue;
                    if (impacts[i].distance >= closest) continue;
                    closest = impacts[i].distance;
                    receiver = candidate;
                    point = impacts[i].point;
                }
                if (receiver != null)
                {
                    receiver.ReceiveImpact(point, damage, radius);
                    Destroy(gameObject);
                    return;
                }
            }
            transform.position = destination;
            velocity = nextVelocity;
            if (ocean != null && destination.y < ocean.SampleHeight(destination.x, destination.z, Time.time))
                Destroy(gameObject);
        }
    }
}
