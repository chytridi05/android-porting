# android-porting

Port Android libGDX games to Windows x64. The CLI adds an LWJGL3 desktop target
to an editable Gradle project from either a Gradle source tree or a supported APK.

[Download the Windows x64 CLI](https://github.com/chytridi05/android-porting/releases/latest)

## Gradle source quick start

Extract the CLI ZIP and open PowerShell in that folder. Point the CLI at the
folder containing `settings.gradle` or `settings.gradle.kts` and choose a new
folder for the desktop copy.

```powershell
$source = 'C:\projects\my-game'
$desktop = 'C:\projects\my-game-desktop'
.\Porting.Cli.exe inspect $source
.\Porting.Cli.exe plan $source
.\Porting.Cli.exe generate $source $desktop
```

`plan` previews the desktop target and reports compatibility issues. See
`.\Porting.Cli.exe plan --help` to set the game entry, assets, or window size.

## Build and share

Set `JAVA_HOME` to a Windows x64 JDK supported by your Gradle Wrapper. Set
`ANDROID_HOME` if the Android plugin requires an SDK. Make game-specific edits,
such as adding keyboard controls, in the desktop copy.

```powershell
.\Porting.Cli.exe build $desktop
.\Porting.Cli.exe package $desktop 'C:\projects\my-game-win-x64.zip'
```

Extract the game ZIP and run `Launch.cmd`. The game ZIP includes Java, and
the CLI ZIP includes .NET.

## Example

See how Tilt Maze was adapted for keyboard play on Windows.
[Tilt Maze walkthrough](https://github.com/chytridi05/android-porting/blob/v0.1.0/README.md#tilt-maze-windows-desktop-port)

[Recover Tilt Maze from its APK](samples/tilt-maze/apk-recovery.md).

To build the CLI from source, run `dotnet build android-porting.slnx -c Release -p:Platform=x64` with the .NET 10 SDK.

## APK quick start

Use the `0.2.0` CLI line with an authorized standalone APK. Set `JAVA_HOME`
to a Windows x64 JDK 21, `PORTING_JADX_JAR` to `jadx-1.5.3-all.jar`, and
`GRADLE_HOME` to an unpacked Gradle 8.14.3 distribution. The generator copies
the distribution's Gradle Wrapper after verifying its checksum. [JADX releases](https://github.com/skylot/jadx/releases/tag/v1.5.3) · [Gradle checksums](https://gradle.org/release-checksums/)

```powershell
$apk = 'C:\inputs\game.apk'
$desktop = 'C:\projects\game-desktop'
$attachments = 'C:\inputs\attachments'
.\Porting.Cli.exe inspect $apk --companion $attachments
.\Porting.Cli.exe plan $apk --companion $attachments
.\Porting.Cli.exe generate $apk $desktop --companion $attachments
.\Porting.Cli.exe build $desktop
.\Porting.Cli.exe package $desktop 'C:\projects\game-win-x64.zip' --exe Game
```

Repeat `--companion` for each file or directory. Omit it when the game has no
attachments. The port recognizes ZIP-based `main.<versionCode>.<package>.obb`
and `patch.<versionCode>.<package>.obb` files. Use `--obb <file>` for a renamed
expansion archive and `--require-asset <path>` to check for a required
case-sensitive resource. Resource paths are relative to the APK's `assets/`
directory or the root of an OBB. Conflicting resources block generation.

Edit the recovered sources in `core/src/main/java` and resources in `assets`,
then run `build` and `package` again with a new ZIP path. Original companion
bytes are under `porting-inputs/`. `porting-apk-inputs.json` records origin,
SHA-256, archive decoding, and resource mapping. Verify resource use by playing
the export. Extract the whole ZIP and run `Game.exe` with its private Java
runtime. [JDK application images](https://docs.oracle.com/en/java/javase/21/jpackage/packaging-overview.html)

The APK workflow covers Java recovered by JADX from a libGDX launcher that
constructs its game entry without arguments. `--entry` overrides detection
with a fully qualified class. The validated inputs use libGDX 1.14.1, with
Box2D on Tilt Maze. Games with Android-specific controls may require edits
such as the [Tilt Maze keyboard adaptation](samples/tilt-maze/apk-recovery.md).
Split APKs and proprietary attachment decoders are later milestones.

### Verify two APK ports on Windows x64

Use PowerShell 7 and an interactive Windows x64 desktop. Use the Tilt Maze
APK from the recovery walkthrough and build the OBB fixture with Gradle
8.14.3 and an Android SDK with Platform 36 and Build Tools 35.0.0.

```powershell
$cli = 'C:\path\to\Porting.Cli.exe'
$tiltApk = 'C:\inputs\tilt-maze.apk'
$fixture = '.local/obb-fixture-source'
Copy-Item -LiteralPath 'tests/fixtures/obb-libgdx' -Destination $fixture -Recurse
& "$env:GRADLE_HOME/bin/gradle.bat" -p $fixture assembleDebug
$routeApk = "$fixture/build/outputs/apk/debug/obb-libgdx-fixture-debug.apk"
.\tests\Test-ApkWorkflow.ps1 -Cli $cli -TiltMazeApk $tiltApk -ObbGameApk $routeApk `
    -WorkDirectory '.local/apk-release-verification'
```

The test recovers and plays Tilt Maze, checks missing and conflicting
attachments and compares retained bytes. It packages the OBB game twice,
changes the recovered route from `RUR` to `URU`, and verifies both routes
with keyboard input to the Windows x64 executables. Each executable runs with
a separate profile and only Windows system tools on `PATH`. Add `-Offline`
when the Wrapper distribution and dependencies are already cached. For release
qualification, run this on a clean Windows x64 host with the documented tools.
