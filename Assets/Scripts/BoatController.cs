using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BoatController : MonoBehaviour
{
    public float moveSpeed = 5f;
    public float turnSpeed = 50f;

    void Update()
    {
        // Forward/backward movement
        float move = Input.GetAxis("Vertical") * moveSpeed * Time.deltaTime;
        transform.Translate(0, 0, move);

        // Turning left/right
        float turn = Input.GetAxis("Horizontal") * turnSpeed * Time.deltaTime;
        transform.Rotate(0, turn, 0);
    }
}
