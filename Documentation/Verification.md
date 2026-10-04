# Verification status

This file records what has actually been **executed and observed**, and what has
not. It exists because "the code compiles" and "the tests pass" are not the same
claim as "the game works", and the difference matters.

## After the naming change (2026-10-04)

The product, its packages and its assemblies were renamed to غَسَق / GHASAQ and
the repository was swept for the retired words of the earlier working title
([Naming.md](Naming.md) is the contract). Everything below was re-executed
**after** that change, in this environment, with Godot 4.5-stable (.NET)
downloaded for the run:

| Check | Command | Result |
| --- | --- | --- |
| Naming gate | `bash Tools/check-naming.sh` | **Pass** — no retired word remains |
| Core purity gate | `bash Tools/check-core-purity.sh` | **Pass** |
| Core test suite | `bash Tools/test-core.sh` | **563 passed, 0 failed** (327 ms) |
| Godot project layout | `bash Tools/check-godot-project.sh` | **Pass** |
| Godot C# assembly builds | `dotnet build Ghasaq.csproj` | **Pass** — 0 warnings, 0 errors → `Ghasaq.dll` |
| Headless smoke test inside Godot | `bash Tools/test-godot.sh <godot>` | **Pass — 16/16 checks** |
| The main scene assembles and runs | `godot --headless --path . --quit-after 300` | **Pass** — `Ghasaq ready: region 'grey-wilds', 5 hostiles, 5 quests, level 1` |
| Android ARM64 APK | `bash Tools/build-android.sh` | **BUILD SUCCESS** — 103,542,168 bytes (98 MB) |

The fresh APK was then inspected with the Android SDK's own tools (`aapt`,
`unzip`, `apksigner`):

```
package: name='com.ghasaq.thelastnight' versionCode='1' versionName='1.0.0'
sdkVersion:'24'  targetSdkVersion:'35'
application-label:'غَسَق: الليلة الأخيرة'
native-code: 'arm64-v8a'
assets/.godot/mono/publish/arm64/Ghasaq.dll
assets/.godot/mono/publish/arm64/Ghasaq.Core.dll
Signer #1 certificate DN: CN=Godot, OU=Godot Engine, O=Stichting Godot, C=NL
```

Still not verified, because they need hardware: installing the APK on a physical
device, touch controls, frame rate, and the camera on real devices.

Both workflows have **now run on GitHub**, on the rename commit `f6832c2`:

| Workflow | Run | Result |
| --- | --- | --- |
| `ci.yml` | CI #2 | **success** — naming gate, core purity, 563 tests, layout, build, smoke test |
| `android.yml` | Android ARM64 #2 | **success** — the ARM64 APK was exported and uploaded on a GitHub runner |

The table below records the runs made **before** the renaming — including the
pre-rename proof that the core and the Godot layer were healthy; its command
names have been updated to the current identity.

## Slice budgets and camera (days 15–45), 2026-10-04

Two verifiable pieces of the slice have landed: the combat budgets from plan §7
are now code the build enforces, and the decided 3/4 camera has its first-draft
defaults.

| Check | Command | Result |
| --- | --- | --- |
| Naming gate | `bash Tools/check-naming.sh` | **Pass** |
| Core test suite (with the new budget tests) | `bash Tools/test-core.sh` | **567 passed, 0 failed** (365 ms) |
| Godot C# assembly builds | `dotnet build Ghasaq.csproj` | **Pass** — 0 warnings, 0 errors |
| Godot project layout | `bash Tools/check-godot-project.sh` | **Pass** |
| Headless smoke test inside Godot | `bash Tools/test-godot.sh <godot>` | **Pass** |
| The main scene assembles and runs | `godot --headless --path . --quit-after 300` | **Pass** — `Ghasaq ready: region 'grey-wilds', ...` |

New in code:

- `Core/Combat/CombatTuning.cs` — the plan's numbers in one place: telegraph
  floor 0.40 s, hit-stop band 40–80 ms, three hostiles on screen.
- `ContentValidator` now fails the build when a damaging enemy move is faster
  than the telegraph floor; the fault-injection test proves the rule fires. The
  shipped Cinder Maul was authored at 0.38 s and was raised to 0.40 s.
- Camera defaults for [CameraDecision.md](CameraDecision.md): pitch 50° (range
  20–60°), distance 11 m.

**Not measured:** the camera's readability and feel, the hit-stop animation
itself (the band is recorded in code; the view work is not done), and the 60 fps
budget on the target device. Those need eyes, hands and hardware.

## What has been run, and passed

All of the following were run in the environment this port was written in, which
has Godot 4.5 (.NET) but no Unity and no Unreal Engine.

| Check | Command | Result |
| --- | --- | --- |
| Core purity gate | `bash Tools/check-core-purity.sh` | **Pass** — the core is engine-free |
| Core compiles | `bash Tools/test-core.sh` | **Pass** — portable library, 0 warnings |
| Core test suite | `bash Tools/test-core.sh` | **563 passed, 0 failed** |
| Godot project layout is complete and engine-clean | `bash Tools/check-godot-project.sh` | **Pass** |
| Godot C# assembly builds | `dotnet build Ghasaq.csproj` | **Pass** — 0 warnings, 0 errors |
| Godot imports the project | `godot --headless --path . --import` | **Pass** |
| Headless smoke test inside Godot | `godot --headless --path . res://Tests/Godot/GodotSmoke.tscn` | **Pass — 15/15 checks** |
| The main scene assembles and runs | `godot --headless --path . --quit-after 300` | **Pass** — 300 physics frames, no errors |
| Android ARM64 APK | `bash Tools/build-android.sh` | **BUILD SUCCESS** — 98 MB validated APK |

### The headless smoke test

`Tests/Godot/GodotSmoke.cs` runs inside the engine and exercises the core through it:

```
ok - DeterministicRng(42) uint #1 / #2 / #3      (492690617, 1919685028, 3561993920)
ok - DeterministicRng(42) first float             (0.1147134)
ok - StableHash("a")                              (af63dc4c8601ec8c)
ok - world graph has five regions
ok - wilds starts with five hostiles
ok - a fight actually resolved (hostiles fell)
ok - the opening fight reached a conclusion
ok - the Sigilbearer took damage
ok - defeats granted experience
ok - save serialises to JSON / deserialises
ok - experience / inventory / position survive a save round-trip
```

It exits non-zero if any check fails, so CI classifies on it. The parity checks
pin the exact RNG values, so a future change that silently alters how a fight plays
out fails the build.

### The main scene

Running `scenes/Main.tscn` headless builds the whole game and logs:

```
Ghasaq ready: region 'grey-wilds', 5 hostiles, 5 quests, level 1
```

That is the assembled session: arena, player view, five enemy views, the HUD bound
to the session, the menu wired to the HUD's menu button, and the opening quest
offered.

## The Android APK

`Tools/build-android.sh` produced a real APK and validated it. Observed with
`aapt`, `unzip` and `apksigner`:

```
package: name='com.ghasaq.thelastnight' versionName='1.0.0'
sdkVersion:'24'  targetSdkVersion:'35'
android:screenOrientation = 0                 # landscape
lib/arm64-v8a/libgodot_android.so             # + 25 more arm64 libraries
assets/.godot/mono/publish/arm64/Ghasaq.dll
assets/.godot/mono/publish/arm64/Ghasaq.Core.dll
Signer #1 certificate DN: CN=Godot, OU=Godot Engine, O=Stichting Godot, C=NL
```

The APK is `build/android/ghasaq.apk` (98 MB) and is not committed (it is
gitignored).

### What the APK proves, and what it does not

Proven: the project configures, imports, compiles, publishes the .NET assembly, and
packages a signed, ARM64, landscape APK whose package name and target SDK match the
project's earlier targets.

Not proven: the APK has **not been installed on a physical device**. Touch controls,
frame rate and the camera on real hardware are untested. That requires a phone.

## What has NOT been run

| Not verified | Why | What it would take |
| --- | --- | --- |
| **Installing and playing the APK on a device** | Requires hardware. | Install the APK on a phone. |
| **Performance on target hardware** | Requires hardware. | Profile on a mid-range phone. |
| **A release-signed APK** | No release keystore exists in the repository. | Add one and set `GHASAQ_EXPORT_MODE=release`. |

The workflows' steps were validated locally first, and then on GitHub itself:
`ci.yml` and `android.yml` both succeeded on `f6832c2` (2026-10-04).

## Migration history (all engine migrations recorded honestly)

| Era | Engine layer | Core | Status |
| --- | --- | --- | --- |
| Original | Unity 6 (`Assets/`, `Packages/`, `ProjectSettings/`) | C# in `Core/` | Removed |
| Second | Unreal Engine 5 (`Ghasaq.uproject`, `Source/`, `Config/`) | C# in `Core/`; an incomplete C++ port | Removed. **Never compiled or run** — no Unreal Engine was available. |
| Current | Godot 4.5 (`project.godot`, `scripts/`, `scenes/`) | C# in `Core/` | Compiles, runs headless, and exports a validated APK |

The Unreal layer was removed without ever having been compiled; the Godot layer has
been. The engine-free core (and its 567 tests) carried through all three engines
unchanged, which is the entire point of the boundary.

## Reproducing the verification

```bash
# The engine-free core: purity gate, then the test suite.
bash Tools/test-core.sh

# The Godot project: layout, C# build, import, headless smoke test.
bash Tools/check-godot-project.sh
bash Tools/test-godot.sh /path/to/godot

# The Android ARM64 APK. Needs Godot (.NET) and the Android SDK.
ANDROID_HOME=/path/to/android-sdk JAVA_HOME=/path/to/jdk \
  GODOT_BIN=/path/to/godot bash Tools/build-android.sh
```
