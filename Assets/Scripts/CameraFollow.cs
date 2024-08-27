using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target; // The boat's transform
    public Vector3 offset;   // Offset from the target position
    public float smoothSpeed = 0.125f; // Smoothing factor

    void LateUpdate()
    {
        // Desired position for the camera
        Vector3 desiredPosition = target.position + offset;
        
        // Smoothly interpolate between current position and desired position
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);
        
        // Set the camera's position to the smoothed position
        transform.position = smoothedPosition;

        // Optionally, keep the camera looking at the boat (if not orthogonal view)
        // transform.LookAt(target);
    }
}
