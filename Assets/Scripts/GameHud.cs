using UnityEngine;

namespace GravityFlip
{
    /// <summary>
    /// Timer, pickup counter, and the win panel.
    ///
    /// The HUD never asks the GameManager "did something happen?" every frame. It
    /// subscribes to the manager's events and reacts. That direction of dependency
    /// matters: the game rules don't know a HUD exists, so you could delete this file and
    /// the game would still work. Try to keep that property as you add systems.
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        [SerializeField] private Color panelColour = new Color(0.05f, 0.06f, 0.09f, 0.82f);
        [SerializeField] private Color flashColour = new Color(0.4f, 1f, 0.6f, 1f);

        [Tooltip("Drag the Game Manager here.")]
        [SerializeField] private GameManager manager;

        private GUIStyle timerStyle;
        private GUIStyle counterStyle;
        private GUIStyle titleStyle;
        private GUIStyle subStyle;
        private bool stylesReady;

        private float flashUntil = -1f;

        /// <summary>
        /// A serialized reference gets you the object, but never the subscription — an
        /// event hook-up is code, not data, so it cannot be saved in a scene file and has
        /// to be redone every time the game starts. Forgetting this is why a HUD wired in
        /// the Inspector will happily show the timer and then never react to a pickup.
        /// </summary>
        private void Start()
        {
            if (manager != null) Bind(manager);
        }

        /// <summary>Optional override for a runtime-created manager.</summary>
        public void Bind(GameManager gameManager)
        {
            // Detach from any previous manager first. Without this, rebinding would leave
            // a stale subscription behind and the flash would fire twice.
            if (manager != null) manager.CollectedChanged -= HandleCollectedChanged;

            manager = gameManager;
            if (manager != null) manager.CollectedChanged += HandleCollectedChanged;
        }

        // Always unsubscribe. An event holds a reference to the subscriber, so forgetting
        // this keeps destroyed objects alive and eventually throws when the event fires
        // into something that no longer exists. This is one of the most common sources of
        // "impossible" null reference exceptions in Unity projects.
        private void OnDestroy()
        {
            if (manager != null) manager.CollectedChanged -= HandleCollectedChanged;
        }

        private void HandleCollectedChanged(int collected, int total)
        {
            flashUntil = Time.time + 0.35f;
        }

        private void OnGUI()
        {
            if (manager == null) return;
            EnsureStyles();

            GUI.Label(
                new Rect(Screen.width * 0.5f - 120f, 8f, 240f, 40f),
                GameManager.FormatTime(manager.ElapsedTime),
                timerStyle);

            Color previous = GUI.color;
            if (Time.time < flashUntil) GUI.color = flashColour;

            GUI.Label(
                new Rect(Screen.width - 230f, 10f, 210f, 32f),
                manager.Collected + " / " + manager.TotalCollectibles,
                counterStyle);

            GUI.color = previous;

            if (manager.State == GameState.Won) DrawWinPanel();
        }

        private void DrawWinPanel()
        {
            const float width = 440f;
            const float height = 190f;

            Rect panel = new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f,
                width, height);

            Color previous = GUI.color;
            GUI.color = panelColour;
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = previous;

            GUI.Label(new Rect(panel.x, panel.y + 30f, panel.width, 44f),
                "All pickups collected", titleStyle);
            GUI.Label(new Rect(panel.x, panel.y + 84f, panel.width, 32f),
                "Time   " + GameManager.FormatTime(manager.ElapsedTime), subStyle);
            GUI.Label(new Rect(panel.x, panel.y + 130f, panel.width, 32f),
                "Press R to play again", subStyle);
        }

        /// <summary>
        /// GUIStyles are built once and cached. OnGUI is called several times per frame
        /// (once to work out layout, again to actually draw), so allocating styles inside
        /// it would churn out garbage continuously. GUI.skin only exists during OnGUI,
        /// which is why this can't just live in Awake.
        /// </summary>
        private void EnsureStyles()
        {
            if (stylesReady) return;

            timerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter
            };

            counterStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                alignment = TextAnchor.UpperRight
            };

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter
            };

            subStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.UpperCenter
            };

            stylesReady = true;
        }
    }
}
