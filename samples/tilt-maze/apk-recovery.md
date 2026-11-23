# Recover Tilt Maze from its APK

This Windows x64 walkthrough starts with the debug APK built from the [Tilt
Maze baseline at `66f95e4`](https://github.com/chytridi05/android-porting/blob/v0.1.0/README.md#tilt-maze-android-baseline).
The tested APK has package `com.micro1.maze`, version `1.0`, and SHA-256
`7948b2e51fd371f68e165c0e443ae959626fd45ab5556ad25dbacf2aadfdb0ab`.
Record the hash of your authorized APK and keep a fresh copy for the recovery.

## Recover the desktop sources

Run these commands from the `android-porting` repository root in PowerShell 7.
Use a new work directory, JADX 1.5.3 on `PATH`, and a Windows x64 JDK 21.

```powershell
$apk = 'C:\path\to\android-debug.apk'
$work = '.local/tilt-maze-apk-recovery'
$desktop = "$work/desktop"
$package = 'src/main/java/com/micro1/maze'

Get-FileHash -Algorithm SHA256 -LiteralPath $apk
jadx --threads-count 4 -d "$work/jadx" $apk
New-Item -ItemType Directory -Path "$desktop/$package"
foreach ($name in 'FontUtil', 'MazeGame', 'MazeWorld', 'SandboxScreen', 'TitleScreen') {
    Copy-Item -LiteralPath "$work/jadx/sources/com/micro1/maze/$name.java" `
        -Destination "$desktop/$package"
}
git apply --check --directory=$desktop samples/tilt-maze/apk-desktop.patch
git apply --directory=$desktop samples/tilt-maze/apk-desktop.patch
```

JADX reported 15 errors while decompiling the full APK. All five classes
copied above were emitted and compiled. Inspect those files for decompiler
errors if repeating the recovery on a different APK or JADX version.

- The decoded manifest names `com.micro1.maze.android.AndroidLauncher` as the
  launcher. Its `onCreate` in `classes4.dex` calls `initialize(new MazeGame(),
  config)`. The recovered game classes come from `classes2.dex`.
- `com.badlogic.gdx.Version` in the APK supplies the libGDX version `1.14.1`.
  `MazeWorld` imports Box2D and the APK contains `libgdx-box2d.so`. The Gradle
  project uses matching libGDX and Box2D desktop artifacts and their natives.
  The APK has no `assets/` entries. The application draws the maze in code.
- The patch adds the Gradle application and LWJGL3 launcher. It maps Enter to
  starting and replay, held arrows to tilt, and R to reset. Use the original
  source only after recovery to compare behavior.

## Build and verify on Windows

With Gradle 8.14.3 on `PATH` and `JAVA_HOME` set to JDK 21, build and launch.
Add `--offline` once the Gradle dependencies are cached.

```powershell
gradle -p $desktop build installDist
gradle -p $desktop run
```

To repeat the keyboard playback, compile the existing verifier against the
recovered application's installed JARs and run it through LWJGL3.

```powershell
$lib = "$((Resolve-Path -LiteralPath $desktop).Path)/build/install/tilt-maze-apk/lib/*"
$classes = "$work/keyboard-classes"
New-Item -ItemType Directory -Path $classes
& "$env:JAVA_HOME/bin/javac.exe" -cp $lib -d $classes tests/verification/TiltMazeKeyboardPlayback.java
& "$env:JAVA_HOME/bin/java.exe" -cp "$classes;$lib" TiltMazeKeyboardPlayback
```

On Windows x64 with Microsoft OpenJDK 21.0.7+6 and Gradle 8.14.3, `build
installDist` succeeded. The playback supplied held-key states through the
game's input API. It started with Enter, moved right, reset with R, traversed
all nine intermediate waypoints, reached `WON`, and replayed from the start.

For comparison with the original source, the 12 `MazeWorldTest` cases from
the pinned revision were run against the recovered desktop project. All 12
passed. The original test code was added to a separate verification copy
after the APK-only build and playthrough.
