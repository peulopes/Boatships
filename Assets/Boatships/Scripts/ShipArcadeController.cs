using UnityEngine;
using UnityEngine.InputSystem;

namespace Boatships
{
    /// <summary>
    /// Prototype arcade navigation and visual buoyancy. The kinematic body is
    /// deliberate: proper collisions and physics-based sinking come later.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ShipArcadeController : MonoBehaviour
    {
        [SerializeField] private OceanWaves ocean;
        [SerializeField] private VirtualJoystick moveJoystick;
        [SerializeField] private Transform visualPivot;

        [Header("Arcade Motion")]
        [SerializeField] private float forwardSpeed = 9f;
        [SerializeField] private float reverseSpeed = 3.5f;
        [SerializeField] private float turningSpeedDeg = 105f;
        [SerializeField] private float acceleration = 16f;
        [SerializeField] private float deceleration = 11f;

        [Header("Wave Following")]
        [SerializeField] private float floatOffset = 0.55f;
        [SerializeField] private float waterFollowSpeed = 7f;
        [SerializeField] private float tiltFollowSpeed = 5f;
        [SerializeField] private float tiltSampleDistance = 1.5f;

        private Rigidbody body;
        private float currentSpeed;

        public OceanWaves Ocean { set => ocean = value; }
        public VirtualJoystick Joystick { set => moveJoystick = value; }
        public Transform VisualPivot { set => visualPivot = value; }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        private Vector2 ReadMoveInput()
        {
            Vector2 input = moveJoystick != null ? moveJoystick.Direction : Vector2.zero;
            // Desktop editor testing: WASD / arrow keys. Touch input takes priority.
            if (input.sqrMagnitude > 0.0025f) return input;

            Keyboard kb = Keyboard.current;
            if (kb == null) return Vector2.zero;

            float x = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f)
                    - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
            float y = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f)
                    - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }

        private void FixedUpdate()
        {
            Vector2 input = ReadMoveInput();
            float targetSpeed = input.y >= 0f
                ? input.y * forwardSpeed
                : input.y * reverseSpeed;
            float speedChange = Mathf.Abs(input.y) > 0.01f ? acceleration : deceleration;
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, speedChange * Time.fixedDeltaTime);

            Quaternion yaw = body.rotation * Quaternion.Euler(
                0f, input.x * turningSpeedDeg * Time.fixedDeltaTime, 0f);

            Vector3 next = body.position + (yaw * Vector3.forward) * currentSpeed * Time.fixedDeltaTime;
            float waterHeight = ocean != null ? ocean.SampleHeight(next.x, next.z, Time.fixedTime) : 0f;
            float follow = 1f - Mathf.Exp(-waterFollowSpeed * Time.fixedDeltaTime);
            next.y = Mathf.Lerp(body.position.y, waterHeight + floatOffset, follow);

            body.MoveRotation(yaw);
            body.MovePosition(next);
        }

        private void LateUpdate()
        {
            if (ocean == null || visualPivot == null) return;

            Vector3 p = transform.position;
            Vector3 f = transform.forward * tiltSampleDistance;
            Vector3 r = transform.right * tiltSampleDistance;
            float time = Time.time;

            float front = ocean.SampleHeight(p.x + f.x, p.z + f.z, time);
            float back = ocean.SampleHeight(p.x - f.x, p.z - f.z, time);
            float right = ocean.SampleHeight(p.x + r.x, p.z + r.z, time);
            float left = ocean.SampleHeight(p.x - r.x, p.z - r.z, time);

            float pitch = -Mathf.Atan2(front - back, tiltSampleDistance * 2f) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2(right - left, tiltSampleDistance * 2f) * Mathf.Rad2Deg;
            Quaternion tilt = Quaternion.Euler(
                Mathf.Clamp(pitch, -12f, 12f), 0f, Mathf.Clamp(roll, -12f, 12f));
            float blend = 1f - Mathf.Exp(-tiltFollowSpeed * Time.deltaTime);
            visualPivot.localRotation = Quaternion.Slerp(visualPivot.localRotation, tilt, blend);
        }
    }
}
