# android-porting

Port Android libGDX games to Windows x64. The CLI makes an editable Gradle
copy of your Java or Kotlin project with an LWJGL3 desktop target.

[Download the Windows x64 CLI](https://github.com/chytridi05/android-porting/releases/latest)

## Quick start

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

## APK inspection in development builds

Pass an authorized APK and repeat `--companion` for files or directories.
Inspection reports DEX entries, native libraries, APK assets, and SHA-256
hashes for the APK and supplied files.

```powershell
.\Porting.Cli.exe inspect 'C:\inputs\game.apk' --companion 'C:\inputs\attachments'
```
