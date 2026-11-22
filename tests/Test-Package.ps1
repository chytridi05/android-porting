param(
    [Parameter(Mandatory)] [string] $Cli,
    [Parameter(Mandatory)] [string] $Project,
    [Parameter(Mandatory)] [string] $WindowTitle,
    [Parameter(Mandatory)] [string] $WorkDirectory,
    [switch] $Offline
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$cliPath = (Resolve-Path -LiteralPath $Cli).Path
$projectPath = (Resolve-Path -LiteralPath $Project).Path
$root = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($WorkDirectory)
if (Test-Path -LiteralPath $root) { throw "Choose a new verification directory. '$root' already exists." }
$null = [IO.Directory]::CreateDirectory($root)
$archive = Join-Path $root 'Game.zip'
$options = if ($Offline) { @('--offline') } else { @() }

function Assert-Cli([string[]] $Command, [int] $ExitCode, [string] $Pattern, [string] $LogName = '') {
    $lines = @(& $cliPath @Command 2>&1)
    $actual = $LASTEXITCODE
    if ($LogName) {
        Set-Content -LiteralPath (Join-Path $root $LogName) -Value $lines -Encoding utf8
    }
    if ($actual -ne $ExitCode -or ($lines -join "`n") -cnotmatch $Pattern) {
        throw "Unexpected package result for $($Command -join ' '), exit $actual.`n$(($lines | Select-Object -Last 25) -join "`n")"
    }
}

Assert-Cli @('package', '--help') 0 'package <output-directory> <output-zip>'
Assert-Cli @('package', $projectPath) 2 'Specify the output ZIP path'
Assert-Cli @('package', $projectPath, (Join-Path $root 'game.txt')) 1 'package\.archive-path'
Assert-Cli @('package', $root, $archive) 1 'package\.project-metadata'
$originalJavaHome = $env:JAVA_HOME
try {
    $env:JAVA_HOME = Join-Path $root 'missing-jdk'
    Assert-Cli @('package', $projectPath, $archive) 1 'package\.jdk-missing'
}
finally {
    $env:JAVA_HOME = $originalJavaHome
}
if (Test-Path -LiteralPath $archive) { throw 'A failed package attempt created an archive.' }

$packageCommand = @('package', $projectPath, $archive) + $options
Assert-Cli $packageCommand 0 'Packaged ZIP:' 'package.log'
$archiveHash = (Get-FileHash -LiteralPath $archive).Hash
Assert-Cli $packageCommand 1 'package\.archive-exists' 'package-conflict.log'
if ((Get-FileHash -LiteralPath $archive).Hash -ne $archiveHash) {
    throw 'A conflicting package attempt changed the existing archive.'
}

$installed = Join-Path $root 'Extracted game & runtime'
[IO.Compression.ZipFile]::ExtractToDirectory($archive, $installed)
$bundledJava = Join-Path $installed 'runtime/bin/java.exe'
$launcher = Join-Path $installed 'Launch.cmd'
foreach ($file in @($bundledJava, $launcher, (Join-Path $installed 'runtime/lib/modules'))) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing packaged file '$file'." }
}

$testProfile = Join-Path $root 'Launch profile'
$temp = Join-Path $testProfile 'Temp'
$local = Join-Path $testProfile 'AppData/Local'
$roaming = Join-Path $testProfile 'AppData/Roaming'
foreach ($directory in @($temp, $local, $roaming)) {
    $null = [IO.Directory]::CreateDirectory($directory)
}
$systemDirectory = Join-Path $env:SystemRoot 'System32'
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $systemDirectory 'cmd.exe'))
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.WorkingDirectory = $testProfile
$start.Environment.Clear()
$start.Environment['SystemRoot'] = $env:SystemRoot
$start.Environment['WINDIR'] = $env:SystemRoot
$start.Environment['PATH'] = $systemDirectory
$start.Environment['TEMP'] = $temp
$start.Environment['TMP'] = $temp
$start.Environment['USERPROFILE'] = $testProfile
$start.Environment['LOCALAPPDATA'] = $local
$start.Environment['APPDATA'] = $roaming
$start.Environment['PORTING_TEST_LAUNCHER'] = $launcher
$start.Arguments = '/d /v:off /s /c ""%PORTING_TEST_LAUNCHER%""'

$shell = [Diagnostics.Process]::Start($start)
$game = $null
try {
    $stdout = $shell.StandardOutput.ReadToEndAsync()
    $stderr = $shell.StandardError.ReadToEndAsync()
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $game = Get-Process -Name java -ErrorAction SilentlyContinue | Where-Object {
            $_.Path -ieq $bundledJava -and $_.MainWindowTitle -ceq $WindowTitle
        } | Select-Object -First 1
        if ($game) { break }
        Start-Sleep -Milliseconds 200
    } while (!$shell.HasExited -and [DateTime]::UtcNow -lt $deadline)

    if (!$game) { throw "The bundled Java did not open '$WindowTitle'. See 'launch.log' in '$root'." }
    if (!$game.CloseMainWindow() -or !$shell.WaitForExit(10000) -or $shell.ExitCode -ne 0) {
        throw "The packaged game did not exit cleanly. See 'launch.log' in '$root'."
    }
    Write-Output "Observed and closed '$WindowTitle' using '$bundledJava' with only Windows system tools on PATH."
}
finally {
    if (!$shell.HasExited) {
        $shell.Kill($true)
        $shell.WaitForExit()
    }
    Set-Content -LiteralPath (Join-Path $root 'launch.log') -Value @(
        $stdout.GetAwaiter().GetResult(), $stderr.GetAwaiter().GetResult()
    ) -Encoding utf8
    if ($game) { $game.Dispose() }
    $shell.Dispose()
}
