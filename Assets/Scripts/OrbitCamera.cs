using UnityEngine;

namespace GravityFlip
{
    /// <summary>
    /// A third-person orbit camera that lives inside the player's gravity frame.
    ///
    /// WHY A NORMAL ORBIT CAMERA DOESN'T WORK: the usual implementation stores a yaw
    /// angle around the world Y axis and a pitch angle, then builds a rotation from
    /// Euler angles. That hardcodes "up is world up". The instant you stand on a wall,
    /// your camera is sideways and the controls feel inverted and nauseating.
    ///
    /// Instead we store the camera's orientation as a forward VECTOR plus a pitch angle,
    /// and re-derive everything from the player's current up each frame.
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        [Header("Framing")]
        [SerializeField] private float distance = 7f;
        [Tooltip("Look at a point above the player's feet rather than the origin.")]
        [SerializeField] private float focusHeight = 1.2f;

        [Header("Look")]
        [SerializeField] private float sensitivity = 2.2f;
        [Tooltip("Negative pitch looks up, positive looks down.")]
        [SerializeField] private float minPitch = -40f;
        [SerializeField] private float maxPitch = 75f;
        [SerializeField] private bool invertY = false;

        [Header("Smoothing")]
        [Tooltip("How fast the camera re-levels itself after a flip. Too fast is jarring, " +
                 "too slow is disorienting. This value is worth playing with by feel.")]
        [SerializeField] private float upSmoothing = 7f;
        [SerializeField] private float positionSmoothing = 18f;

        [Header("Wall avoidance")]
        [SerializeField] private LayerMask obstacleMask = ~0;
        [SerializeField] private float collisionRadius = 0.3f;

        [Header("Target")]
        [Tooltip("Drag the Player here. Leave empty only if something spawns the player at " +
                 "runtime and calls SetTarget itself.")]
        [SerializeField] private Transform target;

        private Rigidbody targetBody;
        private GravityBody targetGravity;

        private Vector3 smoothedUp = Vector3.up;
        private Vector3 forwardReference = Vector3.forward;
        private float pitch = 15f;
        private bool ready;

        private readonly RaycastHit[] obstacleHits = new RaycastHit[8];

        /// <summary>
        /// Picks up the reference you dragged into the Inspector. Note what is and isn't
        /// saved in the scene: `target` is a [SerializeField] so it persists, but
        /// `targetBody`, `targetGravity` and `smoothedUp` are derived state that has to be
        /// rebuilt every time the game starts. Keeping the two kinds of data apart —
        /// authored versus derived — is most of what Start() is for.
        /// </summary>
        private void Start()
        {
            if (target != null) SetTarget(target);
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            if (target == null) return;

            targetBody = target.GetComponent<Rigidbody>();
            targetGravity = target.GetComponent<GravityBody>();

            smoothedUp = targetGravity != null ? targetGravity.Up : Vector3.up;

            forwardReference = Vector3.ProjectOnPlane(target.forward, smoothedUp);
            if (forwardReference.sqrMagnitude < 1e-6f)
            {
                forwardReference = PerpendicularTo(smoothedUp);
            }
            forwardReference.Normalize();

            ready = true;

            // Only grab the mouse if the game is actually running. Bootstrap can call this
            // from the editor when generating the level, and stealing the cursor while
            // someone is trying to click around the Inspector is hostile.
            if (Application.isPlaying) LockCursor(true);
        }

        private void Update()
        {
            // Escape releases the mouse so you can actually get back to the editor.
            // Forget this and your first playtest ends with a panicked alt-tab.
            if (Input.GetKeyDown(KeyCode.Escape)) LockCursor(false);
            if (Input.GetMouseButtonDown(0)) LockCursor(true);
        }

        // LateUpdate, not Update: it runs after every other script's Update, so the player
        // has already moved this frame. Positioning the camera in Update instead makes it
        // chase a stale position, which reads as a subtle lag on the character.
        private void LateUpdate()
        {
            if (!ready || target == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            UpdateOrientation(dt);
            UpdatePosition(dt);
        }

        private void UpdateOrientation(float dt)
        {
            // Aim at where gravity is HEADING, not where it currently is. GravityBody is
            // already smoothing its own value; chaining two smoothers would make the
            // camera lag the physics and feel mushy. Both independently ease to the
            // same destination at their own rate instead.
            Vector3 desiredUp = targetGravity != null ? targetGravity.TargetUp : Vector3.up;
            smoothedUp = SafeSlerp(smoothedUp, desiredUp, 1f - Mathf.Exp(-upSmoothing * dt));

            // Mouse deltas are NOT multiplied by deltaTime. They already represent movement
            // that happened during this frame, so scaling by dt would make sensitivity
            // depend on framerate — a very common mistake.
            float mouseX = Input.GetAxis("Mouse X") * sensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * sensitivity * (invertY ? 1f : -1f);

            // Yaw is applied by rotating our stored forward vector around the current up.
            // Because the vector is re-projected into the new plane every frame, a flip
            // carries your heading with it instead of resetting the camera.
            forwardReference = Quaternion.AngleAxis(mouseX, smoothedUp) * forwardReference;
            forwardReference = Vector3.ProjectOnPlane(forwardReference, smoothedUp);
            if (forwardReference.sqrMagnitude < 1e-6f)
            {
                forwardReference = PerpendicularTo(smoothedUp);
            }
            forwardReference.Normalize();

            pitch = Mathf.Clamp(pitch + mouseY, minPitch, maxPitch);
        }

        private void UpdatePosition(float dt)
        {
            Vector3 right = Vector3.Cross(smoothedUp, forwardReference);
            Vector3 viewDirection = Quaternion.AngleAxis(pitch, right) * forwardReference;

            Vector3 focus = target.position + smoothedUp * focusHeight;
            float allowedDistance = distance;

            // Pull the camera in if a wall is in the way. Essential here in a way it isn't
            // in a normal platformer: you spend half the game standing on walls, so the
            // camera is constantly being pushed into geometry.
            int count = Physics.SphereCastNonAlloc(
                focus, collisionRadius, -viewDirection, obstacleHits, distance,
                obstacleMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = obstacleHits[i];
                if (hit.distance <= 0f) continue;

                // Don't let the player's own capsule shove the camera into their face.
                if (targetBody != null && hit.collider.attachedRigidbody == targetBody) continue;

                allowedDistance = Mathf.Min(allowedDistance, hit.distance);
            }

            Vector3 desiredPosition = focus - viewDirection * allowedDistance;

            transform.position = Vector3.Lerp(
                transform.position, desiredPosition, 1f - Mathf.Exp(-positionSmoothing * dt));

            transform.rotation = Quaternion.LookRotation(viewDirection, smoothedUp);
        }

        /// <summary>
        /// Vector3.Slerp has no defined arc between exactly opposite vectors, which is
        /// precisely what a floor-to-ceiling flip produces. Nudge off the axis first.
        /// </summary>
        private static Vector3 SafeSlerp(Vector3 from, Vector3 to, float t)
        {
            if (Vector3.Dot(from, to) < -0.9995f)
            {
                from = Quaternion.AngleAxis(5f, PerpendicularTo(from)) * from;
            }
            return Vector3.Slerp(from, to, t).normalized;
        }

        /// <summary>Any unit vector at right angles to the given one.</summary>
        private static Vector3 PerpendicularTo(Vector3 v)
        {
            Vector3 axis = Vector3.Cross(v, Vector3.up);
            if (axis.sqrMagnitude < 1e-4f)
            {
                axis = Vector3.Cross(v, Vector3.right);
            }
            return axis.normalized;
        }

        private static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
