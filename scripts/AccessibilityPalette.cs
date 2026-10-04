using Godot;

namespace Ghasaq.Game
{
    /// <summary>
    /// The two colour languages the HUD can speak: the shipped palette and the
    /// colour-blind-safe one (plan section 6: عمى الألوان).
    ///
    /// The safe palette is the Okabe-Ito set, chosen so its colours keep their
    /// distance under the common forms of colour blindness. Where a hue still
    /// carries a state - a critical blow versus a normal one - the safe mode
    /// adds a shape cue as well, because no palette can carry everything: the
    /// suffix "!" is that cue. Everything here is arithmetic on colours and
    /// strings, so the smoke test can assert it without drawing.
    /// </summary>
    public static class AccessibilityPalette
    {
        // Okabe-Ito: vermillion, orange, sky blue, reddish purple.
        private static readonly Color Vermillion = new Color(0.84f, 0.37f, 0f);
        private static readonly Color Orange = new Color(0.90f, 0.62f, 0f);
        private static readonly Color SkyBlue = new Color(0.34f, 0.71f, 0.91f);
        private static readonly Color ReddishPurple = new Color(0.80f, 0.47f, 0.65f);

        /// <summary>The health bar's fill.</summary>
        public static Color HealthFill(bool colorblindSafe)
        {
            return colorblindSafe
                ? new Color(Vermillion.R, Vermillion.G, Vermillion.B, 0.95f)
                : new Color(0.62f, 0.16f, 0.16f, 0.95f);
        }

        /// <summary>The stamina bar's fill.</summary>
        public static Color StaminaFill(bool colorblindSafe)
        {
            return colorblindSafe
                ? new Color(SkyBlue.R, SkyBlue.G, SkyBlue.B, 0.95f)
                : new Color(0.30f, 0.52f, 0.58f, 0.95f);
        }

        /// <summary>The Soot bar's Dimming fill: red by default, purple in safe mode.</summary>
        public static Color SootFill(Color clear, float dimmingFraction, bool colorblindSafe)
        {
            Color dimmed = colorblindSafe
                ? new Color(ReddishPurple.R, ReddishPurple.G, ReddishPurple.B, 0.95f)
                : new Color(0.74f, 0.22f, 0.18f, 0.95f);

            return clear.Lerp(dimmed, Mathf.Clamp(dimmingFraction, 0f, 1f));
        }

        /// <summary>The floating damage number's colour; a safe-mode critical is blue, carried further by the "!" mark.</summary>
        public static Color Damage(bool critical, bool colorblindSafe)
        {
            if (colorblindSafe)
            {
                return critical ? SkyBlue : Orange;
            }

            return critical ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.35f, 0.28f);
        }

        /// <summary>The text of a floating number; safe mode marks a critical with a shape as well as a colour.</summary>
        public static string DamageText(float amount, bool critical, bool colorblindSafe)
        {
            string text = Mathf.RoundToInt(amount).ToString();

            return critical && colorblindSafe ? text + "!" : text;
        }

        /// <summary>The colour a body warms toward while a blow winds up.</summary>
        public static Color Telegraph(bool colorblindSafe)
        {
            return colorblindSafe
                ? new Color(SkyBlue.R, SkyBlue.G, SkyBlue.B, 1f)
                : new Color(0.98f, 0.42f, 0.2f);
        }
    }
}
