#!/usr/bin/env bash
# -----------------------------------------------------------------------------
# Verifies the repository is a coherent Godot 4.5 project - and only a Godot
# project.
#
# This is the cheap structural gate: it proves the files a Godot build needs are
# present and wired to each other, and that nothing from the engines this
# project used to target is still lying around to confuse a build or a reader.
# It does NOT compile anything; that is what the CI build step and
# Tools/build-android.sh do.
#
# Exit 0 = layout is coherent. Exit 1 = something is missing or left over.
# -----------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

failures=0

fail() {
    echo "check-godot-project: FAIL - $1" >&2
    failures=1
}

# -----------------------------------------------------------------------------
# 1. Project descriptor and main scene.
# -----------------------------------------------------------------------------
if [ ! -f "$ROOT/project.godot" ]; then
    fail "project.godot is missing"
else
    if ! grep -q 'config/features=PackedStringArray("4.5", "C#' "$ROOT/project.godot"; then
        fail "project.godot does not declare the 4.5 C# feature set"
    fi

    if ! grep -q 'run/main_scene="res://scenes/Main.tscn"' "$ROOT/project.godot"; then
        fail "project.godot does not point at res://scenes/Main.tscn"
    fi

    if ! grep -q 'window/handheld/orientation=0' "$ROOT/project.godot"; then
        fail "project.godot does not lock the handheld orientation to Landscape"
    fi

    if ! grep -q 'project/assembly_name="Ghasaq"' "$ROOT/project.godot"; then
        fail "project.godot does not name the Ghasaq C# assembly"
    fi

    if ! grep -q 'theme/custom_font="res://assets/fonts/ghasaq-ui-font.tres"' "$ROOT/project.godot"; then
        fail "project.godot does not set the UI font (the Sigil lines are Arabic)"
    fi
fi

# -----------------------------------------------------------------------------
# 2. C# project and the presentation scripts.
# -----------------------------------------------------------------------------
if [ ! -f "$ROOT/Ghasaq.csproj" ]; then
    fail "Ghasaq.csproj is missing"
fi

# Godot's .NET Android export needs the solution to bundle the assembly.
if [ ! -f "$ROOT/Ghasaq.sln" ]; then
    fail "Ghasaq.sln is missing (Godot's .NET export requires it)"
fi

# The core and the test harness are a library and a harness, not game resources.
for ignore in Core Tests; do
    if [ ! -f "$ROOT/$ignore/.gdignore" ]; then
        fail "$ignore/.gdignore is missing (Godot would scan it as project resources)"
    fi
done

if [ ! -d "$ROOT/scripts" ]; then
    fail "scripts/ is missing"
fi

for script in GameRoot.cs CombatantView.cs PlayerView.cs EnemyView.cs Arena.cs \
    CameraRig.cs PlayerInputReader.cs PlayerDriver.cs OcclusionProvider.cs \
    Hud.cs GameMenu.cs BattleFeedback.cs HitSpark.cs GodotSaveStorage.cs CoordinateConvert.cs \
    AccessibilitySettings.cs AccessibilityPalette.cs SettingsStore.cs; do
    if [ ! -f "$ROOT/scripts/$script" ]; then
        fail "missing presentation script: scripts/$script"
    fi
done

# -----------------------------------------------------------------------------
# 3. Scenes.
# -----------------------------------------------------------------------------
for scene in Main.tscn Arena.tscn Player.tscn Enemy.tscn Hud.tscn GameMenu.tscn; do
    if [ ! -f "$ROOT/scenes/$scene" ]; then
        fail "missing scene: scenes/$scene"
    fi
done

# -----------------------------------------------------------------------------
# 3b. The UI font. Godot's built-in font has no Arabic glyphs, and the Sigil
#     verb and Price lines are Arabic-first (Documentation/Sigils.md). Without
#     these files the HUD would draw nothing where a Price must be.
# -----------------------------------------------------------------------------
for asset in assets/fonts/NotoSans-Regular.ttf assets/fonts/NotoSansArabic-Regular.ttf \
    assets/fonts/ghasaq-ui-font.tres assets/fonts/OFL.txt; do
    if [ ! -f "$ROOT/$asset" ]; then
        fail "missing UI font asset: $asset"
    fi
done

# -----------------------------------------------------------------------------
# 4. Android export preset: ARM64, landscape is a project setting.
# -----------------------------------------------------------------------------
if [ ! -f "$ROOT/export_presets.cfg" ]; then
    fail "export_presets.cfg is missing"
else
    if ! grep -q 'name="Android"' "$ROOT/export_presets.cfg"; then
        fail "export_presets.cfg has no Android preset"
    fi

    if ! grep -q 'architectures/arm64-v8a=true' "$ROOT/export_presets.cfg"; then
        fail "export_presets.cfg does not build for Android ARM64 (arm64-v8a)"
    fi

    if ! grep -q 'package/unique_name="com.ghasaq.thelastnight"' "$ROOT/export_presets.cfg"; then
        fail "export_presets.cfg does not use the com.ghasaq.thelastnight package"
    fi
fi

# -----------------------------------------------------------------------------
# 5. No engine leftovers from the migrations this project has been through.
# -----------------------------------------------------------------------------
if [ -e "$ROOT/Ghasaq.uproject" ] || [ -d "$ROOT/Source" ] || [ -d "$ROOT/Config" ]; then
    fail "Unreal leftovers are still present (Ghasaq.uproject / Source/ / Config/)"
fi

for leftover in Assets Packages ProjectSettings Library; do
    if [ -e "$ROOT/$leftover" ]; then
        fail "Unity leftover present: $leftover/"
    fi
done

if find "$ROOT" -name '*.asmdef' -type f | grep -q .; then
    fail "Unity assembly definition (.asmdef) files are still present"
fi

# Generated build artifacts belong in .gitignore, not the tree.
for artifact in Binaries Intermediate DerivedDataCache; do
    if [ -e "$ROOT/$artifact" ]; then
        fail "Unreal build artifact present (should be ignored): $artifact/"
    fi
done

if [ "$failures" -ne 0 ]; then
    exit 1
fi

echo "check-godot-project: OK (Godot 4.5 project layout is complete and engine-clean)"
