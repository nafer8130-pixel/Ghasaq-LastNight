using Godot;

namespace Ghasaq.Game
{
    /// <summary>
    /// What the player can change about how the game presents itself, and
    /// nothing else (plan section 6, phase B: إعدادات وصولية).
    ///
    /// These are view-layer preferences: the reduced shake, the Dimming's
    /// distortion, the text size and the colour-blind cues change what is
    /// drawn, never what is simulated. The core never reads this type, so no
    /// session rule can be made easier or harder from a settings row.
    ///
    /// The values are persisted per device by <see cref="SettingsStore"/>,
    /// separately from save slots: an accessibility preference belongs to the
    /// person holding the phone, not to a run.
    /// </summary>
    public sealed class AccessibilitySettings
    {
        /// <summary>The text sizes a press cycles through, as multipliers.</summary>
        public static readonly float[] FontScaleSteps = { 1f, 1.25f, 1.5f };

        /// <summary>How much of a camera impulse survives "reduce shake".</summary>
        public const float ReducedShakeScale = 0.2f;

        /// <summary>The default menu row size the text steps scale from.</summary>
        public const int BaseMenuFontSize = 16;

        /// <summary>Shake impulses are kept at a fifth when the player asked for less motion.</summary>
        public bool ReduceShake;

        /// <summary>
        /// The light vignette the Dimming draws. On by default, because it is
        /// part of how the state reads; the switch exists for the players it
        /// would bother (plan section 3.6: "اختياري يمكن إغلاقه").
        /// </summary>
        public bool DimmingDistortion = true;

        /// <summary>Swaps state colours for a colour-blind-safe palette and adds a non-colour critical mark.</summary>
        public bool ColorblindSafe;

        /// <summary>Index into <see cref="FontScaleSteps"/>.</summary>
        public int FontScaleIndex;

        /// <summary>The current text multiplier.</summary>
        public float FontScale => FontScaleSteps[Mathf.Clamp(FontScaleIndex, 0, FontScaleSteps.Length - 1)];

        /// <summary>The current text multiplier as the number the menu shows.</summary>
        public int FontScalePercent => Mathf.RoundToInt(FontScale * 100f);

        /// <summary>How much of a shake impulse reaches the camera.</summary>
        public float ShakeScale => ReduceShake ? ReducedShakeScale : 1f;

        /// <summary>Moves to the next text size, wrapping back to 100%.</summary>
        public void CycleFontScale()
        {
            FontScaleIndex = (FontScaleIndex + 1) % FontScaleSteps.Length;
        }

        /// <summary>Brings a value loaded from disk back into range.</summary>
        public void Normalize()
        {
            FontScaleIndex = Mathf.Clamp(FontScaleIndex, 0, FontScaleSteps.Length - 1);
        }
    }
}
