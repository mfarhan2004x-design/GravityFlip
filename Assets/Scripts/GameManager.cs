using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GravityFlip
{
    public enum GameState
    {
        Playing,
        Won
    }

    /// <summary>
    /// Owns the rules: how many things there are to collect, how long you've taken, and
    /// whether you've won.
    ///
    /// A NOTE ON THE DESIGN, because this is the kind of thing interviewers ask about:
    /// there is no `public static GameManager Instance` here. Singletons are the default
    /// way beginners wire up a manager, and they work, but they make every script that
    /// touches them impossible to test in isolation and they hide your dependencies —
    /// you can no longer tell what a class needs by reading its fields.
    ///
    /// Instead Bootstrap creates this, then hands the reference to whoever needs it.
    /// That's dependency injection, done by hand. Being able to explain that choice is
    /// worth more than the code itself.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [SerializeField] private KeyCode restartKey = KeyCode.R;

        public GameState State { get; private set; } = GameState.Playing;
        public int TotalCollectibles { get; private set; }
        public int Collected { get; private set; }
        public float ElapsedTime { get; private set; }

        /// <summary>Fires with (collected, total) whenever a pickup is taken.</summary>
        public event Action<int, int> CollectedChanged;

        /// <summary>Fires once with the final time when the last pickup is taken.</summary>
        public event Action<float> GameWon;

        [Header("References")]
        [Tooltip("Drag the Player into the first two slots and the Main Camera into the " +
                 "third. The manager needs them so it can take control away from you when " +
                 "the last pickup is collected.")]
        [SerializeField] private PlayerController player;
        [SerializeField] private GravityFlipper flipper;
        [SerializeField] private OrbitCamera orbitCamera;

        /// <summary>
        /// Called by each Collectible as it's created, so the manager learns the total
        /// without Bootstrap having to count them and keep that number in sync.
        /// </summary>
        public void RegisterCollectible()
        {
            TotalCollectibles++;
        }

        /// <summary>Optional override for a runtime-spawned player.</summary>
        public void BindPlayer(PlayerController controller, GravityFlipper gravityFlipper, OrbitCamera camera)
        {
            player = controller;
            flipper = gravityFlipper;
            orbitCamera = camera;
        }

        public void NotifyCollected()
        {
            if (State != GameState.Playing) return;

            Collected++;

            // The ?.Invoke pattern: only call the event if something is actually
            // listening. Raising an event with no subscribers would throw a
            // NullReferenceException, which is a classic first-time-using-events bug.
            CollectedChanged?.Invoke(Collected, TotalCollectibles);

            if (Collected >= TotalCollectibles)
            {
                Win();
            }
        }

        private void Win()
        {
            State = GameState.Won;

            // Take control away from the player rather than freezing time with
            // Time.timeScale = 0. Zeroing timescale also stops animations, particles and
            // anything else you might want still running behind a win screen, and it has
            // a habit of causing subtle bugs later.
            if (player != null) player.enabled = false;
            if (flipper != null) flipper.enabled = false;
            if (orbitCamera != null) orbitCamera.enabled = false;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            GameWon?.Invoke(ElapsedTime);
        }

        private void Update()
        {
            if (State == GameState.Playing)
            {
                ElapsedTime += Time.deltaTime;
                return;
            }

            if (Input.GetKeyDown(restartKey))
            {
                Restart();
            }
        }

        /// <summary>
        /// Reloading the active scene wipes everything and re-runs Bootstrap from scratch.
        /// This is the one place where building the level in code pays off for free —
        /// there is no state to reset by hand, because nothing survives the reload.
        /// </summary>
        public void Restart()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            Scene active = SceneManager.GetActiveScene();

            // A scene that has never been saved and added to Build Settings has a
            // buildIndex of -1, and LoadScene(-1) throws. Failing with an instruction beats
            // failing with an exception, so check rather than assume.
            if (active.buildIndex < 0)
            {
                Debug.LogError(
                    "Restart needs this scene saved and listed in Build Settings. " +
                    "Do File > Save, then File > Build Settings > Add Open Scenes. " +
                    "Until then, stop and restart Play mode to replay.");
                return;
            }

            SceneManager.LoadScene(active.buildIndex);
        }

        /// <summary>Formats seconds as m:ss.hh for display.</summary>
        public static string FormatTime(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int minutes = (int)(seconds / 60f);
            float remainder = seconds - minutes * 60f;
            return string.Format("{0:0}:{1:00.00}", minutes, remainder);
        }
    }
}
