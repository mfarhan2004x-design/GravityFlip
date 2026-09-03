using UnityEngine;

namespace GravityFlip
{
    /// <summary>
    /// A character controller where every direction is derived from the current gravity
    /// instead of being hardcoded to world axes.
    ///
    /// THE CENTRAL IDEA: a normal platformer controller is full of assumptions like
    /// "up is (0,1,0)", "jump means add velocity.y", "grounded means raycast down".
    /// Every one of those breaks the moment gravity can point anywhere. So instead we
    /// ask GravityBody for the current up vector and rebuild our frame of reference
    /// from it each physics step. Nothing in this file mentions Vector3.up.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(GravityBody))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 8f;
        [Tooltip("How fast we reach target speed on the ground. High = responsive/arcadey.")]
        [SerializeField] private float groundAcceleration = 70f;
        [Tooltip("Deliberately lower than ground accel so mid-air control feels committed.")]
        [SerializeField] private float airAcceleration = 22f;
        [Tooltip("Peak jump height in metres. Converted to a launch speed using gravity.")]
        [SerializeField] private float jumpHeight = 2.2f;

        [Header("Ground detection")]
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float groundCheckSkin = 0.12f;
        [Tooltip("Surfaces tilted more than this from 'up' don't count as standable.")]
        [SerializeField] private float maxSlopeAngle = 50f;

        [Header("Game feel")]
        [Tooltip("Grace period after walking off an edge where a jump still works. " +
                 "Players press jump slightly late constantly; without this the game feels stiff.")]
        [SerializeField] private float coyoteTime = 0.12f;
        [Tooltip("Remembers a jump pressed slightly before landing and fires it on touchdown.")]
        [SerializeField] private float jumpBufferTime = 0.12f;
        [SerializeField] private float turnSharpness = 14f;

        private Rigidbody rb;
        private CapsuleCollider capsule;
        private GravityBody gravity;

        [Header("References")]
        [Tooltip("Drag the Main Camera here. Movement is relative to where the camera looks, " +
                 "so without it W means 'the direction the capsule already faces'.")]
        [SerializeField] private Transform cameraTransform;

        // Reused across ground checks so we never allocate during gameplay.
        // Allocating in a per-frame method creates garbage, and garbage collection
        // shows up as a visible stutter. Interviewers do ask about this.
        private readonly RaycastHit[] groundHits = new RaycastHit[8];

        private Vector3 moveInput;
        private Vector3 lastWishDirection;
        private float lastGroundedTime = -99f;
        private float lastJumpPressedTime = -99f;

        public bool IsGrounded { get; private set; }
        public Vector3 GroundNormal { get; private set; }
        public Vector3 Up => gravity.Up;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            gravity = GetComponent<GravityBody>();

            // We rotate the body ourselves in AlignToGravity(). Letting the physics
            // engine also apply torque would fight us and make the capsule tip over.
            rb.freezeRotation = true;

            // Physics ticks 50 times a second by default, but the game might render at
            // 144fps. Interpolation smooths the visual position between physics steps.
            // Without it, fast movement looks subtly juddery and people can't say why.
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            // Cheap insurance against falling through thin platforms at high speed.
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            rb.SetLinearDamping(0f);   // we do our own friction in ApplyMovement
            rb.SetAngularDamping(0f);

            GroundNormal = gravity.Up;
        }

        /// <summary>
        /// Optional override. The normal path is dragging the camera into the Inspector
        /// slot; this exists for the case where the camera is created at runtime.
        /// </summary>
        public void SetCamera(Transform cam)
        {
            cameraTransform = cam;
        }

        // Input is read in Update, not FixedUpdate. Update runs every rendered frame, so
        // it never misses a key press. FixedUpdate can run zero or twice in a frame, which
        // means GetButtonDown there will occasionally drop or double-count an input.
        private void Update()
        {
            moveInput = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));

            if (Input.GetButtonDown("Jump"))
            {
                lastJumpPressedTime = Time.time;
            }
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Vector3 up = gravity.Up;

            RefreshGrounded(up);
            ApplyMovement(up, dt);
            TryJump(up);
            AlignToGravity(up, dt);
        }

        private void ApplyMovement(Vector3 up, float dt)
        {
            // STEP 1: build a movement basis that lies flat against whatever surface
            // counts as the floor right now. We take the camera's forward and flatten it
            // into the plane perpendicular to 'up' — that is what makes W always mean
            // "away from the camera" whether you're on a floor, a wall, or a ceiling.
            Vector3 forward;
            if (cameraTransform != null)
            {
                forward = Vector3.ProjectOnPlane(cameraTransform.forward, up);

                // If the camera is looking almost straight down the up axis, its forward
                // flattens to nearly zero and the basis becomes garbage. Fall back to the
                // camera's own up vector, which is guaranteed to be perpendicular to it.
                if (forward.sqrMagnitude < 1e-6f)
                {
                    forward = Vector3.ProjectOnPlane(cameraTransform.up, up);
                }
            }
            else
            {
                forward = Vector3.ProjectOnPlane(transform.forward, up);
            }

            if (forward.sqrMagnitude < 1e-6f) return;
            forward.Normalize();

            // In Unity's coordinate system Cross(up, forward) gives right.
            // Worth deriving once by hand: Cross((0,1,0), (0,0,1)) == (1,0,0).
            Vector3 right = Vector3.Cross(up, forward);

            Vector3 wish = forward * moveInput.z + right * moveInput.x;

            // Holding two keys gives an input vector of length sqrt(2). Without this clamp
            // diagonal movement is ~41% faster than straight movement — a bug shipped in a
            // startling number of games.
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            lastWishDirection = wish;

            // STEP 2: split velocity into the part along 'up' and the part across it.
            // We control the across-part (walking) and leave the along-part alone so
            // gravity and jumping still work untouched.
            Vector3 velocity = rb.GetVelocity();
            Vector3 alongUp = Vector3.Project(velocity, up);
            Vector3 acrossUp = velocity - alongUp;

            Vector3 target = wish * moveSpeed;
            float accel = IsGrounded ? groundAcceleration : airAcceleration;
            Vector3 newAcross = Vector3.MoveTowards(acrossUp, target, accel * dt);

            rb.SetVelocity(newAcross + alongUp);
        }

        /// <summary>
        /// Sweeps a sphere from the capsule's middle toward the player's feet.
        /// A sphere sweep rather than a single ray so you stay grounded when standing
        /// half-off a ledge, where a centre ray would miss and you'd start falling.
        /// </summary>
        private void RefreshGrounded(Vector3 up)
        {
            IsGrounded = false;
            GroundNormal = up;

            float castRadius = capsule.radius * 0.92f;
            Vector3 origin = transform.TransformPoint(capsule.center);
            float halfHeight = Mathf.Max(capsule.height * 0.5f, capsule.radius);
            float distance = halfHeight - castRadius + groundCheckSkin;

            int count = Physics.SphereCastNonAlloc(
                origin, castRadius, -up, groundHits, distance,
                groundMask, QueryTriggerInteraction.Ignore);

            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = groundHits[i];

                // Ignore our own capsule. The sweep starts inside it, and a sweep that
                // begins overlapping a collider reports distance 0 with a meaningless
                // normal — which would read as "grounded on nothing" every single frame.
                if (hit.collider.attachedRigidbody == rb) continue;
                if (hit.distance <= 0f) continue;

                // Is this surface flat enough to stand on, relative to OUR up?
                // On a wall we've flipped onto, the wall's normal IS our up, so it passes.
                if (Vector3.Angle(hit.normal, up) > maxSlopeAngle) continue;

                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    GroundNormal = hit.normal;
                    IsGrounded = true;
                }
            }

            if (IsGrounded)
            {
                lastGroundedTime = Time.time;
            }
        }

        private void TryJump(Vector3 up)
        {
            bool withinCoyoteWindow = Time.time - lastGroundedTime <= coyoteTime;
            bool jumpQueued = Time.time - lastJumpPressedTime <= jumpBufferTime;

            if (!withinCoyoteWindow || !jumpQueued) return;

            // Consume both timers so one press can't produce two jumps.
            lastJumpPressedTime = -99f;
            lastGroundedTime = -99f;

            // Derived from conservation of energy: to reach height h under acceleration g
            // you must leave the ground at sqrt(2gh). Specifying jump HEIGHT instead of a
            // magic launch speed means the jump stays the same size if you retune gravity,
            // which you will do a lot while chasing good feel.
            float jumpSpeed = Mathf.Sqrt(2f * gravity.GravityStrength * jumpHeight);

            Vector3 velocity = rb.GetVelocity();
            Vector3 acrossUp = velocity - Vector3.Project(velocity, up);

            // Overwrite rather than add the up component, so jumping while already rising
            // or falling gives a consistent height.
            rb.SetVelocity(acrossUp + up * jumpSpeed);
        }

        /// <summary>
        /// Keeps the capsule standing perpendicular to the current floor, and turns it to
        /// face where it's walking. This is purely cosmetic — physics doesn't care which
        /// way the capsule points — but it's most of what sells the flip visually.
        /// </summary>
        private void AlignToGravity(Vector3 up, float dt)
        {
            Vector3 desiredForward = lastWishDirection.sqrMagnitude > 1e-4f
                ? lastWishDirection
                : Vector3.ProjectOnPlane(transform.forward, up);

            // Immediately after a 90 degree flip the old forward can end up parallel to the
            // new up, which flattens to zero. Try other axes of the body before giving up.
            if (desiredForward.sqrMagnitude < 1e-6f)
            {
                desiredForward = Vector3.ProjectOnPlane(transform.up, up);
            }
            if (desiredForward.sqrMagnitude < 1e-6f)
            {
                desiredForward = Vector3.ProjectOnPlane(transform.right, up);
            }
            if (desiredForward.sqrMagnitude < 1e-6f) return;

            Quaternion target = Quaternion.LookRotation(desiredForward.normalized, up);
            float t = 1f - Mathf.Exp(-turnSharpness * dt);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, target, t));
        }

        // Draws the ground check in the editor when the player is selected. Being able to
        // SEE a check you're debugging instead of guessing at booleans is one of the
        // highest-value habits you can build early.
        private void OnDrawGizmosSelected()
        {
            if (capsule == null) capsule = GetComponent<CapsuleCollider>();
            if (capsule == null) return;

            Vector3 up = gravity != null ? gravity.Up : transform.up;
            float castRadius = capsule.radius * 0.92f;
            Vector3 origin = transform.TransformPoint(capsule.center);
            float halfHeight = Mathf.Max(capsule.height * 0.5f, capsule.radius);
            float distance = halfHeight - castRadius + groundCheckSkin;

            Gizmos.color = IsGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(origin - up * distance, castRadius);
        }
    }
}
