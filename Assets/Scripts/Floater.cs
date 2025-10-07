using UnityEngine;

public class Floater : MonoBehaviour
{
    public Rigidbody rigidBody;
    public float depthBeforeSubmerged = 1f;
    public float displacementAmount = 3f;
    public int floaterCount = 1;

    // Damping in water
    public float waterLinearDrag = 2.0f;
    public float waterAngularDrag = 1.0f;

    void FixedUpdate()
    {
        // 1) gravity shared across floaters (if you use multiple)
        rigidBody.AddForceAtPosition(Physics.gravity / Mathf.Max(1, floaterCount), transform.position, ForceMode.Acceleration);

        // 2) sample wave height (ignore horizontal displacement for forces)
        Vector3 dummy;
        float waterHeight = WaveManager.instance.GetWaveHeight(transform.position.x, transform.position.z, out dummy);

        // 3) apply buoyancy only when submerged
        if (transform.position.y < waterHeight)
        {
            float depth = waterHeight - transform.position.y;
            float submergence = Mathf.Clamp01(depth / Mathf.Max(0.0001f, depthBeforeSubmerged));
            float lift = submergence * displacementAmount * Mathf.Abs(Physics.gravity.y);

            // Upward buoyancy at this floater point
            rigidBody.AddForceAtPosition(Vector3.up * lift, transform.position, ForceMode.Acceleration);

            // 4) add water drag to damp motion while submerged
            //    (proportional damping; tweak to taste)
            rigidBody.AddForce(-rigidBody.velocity * waterLinearDrag * submergence, ForceMode.Acceleration);
            rigidBody.AddTorque(-rigidBody.angularVelocity * waterAngularDrag * submergence, ForceMode.Acceleration);
        }

        // IMPORTANT: Do NOT write to rigidBody.position here (no manual horizontal displacement).
    }
}
