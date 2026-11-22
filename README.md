# android-porting

A Windows x64 tool for porting Android applications to editable desktop projects.
The first release targets libGDX games with a shared Java or Kotlin module.

The C# / .NET 10 CLI will generate a Gradle project with an LWJGL3 desktop target
and report compatibility issues. Build and packaging will be separate commands.
A WPF desktop interface will follow using the same porting core.

Open `android-porting.slnx` in Visual Studio or run
`dotnet build android-porting.slnx` with the .NET 10 SDK. The solution contains
the CLI entry point and the shared core library.

## Inspect and plan a source project

Build the CLI and pass the directory that contains `settings.gradle` or
`settings.gradle.kts`. These commands read project files without running Gradle.

```powershell
dotnet build android-porting.slnx -c Release -p:Platform=x64
$cli = Resolve-Path 'src/Porting.Cli/bin/x64/Release/net10.0-windows/Porting.Cli.exe'
$source = 'C:\path\to\libgdx-project'
& $cli inspect $source
& $cli plan $source
& $cli plan $source --entry com.example.MyGame --asset assets --width 1280 --height 720
```

`inspect` prints the detected modules, launcher, and asset paths. `plan`
previews the desktop target, proposed file changes, and Gradle build tasks.
Use repeated `--asset` options to set the search order or `--no-assets` for an
empty list. A blocking diagnostic exits with code 1. Invalid command arguments
exit with code 2. `--help` lists the available options.

To repeat the Java and Kotlin inspection and planning checks in PowerShell 7,
point `-TiltMaze` to an **unpatched** copy of the pinned source described below.
The Kotlin fixture and its mapped modules and asset directories are included
under `tests/fixtures/kotlin-libgdx`.

```powershell
.\tests\Test-InspectPlan.ps1 -Cli $cli -TiltMaze 'C:\path\to\tilt-maze'
```

## Tilt Maze Android baseline

The `0.0.1` manual port uses Elia Youssef's MIT-licensed Tilt Maze at revision
`66f95e4708d2f2bda7fc02fa48b9ad66556ef262`.
[Pinned source](https://github.com/Elia-Youssef/libGDX-2d-tilt-maze/tree/66f95e4708d2f2bda7fc02fa48b9ad66556ef262)
· [MIT license](https://github.com/Elia-Youssef/libGDX-2d-tilt-maze/blob/66f95e4708d2f2bda7fc02fa48b9ad66556ef262/LICENSE)

On Windows x64, set `JAVA_HOME` to a JDK 21 installation and `ANDROID_HOME` to
an SDK containing Android Platform 36 and Build Tools 35.0.0. The pinned
project uses Gradle Wrapper 8.14.3, Android Gradle Plugin 8.9.1, and libGDX with
Box2D 1.14.1. Run the following in PowerShell from a fresh copy of the pinned
source project.

```powershell
.\gradlew.bat --no-daemon --no-build-cache :core:test :android:assembleDebug
```

The first run downloads Gradle and its dependencies. Add `--offline` for a
repeat with a populated cache. The test reports are under
`core/build/test-results/test/` and the APK is at
`android/build/outputs/apk/debug/android-debug.apk`.

The offline verification used Microsoft OpenJDK 21.0.7+6 in a fresh copy on
Windows x64. All 12 headless tests passed. The debug APK identified itself as
`com.micro1.maze` version `1.0`, with minimum API 21 and target API 36.
`apksigner` verified the v2 signature. The APK contains libGDX and Box2D native
libraries for `arm64-v8a`, `armeabi-v7a`, `x86`, and `x86_64`.

The build completed with notices about newer SDK metadata, unstripped native
libraries, and deprecated Gradle features.

The Android launcher starts `com.micro1.maze.MazeGame` in landscape. Touch
starts, resets, or replays the game, and the accelerometer drives the Box2D
simulation. The shared game draws the maze and interface in code, so the sample
is asset-free.

## Tilt Maze Windows desktop port

Apply the [desktop patch](samples/tilt-maze/windows-desktop.patch) from the
root of a fresh Git checkout of the pinned Tilt Maze revision. Use the same
JDK and Android SDK setup as the Android baseline. Set `$patch` to the
absolute path to the patch in this repository.

```powershell
$patch = 'C:\path\to\android-porting\samples\tilt-maze\windows-desktop.patch'
git apply $patch
.\gradlew.bat :lwjgl3:build
.\gradlew.bat :lwjgl3:run
```

The `lwjgl3` module launches the existing game at 1280 × 720 and loads the
desktop libGDX and Box2D natives. In the Windows window, press Enter to start
or replay, hold the arrow keys to tilt the maze, and press R to reset the
ball during play. Android continues to use the accelerometer and touch.

### Windows validation

The committed patch was applied to a fresh copy of the pinned source and built
on Windows x64. All 12 shared tests passed, and the Android debug APK rebuilt.
Pressing Enter started the maze, and a keyboard playthrough reached the
`YOU WIN` screen with the `ENTER TO PLAY AGAIN` prompt.

A Windows LWJGL3 integration run fed held-key states to the game input API
frame by frame. It verified movement and reset, traversed the maze to `WON`,
and used Enter to replay from the starting position.

### Inspection and generation findings

- Read `settings.gradle` and the Android module dependencies to identify
  `:android` and its shared `:core` module. The Android launcher's call to
  `initialize(new MazeGame(), config)` identifies `com.micro1.maze.MazeGame`.
  An empty list of asset directories is valid for this project.
- Add an `:lwjgl3` application module that depends on `:core` and registers its
  launcher as the Gradle main class. Box2D needs its own desktop native JAR in
  addition to the libGDX desktop natives.
- Adapt input at both screens. `TitleScreen` handles starting the game, and
  `SandboxScreen` maps keyboard input to the existing `MazeWorld` tilt and
  reset methods. Support touch and accelerometer input on the Android backend.

See [AGENTS.md](AGENTS.md) for development and review conventions.
