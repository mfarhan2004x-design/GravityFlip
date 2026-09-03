using UnityEngine;

namespace GravityFlip
{
    /// <summary>
    /// The actual mechanic: look at a surface, press F, and that surface becomes the floor.
    ///
    /// It works by firing a ray through the centre of the screen, taking the NORMAL of
    /// whatever it hits (the direction the surface faces), and telling GravityBody that
    /// this normal is the new up. That's the entire trick — everything else in the project
    /// exists to make this one line feel good.
    /// </summary>
    [RequireComponent(typeof(GravityBody))]
    public class GravityFlipper : MonoBehaviour
    {
        [Header("Targeting")]
        [SerializeField] private float maxRange = 40f;
        [SerializeField] private LayerMask flippableMask = ~0;
        [Tooltip("Rounds the surface normal to the nearest world axis. Keeps flips " +
                 "predictable on a blocky level; turn off if you add curved geometry.")]
        [SerializeField] private bool snapToWorldAxes = true;

        [Header("Input")]
        [SerializeField] private KeyCode flipKey = KeyCode.F;
        [SerializeField] private bool alsoFlipOnRightMouse = true;
        [SerializeField] private float cooldown = 0.25f;

        [Header("Feel")]
        [Tooltip("Fraction of your speed along the OLD up that survives a flip. " +
                 "0 = dead stop, 1 = keep everything. Around a third preserves a sense of " +
                 "momentum without flinging you across the level.")]
        [Range(0f, 1f)]
        [SerializeField] private float velocityRetainedOnFlip = 0.35f;

        private GravityBody gravity;
        private Rigidbody rb;

        [Header("References")]
        [Tooltip("Drag the Main Camera here. The flip ray is fired from the centre of this " +
                 "camera's view, so it's what makes the crosshair mean anything.")]
        [SerializeField] private Camera viewCamera;

        private readonly RaycastHit[] hits = new RaycastHit[8];
        private float lastFlipTime = -99f;

        /// <summary>True when the crosshair is over something you could flip onto.</summary>
        public bool HasValidTarget { get; private set; }

        /// <summary>The up vector we would adopt if you pressed flip right now.</summary>
        public Vector3 CandidateUp { get; private set; }

        private void Awake()
        {
            gravity = GetComponent<GravityBody>();
            rb = GetComponent<Rigidbody>();
            CandidateUp = gravity.TargetUp;
        }

        public void SetCamera(Camera cam)
        {
            viewCamera = cam;
        }

        private void Update()
        {
            HasValidTarget = TryFindTarget(out Vector3 candidate);
            CandidateUp = candidate;

            bool pressed = Input.GetKeyDown(flipKey)
                           || (alsoFlipOnRightMouse && Input.GetMouseButtonDown(1));

            if (!pressed) return;
            if (Time.time - lastFlipTime < cooldown) return;
            if (!HasValidTarget) return;

            // Already our up — do nothing rather than burning the cooldown on a no-op.
            if (Vector3.Dot(candidate, gravity.TargetUp) > 0.999f) return;

            Flip(candidate);
        }

        private void Flip(Vector3 newUp)
        {
            lastFlipTime = Time.time;

            if (rb != null)
            {
                // Split velocity relative to the OLD frame before we change anything.
                Vector3 oldUp = gravity.Up;
                Vector3 velocity = rb.GetVelocity();
                Vector3 alongOldUp = Vector3.Project(velocity, oldUp);
                Vector3 acrossOldUp = velocity - alongOldUp;

                // Without this, flipping while falling fast means you arrive at the new
                // floor carrying all that speed sideways and skid off the level.
                rb.SetVelocity(acrossOldUp + alongOldUp * velocityRetainedOnFlip);
            }

            gravity.SetUp(newUp);
        }

        private bool TryFindTarget(out Vector3 candidateUp)
        {
            candidateUp = gravity.TargetUp;
            if (viewCamera == null) return false;

            // Straight out of the middle of the screen, which is where the crosshair is.
            Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            int count = Physics.RaycastNonAlloc(
                ray, hits, maxRange, flippableMask, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.distance <= 0f) continue;

                // The camera sits behind the player, so the ray passes straight through
                // them. Skipping our own colliders stops us flipping onto ourselves.
                if (rb != null && hit.collider.attachedRigidbody == rb) continue;

                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    candidateUp = hit.normal;
                    found = true;
                }
            }

            if (found && snapToWorldAxes)
            {
                candidateUp = SnapToNearestAxis(candidateUp);
            }

            return found;
        }

        /// <summary>Rounds a direction to whichever of the six world axes it's closest to.</summary>
        private static Vector3 SnapToNearestAxis(Vector3 v)
        {
            float ax = Mathf.Abs(v.x);
            float ay = Mathf.Abs(v.y);
            float az = Mathf.Abs(v.z);

            if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
            if (ay >= az) return new Vector3(0f, Mathf.Sign(v.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(v.z));
        }
    }
}
