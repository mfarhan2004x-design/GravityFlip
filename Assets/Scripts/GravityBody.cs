using UnityEngine;

namespace GravityFlip
{
    /// <summary>
    /// Gives one object its own personal gravity direction.
    ///
    /// WHY THIS EXISTS: Unity has a single global gravity vector (Physics.gravity,
    /// default 0,-9.81,0). That is useless to us — the whole game is about "down"
    /// being different per object and changing at runtime. So we switch this
    /// Rigidbody's built-in gravity off and push it ourselves every physics step.
    ///
    /// The direction is smoothed rather than snapped, so a flip reads as the world
    /// swinging around you instead of a hard teleport of physics.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class GravityBody : MonoBehaviour
    {
        [Tooltip("Acceleration in m/s^2. Earth is 9.81, but platformers almost always " +
                 "use a much higher value because realistic gravity feels floaty.")]
        [SerializeField] private float gravityStrength = 26f;

        [Tooltip("How quickly gravity swings to a new direction. Higher = snappier.")]
        [SerializeField] private float reorientSharpness = 9f;

        private Rigidbody rb;
        private Vector3 currentDirection = Vector3.down;
        private Vector3 targetDirection = Vector3.down;

        /// <summary>The direction things fall right now (unit length).</summary>
        public Vector3 GravityDirection => currentDirection;

        /// <summary>The player's local "up" — everything in the controller is built on this.</summary>
        public Vector3 Up => -currentDirection;

        /// <summary>Where we are heading, ignoring the smoothing.</summary>
        public Vector3 TargetUp => -targetDirection;

        /// <summary>True while mid-flip. Useful for suppressing input or playing effects.</summary>
        public bool IsReorienting => Vector3.Dot(currentDirection, targetDirection) < 0.9999f;

        public float GravityStrength => gravityStrength;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();

            // Hand over control. If you forget this line you get double gravity:
            // Unity's downward pull plus ours, which feels broken in a way that is
            // surprisingly hard to diagnose.
            rb.useGravity = false;
        }

        /// <summary>Flip so that <paramref name="newUp"/> becomes the new up direction.</summary>
        public void SetUp(Vector3 newUp)
        {
            SetGravityDirection(-newUp);
        }

        public void SetGravityDirection(Vector3 direction)
        {
            // Guard against a zero vector. Normalizing (0,0,0) gives (0,0,0), which would
            // silently kill gravity entirely — a great example of a bug that looks like
            // "physics is broken" but is really one bad input.
            if (direction.sqrMagnitude < 1e-6f) return;
            targetDirection = direction.normalized;
        }

        // Physics goes in FixedUpdate, not Update.
        // Update runs once per rendered frame (variable, could be 40fps or 240fps).
        // FixedUpdate runs on a steady timestep (0.02s by default) in lockstep with the
        // physics engine. Applying forces in Update makes your gravity strength depend
        // on the player's framerate, which is a classic beginner bug.
        private void FixedUpdate()
        {
            StepReorientation(Time.fixedDeltaTime);

            // ForceMode.Acceleration applies a change in velocity while IGNORING mass.
            // That is exactly what gravity does — a feather and an anvil fall at the
            // same rate. If you used ForceMode.Force here, heavy objects would fall slower.
            rb.AddForce(currentDirection * gravityStrength, ForceMode.Acceleration);
        }

        private void StepReorientation(float deltaTime)
        {
            if (currentDirection == targetDirection) return;

            // EDGE CASE worth understanding, because it will bite you:
            // if the new direction is exactly opposite the current one (a 180 degree
            // flip, floor to ceiling) then there is no unique arc between them —
            // infinitely many rotation planes are equally valid. Slerp has nothing to
            // work with and the flip can stall or pop. We nudge off-axis first so a
            // well-defined plane exists.
            if (Vector3.Dot(currentDirection, targetDirection) < -0.9995f)
            {
                Vector3 axis = Vector3.Cross(currentDirection, Vector3.up);
                if (axis.sqrMagnitude < 1e-4f)
                {
                    axis = Vector3.Cross(currentDirection, Vector3.right);
                }
                currentDirection = Quaternion.AngleAxis(5f, axis.normalized) * currentDirection;
            }

            // Framerate-independent exponential smoothing.
            // The naive version, Slerp(current, target, sharpness * deltaTime), moves a
            // different amount depending on framerate. 1 - e^(-k*dt) converges to the
            // same place per unit of real time no matter the timestep. Use this pattern
            // any time you smooth a value toward a target.
            float t = 1f - Mathf.Exp(-reorientSharpness * deltaTime);
            currentDirection = Vector3.Slerp(currentDirection, targetDirection, t).normalized;

            // Snap when close enough, so IsReorienting reliably becomes false.
            if (Vector3.Dot(currentDirection, targetDirection) > 0.99999f)
            {
                currentDirection = targetDirection;
            }
        }
    }
}
