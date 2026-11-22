# android-porting

A Windows x64 tool for porting Android applications to editable desktop projects.
The first release targets libGDX games with a shared Java or Kotlin module.

The C# / .NET 10 CLI will generate a Gradle project with an LWJGL3 desktop target
and report compatibility issues. Build and packaging will be separate commands.
A WPF desktop interface will follow using the same porting core.

Open `android-porting.slnx` in Visual Studio or run
`dotnet build android-porting.slnx` with the .NET 10 SDK. The solution contains
the CLI entry point and the shared core library.

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

See [AGENTS.md](AGENTS.md) for development and review conventions.
