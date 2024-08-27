using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Floater : MonoBehaviour
{
    public Rigidbody rigidBody;
    public float depthBeforeSubmerged = 1f;
    public float displacementAmount = 3f;
    public int floaterCount = 1;
    public float waterDrag = 0.99f;
    public float waterAngularDrag = 0.5f;

    private void FixedUpdate()
    {
        // Apply gravity to the rigidbody
        rigidBody.AddForceAtPosition(Physics.gravity / floaterCount, transform.position, ForceMode.Acceleration);

        // Get the wave height and displacement from the WaveManager
        Vector3 displacement;
        float waveHeight = WaveManager.instance.GetWaveHeight(transform.position.x, transform.position.z, out displacement);

        // Check if the object is below the wave height
        if (transform.position.y < waveHeight)
        {
            // Calculate the displacement multiplier based on how submerged the object is
            float displacementMultiplier = Mathf.Clamp01((waveHeight - transform.position.y) / depthBeforeSubmerged) * displacementAmount;

            // Apply an upward force based on the displacement
            rigidBody.AddForceAtPosition(new Vector3(0f, Mathf.Abs(Physics.gravity.y) * displacementMultiplier, 0f), transform.position, ForceMode.Acceleration);

            // Apply drag to the velocity and angular velocity to simulate water resistance
            rigidBody.AddForce(displacementMultiplier * -rigidBody.velocity * waterDrag * Time.fixedDeltaTime, ForceMode.VelocityChange);
            rigidBody.AddTorque(displacementMultiplier * -rigidBody.angularVelocity * waterAngularDrag * Time.fixedDeltaTime, ForceMode.VelocityChange);

            // Apply the horizontal displacement to the floater's position
            rigidBody.position += displacement * displacementMultiplier * Time.fixedDeltaTime;
        }
    }
}