using UnityEngine;

namespace GravityFlip
{
    /// <summary>
    /// A deliberately primitive crosshair plus control hints, drawn with Unity's legacy
    /// immediate-mode GUI. Legacy IMGUI is not what you'd ship, but it needs zero scene
    /// setup, zero packages and zero font assets — which is exactly what you want while
    /// the mechanic is still being figured out. Replace it with real UI later.
    ///
    /// It turns green when you're aiming at something flippable. That feedback matters
    /// more than it sounds: without it, a failed flip is indistinguishable from a bug.
    /// </summary>
    public class Crosshair : MonoBehaviour
    {
        [SerializeField] private float size = 9f;
        [SerializeField] private float thickness = 2f;
        [SerializeField] private bool showHints = true;

        [Tooltip("Drag the Player here — the crosshair reads HasValidTarget off its " +
                 "GravityFlipper to decide whether to turn green.")]
        [SerializeField] private GravityFlipper flipper;

        /// <summary>Optional override for a runtime-spawned player.</summary>
        public void Bind(GravityFlipper target)
        {
            flipper = target;
        }

        private void OnGUI()
        {
            // The flipper gets disabled when you win. Hiding the crosshair and the control
            // hints along with it means the win panel isn't cluttered with UI for controls
            // that no longer do anything.
            if (flipper == null || !flipper.isActiveAndEnabled) return;

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;

            bool valid = flipper.HasValidTarget;

            Color previous = GUI.color;
            GUI.color = valid
                ? new Color(0.35f, 1f, 0.6f, 0.95f)
                : new Color(1f, 1f, 1f, 0.45f);

            GUI.DrawTexture(
                new Rect(cx - size, cy - thickness * 0.5f, size * 2f, thickness),
                Texture2D.whiteTexture);
            GUI.DrawTexture(
                new Rect(cx - thickness * 0.5f, cy - size, thickness, size * 2f),
                Texture2D.whiteTexture);

            GUI.color = previous;

            if (showHints)
            {
                // Bottom-left, so it doesn't collide with the timer the HUD draws along
                // the top edge.
                GUI.Label(new Rect(14f, Screen.height - 30f, 900f, 24f),
                    "WASD move    Space jump    Mouse look    F / Right-click flip gravity    Esc release mouse");
            }
        }
    }
}
