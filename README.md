# android-porting

A Windows x64 tool for porting Android applications to editable desktop projects.
The first release targets libGDX games with a shared Java or Kotlin module.

The C# / .NET 10 CLI generates a Gradle project with an LWJGL3 desktop target
and reports compatibility issues. Build and packaging are separate from generation.
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
under `tests/fixtures/kotlin-libgdx`. The check also verifies that a regular
Android Gradle project and a libGDX Android module without a shared module fail
with diagnostics and leave no generated project.

```powershell
.\tests\Test-InspectPlan.ps1 -Cli $cli -TiltMaze 'C:\path\to\tilt-maze'
```

## Generate an editable desktop project

```powershell
$desktop = 'C:\path\to\new-desktop-project'
& $cli generate $source $desktop
```

`generate` takes the same port options as `plan`. It copies the project into a
new destination, adds an LWJGL3 module or keeps an existing one, and records
the desktop module in `porting-project.json`. It leaves the source directory
untouched and reports a conflict if the destination exists. The copy omits
Git metadata, Gradle and Kotlin caches, conventional Gradle build directories,
and the root `local.properties` machine settings.

A new launcher expects a shared entry class with a no-argument constructor.
It uses fixed libGDX versions read from the shared Gradle dependencies, adds
desktop Box2D natives when the shared module uses Box2D, and reads assets from
the desktop classpath in the planned directory order. Existing repository
configuration must be available to the new module. Project-specific input
and platform API adaptations are edits to make in the output copy. For Tilt
Maze, the two input changes are in the Windows desktop patch below.

## Build and launch the desktop target

Set `JAVA_HOME` to a JDK supported by your project's Gradle Wrapper. Set
`ANDROID_HOME` if the project's Android plugin configuration requires an SDK.
Run `build` against the output directory from `generate`.

```powershell
& $cli build $desktop
```

`build` runs the desktop module's `build` task through the output project's
Gradle Wrapper JAR and streams the build log. `--offline` uses an existing
Gradle dependency cache after the Wrapper distribution is available. The
command uses the project and user Gradle settings and Java environment.

To launch a newly generated `:lwjgl3` target, run its desktop task from the
output directory.

```powershell
Push-Location -LiteralPath $desktop
try {
    & "$env:JAVA_HOME\bin\java.exe" -cp 'gradle/wrapper/gradle-wrapper.jar' org.gradle.wrapper.GradleWrapperMain :lwjgl3:run
}
finally {
    Pop-Location
}
```

For a reused desktop module, substitute its module path from
`porting-project.json`.

### Windows x64 workflow verification

Run the workflow check from a Windows desktop session with PowerShell 7, Git,
JDK 21, and the Android SDK described below. Pass an unpatched Tilt Maze
source directory and a new verification directory. The check uses the same
Gradle Wrapper files for the included Kotlin fixture. Add `-Offline` after
the Wrapper distribution and build dependencies have been cached.

```powershell
.\tests\Test-DesktopWorkflow.ps1 -Cli $cli `
    -TiltMaze 'C:\path\to\tilt-maze' -WorkDirectory '.local\beta-verification'
```

The check verifies a failed copy and a clean retry, preserves an edited
launcher on a destination conflict, and compares the input file hashes before
and after the workflow. It applies the Tilt Maze keyboard changes to the Java
export, builds both desktop targets, and verifies a complete playthrough and
replay for each game. Tilt Maze uses frame-by-frame held-key input in LWJGL3.
The Kotlin check sends arrow keys to the game window and verifies the win and
replay through its title, which comes from the first configured asset directory.
The check also runs the Tilt Maze shared tests and builds both Android debug
APKs from the generated projects. The verification directory retains the
editable projects and logs. The offline Windows x64 run used Microsoft OpenJDK
21.0.7+6 and Gradle 8.14.3.

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
