param(
    [Parameter(Mandatory)] [string] $Cli,
    [Parameter(Mandatory)] [string] $TiltMazeApk,
    [Parameter(Mandatory)] [string] $ObbGameApk,
    [Parameter(Mandatory)] [string] $WorkDirectory,
    [switch] $Offline,
    [string] $SoftwareOpenGlDirectory
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = (Resolve-Path -LiteralPath "$PSScriptRoot/..").Path
$cliPath = (Resolve-Path -LiteralPath $Cli).Path
$tiltApk = (Resolve-Path -LiteralPath $TiltMazeApk).Path
$routeApk = (Resolve-Path -LiteralPath $ObbGameApk).Path
$root = [IO.Path]::GetFullPath($WorkDirectory)
$relativeRoot = [IO.Path]::GetRelativePath($repo, $root).Replace('\', '/')
if ($relativeRoot -in @('.', '..') -or $relativeRoot.StartsWith('../') -or [IO.Path]::IsPathRooted($relativeRoot)) {
    throw 'Choose a work directory inside the checkout so Git can apply the Tilt Maze sample patch.'
}
if (Test-Path -LiteralPath $root) {
    throw "Choose a new work directory. '$root' already exists."
}

$null = [IO.Directory]::CreateDirectory($root)
$gradleOptions = @()
if ($Offline) { $gradleOptions = @('--offline') }
if (!('Porting.Verification.WindowInput' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'verification/WindowInput.cs')
}

function Assert-Cli([string[]] $Command, [int] $ExpectedExit, [string] $ExpectedText, [string] $Log) {
    $lines = @(& $cliPath @Command 2>&1)
    $exitCode = $LASTEXITCODE
    Set-Content -LiteralPath (Join-Path $root "$Log.log") -Value $lines -Encoding utf8
    if ($exitCode -ne $ExpectedExit -or !($lines -join [Environment]::NewLine).Contains($ExpectedText)) {
        throw "Expected exit $ExpectedExit and '$ExpectedText' from '$($Command -join ' ')'. Got $exitCode. See $Log.log."
    }
}

function Assert-Window([Diagnostics.Process] $Process, [string] $Title) {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $Process.Refresh()
        if ($Process.MainWindowTitle -ceq $Title) { return }
        Start-Sleep -Milliseconds 100
    } while (!$Process.HasExited -and [DateTime]::UtcNow -lt $deadline)

    throw "Expected window '$Title'. Last title was '$($Process.MainWindowTitle)'."
}

function Assert-PackagedRoute([string] $Archive, [string] $Route, [string] $Name) {
    $image = Join-Path $root "$Name & image"
    [IO.Compression.ZipFile]::ExtractToDirectory($Archive, $image)
    if ($SoftwareOpenGlDirectory) {
        # Supply the VM's software graphics driver next to the unpacked application.
        Get-ChildItem -LiteralPath $SoftwareOpenGlDirectory -Filter '*.dll' -File | Copy-Item -Destination $image
    }
    $executable = Join-Path $image 'OBBRoute.exe'
    if (!(Test-Path -LiteralPath (Join-Path $image 'runtime/lib/modules') -PathType Leaf)) {
        throw "The packaged runtime is missing from '$image'."
    }

    $pe = [IO.BinaryReader]::new([IO.File]::OpenRead($executable))
    try {
        $pe.BaseStream.Position = 0x3c
        $pe.BaseStream.Position = $pe.ReadInt32()
        if ($pe.ReadUInt32() -ne 0x00004550 -or $pe.ReadUInt16() -ne 0x8664) {
            throw "The packaged executable is not Windows x64."
        }
    }
    finally {
        $pe.Dispose()
    }

    $profile = Join-Path $root "$Name profile"
    $start = [Diagnostics.ProcessStartInfo]::new($executable)
    $start.WorkingDirectory = $profile
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.Environment.Clear()
    $start.Environment['SystemRoot'] = $env:SystemRoot
    $start.Environment['WINDIR'] = $env:SystemRoot
    $start.Environment['PATH'] = Join-Path $env:SystemRoot 'System32'
    $start.Environment['USERPROFILE'] = $profile
    if ($SoftwareOpenGlDirectory) {
        $start.Environment['GALLIUM_DRIVER'] = 'llvmpipe'
    }
    foreach ($entry in @{ TEMP = 'Temp'; TMP = 'Temp'; LOCALAPPDATA = 'AppData/Local'; APPDATA = 'AppData/Roaming' }.GetEnumerator()) {
        $path = Join-Path $profile $entry.Value
        $null = [IO.Directory]::CreateDirectory($path)
        $start.Environment[$entry.Key] = $path
    }

    $launcher = [Diagnostics.Process]::Start($start)
    $process = $null
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        do {
            $process = Get-Process -Name 'OBBRoute' -ErrorAction SilentlyContinue | Where-Object {
                $_.Path -ieq $executable -and $_.MainWindowTitle -ceq 'OBB Route Ready'
            } | Select-Object -First 1
            if (!$process) { Start-Sleep -Milliseconds 100 }
        } while (!$process -and [DateTime]::UtcNow -lt $deadline)
        if (!$process) { throw "The packaged executable did not open the OBB Route Ready window." }
        $expectedJvm = Join-Path $image 'runtime/bin/server/jvm.dll'
        if (!($process.Modules | Where-Object FileName -IEQ $expectedJvm)) {
            throw 'The game did not load the bundled Java VM.'
        }

        $incorrect = if ($Route.StartsWith('R')) { 0x26 } else { 0x27 }
        if (![Porting.Verification.WindowInput]::Press($process.MainWindowHandle, $incorrect)) {
            throw 'Could not send an incorrect keyboard move to the OBB game.'
        }
        Assert-Window $process 'OBB Route Reset'
        for ($index = 0; $index -lt $Route.Length; $index++) {
            $key = if ($Route[$index] -eq 'R') { 0x27 } else { 0x26 }
            if (![Porting.Verification.WindowInput]::Press($process.MainWindowHandle, $key)) {
                throw 'Could not send a keyboard move to the OBB game.'
            }
            $title = if ($index -eq $Route.Length - 1) { "OBB Route Complete $Route" } else { "OBB Route Step $($index + 1)" }
            Assert-Window $process $title
        }
        if (!$process.CloseMainWindow() -or !$launcher.WaitForExit(10000) -or $launcher.ExitCode -ne 0) {
            throw "The packaged game did not exit cleanly."
        }
        Write-Output "The Windows x64 executable completed route $Route with only Windows system tools on PATH."
    }
    finally {
        if (!$launcher.HasExited) {
            $launcher.Kill($true)
            $launcher.WaitForExit()
        }
        if ($process -and !$process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        if ($process) { $process.Dispose() }
        $launcher.Dispose()
    }
}

$tilt = Join-Path $root 'Tilt Maze & recovered'
Assert-Cli @('generate', $tiltApk, $tilt) 0 'Recovered source files: 5' 'tilt-generate'
$patch = Join-Path $repo 'samples/tilt-maze/apk-desktop.patch'
& git -C $repo apply --include='**/SandboxScreen.java' --include='**/TitleScreen.java' "--directory=$relativeRoot/Tilt Maze & recovered/core" $patch
if ($LASTEXITCODE -ne 0) { throw 'Could not adapt the recovered Tilt Maze keyboard controls.' }
Assert-Cli (@('build', $tilt) + $gradleOptions) 0 'Built :lwjgl3' 'tilt-build'
$distribution = @(Get-ChildItem -LiteralPath "$tilt/lwjgl3/build/distributions" -Filter '*.zip' -File)
if ($distribution.Count -ne 1) { throw 'Expected one Tilt Maze application ZIP.' }
$tiltDist = Join-Path $root 'Tilt Maze distribution'
[IO.Compression.ZipFile]::ExtractToDirectory($distribution[0].FullName, $tiltDist)
$tiltLibraries = Join-Path (Get-ChildItem -LiteralPath $tiltDist -Directory).FullName 'lib/*'
$classes = [IO.Directory]::CreateDirectory((Join-Path $root 'Keyboard verification')).FullName
& "$env:JAVA_HOME/bin/javac.exe" -cp $tiltLibraries -d $classes "$PSScriptRoot/verification/TiltMazeKeyboardPlayback.java"
if ($LASTEXITCODE -ne 0) { throw 'Could not compile the Tilt Maze keyboard verifier.' }
$playback = @(& "$env:JAVA_HOME/bin/java.exe" -cp "$classes;$tiltLibraries" TiltMazeKeyboardPlayback 2>&1)
$playbackExit = $LASTEXITCODE
$playback | Set-Content -LiteralPath (Join-Path $root 'tilt-playback.log') -Encoding utf8
if ($playbackExit -ne 0 -or !(($playback -join [Environment]::NewLine).Contains('Enter replay reset the game after the win.'))) {
    throw 'Tilt Maze keyboard verification did not finish. See tilt-playback.log.'
}
Write-Output 'Tilt Maze reached WON and replayed using its recovered keyboard controls.'

$obb = Join-Path $root 'main.1.fixture.route.obb'
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $PSScriptRoot 'fixtures/obb-libgdx/obb'), $obb)
$opaqueDirectory = [IO.Directory]::CreateDirectory((Join-Path $root 'Attachments & originals')).FullName
$unknown = Join-Path $opaqueDirectory 'unknown.binformat'
$extensionless = Join-Path $opaqueDirectory 'payload'
[IO.File]::WriteAllBytes($unknown, [byte[]] @(0x40, 0x00, 0xFF, 0x03))
[IO.File]::WriteAllBytes($extensionless, [byte[]] @(0x4D, 0xCA, 0xFE))
$inputHashes = @(Get-FileHash -LiteralPath $tiltApk, $routeApk, $obb, $unknown, $extensionless)

$routeProject = Join-Path $root 'OBB game & editable'
Assert-Cli @('generate', $routeApk, $routeProject) 1 'apk.obb-missing' 'obb-missing'
if (Test-Path -LiteralPath $routeProject) { throw 'A failed generation published a project.' }
Assert-Cli @('plan', $routeApk, '--companion', $obb, '--require-asset', 'levels/missing.txt') 1 'apk.required-asset' 'asset-missing'

$conflict = Join-Path $root 'conflict'
$archive = [IO.Compression.ZipFile]::Open($conflict, [IO.Compression.ZipArchiveMode]::Create)
try {
    $content = [IO.StreamWriter]::new($archive.CreateEntry('levels/route.txt').Open())
    try { $content.Write('RRR') } finally { $content.Dispose() }
}
finally {
    $archive.Dispose()
}
Assert-Cli @('plan', $routeApk, '--companion', $obb, '--obb', $conflict) 1 'apk.asset-conflict' 'asset-conflict'

$generateRoute = @('generate', $routeApk, $routeProject, '--companion', $obb, '--companion', $opaqueDirectory,
    '--require-asset', 'levels/route.txt')
Assert-Cli $generateRoute 0 'Mapped resources: 1' 'obb-generate'
$resource = Join-Path $routeProject 'assets/levels/route.txt'
if ((Get-Content -LiteralPath $resource -Raw).Trim() -cne 'RUR') { throw 'The OBB route was not mapped.' }
$metadataPath = Join-Path $routeProject 'porting-apk-inputs.json'
$metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
if ($metadata.runtimeUse -ne 'unverified' -or @($metadata.companions).Count -ne 3 -or
    @($metadata.companions | Where-Object decodedFormat -eq 'zip-obb').Count -ne 1 -or
    $metadata.resources[0].input.containerPath -ne $obb) {
    throw 'The input manifest did not record retention, expansion decoding, and resource origin.'
}
foreach ($record in $metadata.companions) {
    $source = if ($record.input.containerPath) { Join-Path $record.input.containerPath $record.input.originalPath } else { $record.input.originalPath }
    $copy = Join-Path $routeProject $record.preservedPath
    if ((Get-FileHash -LiteralPath $copy).Hash -ne (Get-FileHash -LiteralPath $source).Hash -or
        (Get-FileHash -LiteralPath $copy).Hash -ne $record.input.sha256) {
        throw "Incorrect retained bytes for '$source'."
    }
}

Assert-Cli (@('build', $routeProject) + $gradleOptions) 0 'Built :lwjgl3' 'obb-build'
$originalArchive = Join-Path $root 'obb-original.zip'
Assert-Cli (@('package', $routeProject, $originalArchive, '--exe', 'OBBRoute') + $gradleOptions) 0 'Packaged ZIP:' 'obb-package'
Assert-PackagedRoute $originalArchive 'RUR' 'Original'

Set-Content -LiteralPath $resource -Value 'URU' -Encoding utf8
Assert-Cli $generateRoute 1 'generation.output-exists' 'output-conflict'
if ((Get-Content -LiteralPath $resource -Raw).Trim() -cne 'URU') { throw 'A conflicting generation changed the edited resource.' }
Assert-Cli (@('build', $routeProject) + $gradleOptions) 0 'Built :lwjgl3' 'obb-rebuild'
$editedArchive = Join-Path $root 'obb-edited.zip'
Assert-Cli (@('package', $routeProject, $editedArchive, '--exe', 'OBBRoute') + $gradleOptions) 0 'Packaged ZIP:' 'obb-repackage'
Assert-PackagedRoute $editedArchive 'URU' 'Edited'

foreach ($hash in $inputHashes) {
    if ((Get-FileHash -LiteralPath $hash.Path).Hash -ne $hash.Hash) { throw "The APK workflow changed '$($hash.Path)'." }
}
Write-Output 'Both APK fixture ports, ZIP expansion playthroughs, and edited executable verification passed.'
