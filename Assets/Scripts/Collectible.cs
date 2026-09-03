using UnityEngine;

namespace GravityFlip
{
    /// <summary>
    /// A pickup that spins and bobs. Mounted on floors, walls and ceilings alike, so it
    /// orients itself to whatever surface it was placed against instead of assuming the
    /// world's up axis.
    ///
    /// WHY A TRIGGER AND NOT A NORMAL COLLIDER: a trigger detects overlap without
    /// physically blocking anything, so you walk through the pickup instead of bumping
    /// into it. There's a second, quieter benefit — both the ground sweep in
    /// PlayerController and the flip ray in GravityFlipper pass
    /// QueryTriggerInteraction.Ignore, which means pickups are automatically invisible
    /// to them. Without that you'd be able to stand on a coin, or flip your gravity onto
    /// one in mid-air.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Collectible : MonoBehaviour
    {
        [SerializeField] private float spinDegreesPerSecond = 110f;
        [SerializeField] private float bobAmplitude = 0.18f;
        [SerializeField] private float bobCyclesPerSecond = 0.8f;

        // These two are [SerializeField] for a specific reason. If Bootstrap generates the
        // level in edit mode and you save the scene, private fields WITHOUT this attribute
        // are wiped on reload — the pickup would wake up with no manager and never count
        // towards the win condition. Serialized fields are written into the scene file, so
        // the configuration survives. This is the difference between "set up in code at
        // runtime" and "authored data", and it's most of what [SerializeField] is for.
        [SerializeField] private GameManager manager;
        [SerializeField] private Vector3 mountUp = Vector3.up;

        private Vector3 anchor;
        private Vector3 axis = Vector3.up;
        private float phase;
        private bool taken;

        /// <summary>
        /// Bootstrap calls this right after spawning. <paramref name="surfaceUp"/> is the
        /// normal of whatever surface the pickup sits on — the pickup spins and bobs along
        /// that axis, so one on a wall floats sideways off the wall rather than upwards.
        ///
        /// Note what this does NOT do: it doesn't register with the manager. Configuration
        /// happens here (and may happen in the editor, long before the game runs);
        /// registration happens in Start, when the game is actually starting. Keeping those
        /// two apart is what lets the same component work whether it was created at runtime
        /// or placed by hand and saved.
        /// </summary>
        public void Initialise(GameManager gameManager, Vector3 surfaceUp)
        {
            manager = gameManager;
            mountUp = surfaceUp;
        }

        private void Start()
        {
            // Snapshot the authored position as the point to bob around. Doing it here and
            // not in Initialise means dragging the pickup somewhere else in the editor just
            // works — the new position becomes the anchor.
            anchor = transform.position;
            axis = mountUp.sqrMagnitude < 1e-6f ? Vector3.up : mountUp.normalized;

            // A random starting phase so a group of pickups doesn't bob in perfect
            // lockstep. Tiny detail, but synchronised motion reads as "cheap" instantly.
            phase = Random.value * Mathf.PI * 2f;

            if (manager != null) manager.RegisterCollectible();
            else Debug.LogWarning("Collectible '" + name + "' has no Game Manager, so collecting " +
                                  "it won't count. Drag the Game Manager into its slot.", this);
        }

        private void Update()
        {
            transform.Rotate(axis, spinDegreesPerSecond * Time.deltaTime, Space.World);

            float offset = Mathf.Sin(Time.time * bobCyclesPerSecond * Mathf.PI * 2f + phase) * bobAmplitude;
            transform.position = anchor + axis * offset;
        }

        private void OnTriggerEnter(Collider other)
        {
            // Guard against double-collection. OnTriggerEnter can fire more than once
            // before Destroy actually takes effect at the end of the frame, which would
            // otherwise let one pickup count twice and break the win condition.
            if (taken) return;

            // Check the Rigidbody rather than the collider's own GameObject. The player's
            // colliders could live on child objects, and attachedRigidbody always walks
            // up to the body that owns them.
            Rigidbody body = other.attachedRigidbody;
            if (body == null) return;
            if (body.GetComponent<PlayerController>() == null) return;

            taken = true;
            if (manager != null) manager.NotifyCollected();
            Destroy(gameObject);
        }
    }
}
