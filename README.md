# android-porting

A Windows x64 tool for porting Android applications to editable desktop projects.
The first release targets libGDX games with a shared Java or Kotlin module.

The C# / .NET 10 CLI will generate a Gradle project with an LWJGL3 desktop target
and report compatibility issues. Build and packaging will be separate commands.
A WPF desktop interface will follow using the same porting core.

The current milestone is a manual Windows port of Tilt Maze. Building the
original Android sample requires JDK 21, Android SDK Platform 36, and Build
Tools 35.0.0. Use the source project's Gradle Wrapper for game builds.

See [AGENTS.md](AGENTS.md) for development and review conventions.
