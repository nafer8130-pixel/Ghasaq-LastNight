using Godot;

namespace Ghasaq.Game
{
    /// <summary>
    /// Persists <see cref="AccessibilitySettings"/> as a small config file in
    /// Godot's user directory (writable on Android), separate from save slots.
    ///
    /// A missing or unreadable file is not an error: it loads the defaults, so
    /// a player who has never opened the page and a player whose file was lost
    /// both get the shipped behaviour rather than a refusal.
    /// </summary>
    public static class SettingsStore
    {
        public const string DefaultPath = "user://settings.cfg";

        private const string Section = "accessibility";

        public static AccessibilitySettings Load(string path = DefaultPath)
        {
            var settings = new AccessibilitySettings();
            var config = new ConfigFile();
            Error error = config.Load(path);

            if (error == Error.FileNotFound)
            {
                return settings;
            }

            if (error != Error.Ok)
            {
                GD.PushWarning("Could not read accessibility settings from '" + path + "': " + error + ". Using defaults.");
                return settings;
            }

            settings.ReduceShake = config.GetValue(Section, "reduce_shake", settings.ReduceShake).AsBool();
            settings.DimmingDistortion = config.GetValue(Section, "dimming_distortion", settings.DimmingDistortion).AsBool();
            settings.ColorblindSafe = config.GetValue(Section, "colorblind_safe", settings.ColorblindSafe).AsBool();
            settings.FontScaleIndex = (int)config.GetValue(Section, "font_scale_index", settings.FontScaleIndex);
            settings.Normalize();
            return settings;
        }

        public static bool Save(AccessibilitySettings settings, string path = DefaultPath)
        {
            settings.Normalize();

            var config = new ConfigFile();
            config.SetValue(Section, "reduce_shake", settings.ReduceShake);
            config.SetValue(Section, "dimming_distortion", settings.DimmingDistortion);
            config.SetValue(Section, "colorblind_safe", settings.ColorblindSafe);
            config.SetValue(Section, "font_scale_index", settings.FontScaleIndex);

            Error error = config.Save(path);

            if (error != Error.Ok)
            {
                GD.PushWarning("Could not save accessibility settings to '" + path + "': " + error);
                return false;
            }

            return true;
        }
    }
}
