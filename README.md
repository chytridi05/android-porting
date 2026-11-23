# android-porting

Port Android libGDX games to Windows x64. The desktop app exports editable
Gradle projects from supported APKs. The CLI also accepts Gradle source trees.

[Windows x64 downloads](https://github.com/chytridi05/android-porting/releases/latest)

## Desktop quick start

Extract the `Porting.Desktop-win-x64.zip` release and run
`Porting.Desktop.exe`. For APK recovery, use the JDK, JADX, and Gradle versions
in [APK quick start](#apk-quick-start). Set `JAVA_HOME` before launching the
app. Set `PORTING_JADX_JAR` and `GRADLE_HOME` in the same environment or choose
the JAR and Gradle folder under **APK port options**.

1. Open **APK input** and choose a supported, authorized APK. Use **Add files**
   and **Add folder** for companions, or paste each path into the companion
   field. Use **Add ZIP OBB** or **As ZIP OBB** for a renamed ZIP expansion.
2. Choose **Inspect** to see paths, origins, and SHA-256 hashes. Choose
   **Plan port** to preview source recovery and check resource requirements.
   **Diagnostics** lists the severity, code, and location of each finding.
3. Enter a new project folder and choose **Generate project**. Edit the
   recovered Java files and assets in that folder as needed.
4. Under **Build and package**, choose **Build project**. Enter a new ZIP
   path and choose **Package ZIP**. Use the executable option for an EXE
   or clear it for `Launch.cmd`. Extract the entire ZIP before launching
   the game with its bundled Java runtime.

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

Use the CLI with an authorized standalone APK. Set `JAVA_HOME`
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
Planning reports unsupported APK engines and explicitly supplied expansion
files outside the ZIP format before source recovery. Split APKs, native Android
engines, and proprietary attachment decoders are later milestones.

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
with keyboard input to the Windows x64 executables. Add `-MouseOnly` to build
Tilt Maze and play both OBB routes with mouse clicks. Each executable runs with
a separate profile and only Windows system tools on `PATH`. Add `-Offline`
when the Wrapper distribution and dependencies are already cached. For release
qualification, run this on a clean Windows x64 host with the documented tools.

### Verify equivalent desktop results

On an interactive Windows x64 desktop, supply the extracted WPF release with
`-Desktop 'C:\path\to\Porting.Desktop.exe'` when running
`Test-ApkWorkflow.ps1` above. The test drives the WPF window through Windows
UI Automation, compares SHA-256 hashes for every file in both editable
exports, checks failure diagnostics and cancellation, and plays the
original and edited OBB routes from ZIPs produced through the window.

### Publish the desktop release locally

Publish into a new output folder from the repository root.

```powershell
$publish = '.local/desktop-win-x64'
dotnet publish src/Porting.Desktop/Porting.Desktop.csproj -c Release -r win-x64 `
    -p:Platform=x64 --self-contained true -o $publish
[IO.Compression.ZipFile]::CreateFromDirectory(
    (Resolve-Path $publish).Path, [IO.Path]::GetFullPath('.local/Porting.Desktop-win-x64.zip'))
```
