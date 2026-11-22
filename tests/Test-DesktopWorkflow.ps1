param(
    [Parameter(Mandatory)] [string] $Cli,
    [Parameter(Mandatory)] [string] $TiltMaze,
    [Parameter(Mandatory)] [string] $WorkDirectory,
    [switch] $Offline
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$cliPath = (Resolve-Path -LiteralPath $Cli).Path
$javaSource = (Resolve-Path -LiteralPath $TiltMaze).Path
$root = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($WorkDirectory)
$relative = [IO.Path]::GetRelativePath($javaSource, $root)
if ($relative -ne '..' -and !$relative.StartsWith('..\') -and ![IO.Path]::IsPathRooted($relative)) {
    throw 'Choose a verification directory outside the Tilt Maze source.'
}
if (Test-Path -LiteralPath $root) {
    throw "Choose a new verification directory. '$root' already exists."
}

function Assert-Cli([string[]] $Command, [int] $ExpectedExitCode, [string] $ExpectedPattern, [string] $LogName = '') {
    $lines = @(& $cliPath @Command 2>&1)
    $actualExitCode = $LASTEXITCODE
    $output = $lines -join "`n"
    if ($LogName) {
        Set-Content -LiteralPath (Join-Path $root $LogName) -Value $output -Encoding utf8
    }
    if ($actualExitCode -ne $ExpectedExitCode -or $output -cnotmatch $ExpectedPattern) {
        throw "Unexpected result for $($Command -join ' '), exit $actualExitCode.`n$output"
    }
}

function Get-SourceInventory([string] $Directory) {
    Get-ChildItem -LiteralPath $Directory -Recurse -Force -File | Get-FileHash
}

function Assert-Launch([string] $Project, [string] $Name, [string] $MainClass, [string] $Title) {
    $archives = @(Get-ChildItem -LiteralPath (Join-Path $Project 'lwjgl3/build/distributions') -Filter '*.zip' -File)
    if ($archives.Count -ne 1) {
        throw "Expected one LWJGL3 distribution archive in '$Project'."
    }

    $installed = Join-Path $root $Name
    [IO.Compression.ZipFile]::ExtractToDirectory($archives[0].FullName, $installed)
    $distributions = @(Get-ChildItem -LiteralPath $installed -Directory)
    if ($distributions.Count -ne 1) {
        throw "Expected one application directory in '$installed'."
    }

    $java = if ($env:JAVA_HOME) { Join-Path $env:JAVA_HOME 'bin/java.exe' } else { (Get-Command java.exe).Source }
    $start = [Diagnostics.ProcessStartInfo]::new($java)
    $start.WorkingDirectory = $distributions[0].FullName
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('-cp', (Join-Path $start.WorkingDirectory 'lib/*'), $MainClass)) {
        $start.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        do {
            $process.Refresh()
            $observed = $process.MainWindowTitle
            if ($observed -ceq $Title) { break }
            Start-Sleep -Milliseconds 250
        } while (!$process.HasExited -and [DateTime]::UtcNow -lt $deadline)

        if ($observed -cne $Title) {
            throw "Expected the '$Title' application window. See '$Name.log' in '$root'."
        }
        if (!$process.CloseMainWindow() -or !$process.WaitForExit(10000) -or $process.ExitCode -ne 0) {
            throw "The '$Title' application did not close cleanly. See '$Name.log' in '$root'."
        }
        Write-Output "Observed and closed window '$observed'."
    }
    finally {
        if (!$process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        Set-Content -LiteralPath (Join-Path $root "$Name.log") -Value @(
            $stdout.GetAwaiter().GetResult(), $stderr.GetAwaiter().GetResult()
        ) -Encoding utf8
        $process.Dispose()
    }
}

$null = [IO.Directory]::CreateDirectory($root)
$kotlinSource = Join-Path $root 'Kotlin source'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/kotlin-libgdx') -Destination $kotlinSource -Recurse
# The fixture and the pinned Java sample declare the same Wrapper distribution.
Copy-Item -LiteralPath (Join-Path $javaSource 'gradle/wrapper/gradle-wrapper.jar') -Destination (Join-Path $kotlinSource 'gradle/wrapper')
Copy-Item -LiteralPath (Join-Path $javaSource 'gradlew'), (Join-Path $javaSource 'gradlew.bat') -Destination $kotlinSource

$lockPath = Join-Path $kotlinSource 'retry-fixture.txt'
Set-Content -LiteralPath $lockPath -Value 'Source file used by the copy retry check.'
$before = @(Get-SourceInventory $javaSource) + @(Get-SourceInventory $kotlinSource)
$javaOutput = Join-Path $root 'Tilt Maze & desktop'
$kotlinOutput = Join-Path $root 'Kotlin desktop'
$kotlinGenerate = @('generate', $kotlinSource, $kotlinOutput, '--entry', 'fixture.game.AlternateGame',
    '--asset', 'assets/alternate', '--asset', 'assets/default', '--width', '800', '--height', '600')

$lock = [IO.File]::Open($lockPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
try {
    Assert-Cli $kotlinGenerate 1 'generation\.write-failed' 'copy-failure.log'
}
finally {
    $lock.Dispose()
}
if ((Test-Path -LiteralPath $kotlinOutput) -or @(Get-ChildItem -LiteralPath $root -Force -Filter '.porting-*').Count -ne 0) {
    throw 'Failed generation left a destination or temporary project behind.'
}

Assert-Cli $kotlinGenerate 0 'Generated project:' 'kotlin-generate.log'
Assert-Cli @('generate', $javaSource, $javaOutput) 0 'Generated project:' 'java-generate.log'
Assert-Cli @('generate', $javaSource, (Join-Path $javaSource 'nested-desktop')) 1 'generation\.output-overlap'
if (Test-Path -LiteralPath (Join-Path $javaSource 'nested-desktop')) {
    throw 'Generation created a destination inside the input project.'
}

$launcher = Join-Path $javaOutput 'lwjgl3/src/main/java/com/micro1/maze/DesktopLauncher.java'
Add-Content -LiteralPath $launcher -Value '// Preserve this edit when generation reports a conflict.'
$launcherHash = (Get-FileHash -LiteralPath $launcher).Hash
Assert-Cli @('generate', $javaSource, $javaOutput) 1 'generation\.output-exists' 'copy-conflict.log'
if ((Get-FileHash -LiteralPath $launcher).Hash -ne $launcherHash) {
    throw 'A conflicting generation overwrote an edited launcher.'
}

Assert-Cli @('build', $javaSource) 1 'build\.project-metadata'
$buildOptions = if ($Offline) { @('--offline') } else { @() }
Assert-Cli (@('build', $javaOutput) + $buildOptions) 0 'BUILD SUCCESSFUL' 'java-build.log'
Assert-Launch $javaOutput 'java-launch' 'com.micro1.maze.DesktopLauncher' 'MazeGame'
Assert-Cli (@('build', $kotlinOutput) + $buildOptions) 0 'BUILD SUCCESSFUL' 'kotlin-build.log'
Assert-Launch $kotlinOutput 'kotlin-launch' 'fixture.game.DesktopLauncher' 'Kotlin fixture alternate assets'

$after = @(Get-SourceInventory $javaSource) + @(Get-SourceInventory $kotlinSource)
if (@(Compare-Object $before $after -Property Path, Hash).Count -gt 0) {
    throw 'Generation or build modified the input projects.'
}

Write-Output "Windows desktop generation, retry, conflict, build, and launch checks passed. Logs are under '$root'."
