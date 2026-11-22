param(
    [Parameter(Mandatory)] [string] $Cli,
    [Parameter(Mandatory)] [string] $TiltMaze
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$cliPath = (Resolve-Path -LiteralPath $Cli).Path
$javaProject = (Resolve-Path -LiteralPath $TiltMaze).Path
$kotlinProject = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot 'fixtures/kotlin-libgdx')).Path

function Assert-Cli([string[]] $Command, [int] $ExitCode, [string[]] $ExpectedPatterns) {
    $lines = @(& $cliPath @Command 2>&1)
    $actualExitCode = $LASTEXITCODE
    $output = $lines -join "`n"
    if ($actualExitCode -ne $ExitCode) {
        throw "Expected exit $ExitCode for $($Command -join ' '), received $actualExitCode.`n$output"
    }

    foreach ($pattern in $ExpectedPatterns) {
        if ($output -cnotmatch $pattern) {
            throw "Expected '$pattern' for $($Command -join ' ').`n$output"
        }
    }
}

$settingsPaths = @((Join-Path $javaProject 'settings.gradle'), (Join-Path $kotlinProject 'settings.gradle.kts'))
$originalSettings = @(Get-FileHash -LiteralPath $settingsPaths)

Assert-Cli @('--help') 0 @('inspect <source-directory>', 'plan <source-directory>')
Assert-Cli @('inspect', $javaProject) 0 @(
    'Gradle Wrapper version: 8\.14\.3', 'Android module: :android', 'Shared module: :core',
    'Existing desktop module: \(none\)', 'Detected game entry: com\.micro1\.maze\.MazeGame', 'Asset directories: \(none\)'
)
Assert-Cli @('plan', $javaProject) 0 @(
    'Window: 1280 x 720', 'lwjgl3/build\.gradle\s', 'lwjgl3/src/main/java/com/micro1/maze/DesktopLauncher\.java',
    'Files to modify:\s+settings\.gradle', 'Gradle tasks:\s+:lwjgl3:build'
)
Assert-Cli @('inspect', $kotlinProject) 0 @(
    'Gradle settings: settings\.gradle\.kts', 'Android module: :launcher', 'Shared module: :shared',
    'Detected game entry: fixture\.game\.DemoGame', 'Asset directories:\s+assets/default\s+assets/shared'
)
Assert-Cli @('plan', $kotlinProject, '--entry', 'fixture.game.AlternateGame', '--asset', 'assets/alternate',
    '--asset', 'assets/default', '--width', '800', '--height', '600') 0 @(
    'Desktop game entry: fixture\.game\.AlternateGame', 'Window: 800 x 600',
    'Asset directories:\s+assets/alternate\s+assets/default', 'lwjgl3/build\.gradle\.kts',
    'Files to modify:\s+settings\.gradle\.kts'
)
Assert-Cli @('plan', $kotlinProject, '--no-assets') 0 @('Asset directories: \(none\)')
Assert-Cli @('plan', $kotlinProject, '--width', '0') 1 @('Error plan\.window-size', 'Files to create: \(none\)')
Assert-Cli @('plan', $kotlinProject, '--asset', '../outside') 1 @('Error plan\.asset-path', 'inside the source project')
Assert-Cli @('inspect', (Join-Path $kotlinProject 'assets')) 1 @('Error inspection\.settings-missing', 'Choose the Gradle project root')
Assert-Cli @('plan', $kotlinProject, '--height', 'tall') 2 @('requires an integer pixel value')
Assert-Cli @('plan', $kotlinProject, '--no-assets', '--asset', 'assets/default') 2 @('Use either --asset or --no-assets')

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$unsupported = Join-Path $temporaryRoot "porting-unsupported-$([Guid]::NewGuid().ToString('N'))"
$unsupportedApp = Join-Path $unsupported 'app'
$null = [IO.Directory]::CreateDirectory($unsupportedApp)
try {
    Set-Content -LiteralPath (Join-Path $unsupported 'settings.gradle') -Value "include ':app'" -Encoding utf8
    $appBuild = Join-Path $unsupportedApp 'build.gradle'
    Set-Content -LiteralPath $appBuild -Value "plugins { id 'com.android.application' }" -Encoding utf8
    Assert-Cli @('inspect', $unsupported) 1 @(
        'Error inspection\.android-module', 'No Android libGDX module was found', 'Check the Gradle includes and libGDX dependencies'
    )

    Add-Content -LiteralPath $appBuild -Value "dependencies { implementation 'com.badlogicgames.gdx:gdx-backend-android:1.14.1' }"
    Assert-Cli @('plan', $unsupported) 1 @(
        'Error inspection\.shared-module', 'No shared libGDX module was found', 'Files to create: \(none\)'
    )
    $blockedOutput = Join-Path $unsupported 'blocked-output'
    Assert-Cli @('generate', $unsupported, $blockedOutput) 1 @('Error inspection\.shared-module')
    if (Test-Path -LiteralPath $blockedOutput) {
        throw 'Generation wrote a project for an unsupported input.'
    }
}
finally {
    $resolved = (Resolve-Path -LiteralPath $unsupported).Path
    if ([IO.Path]::GetDirectoryName($resolved) -eq [IO.Path]::TrimEndingDirectorySeparator($temporaryRoot)) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

$currentSettings = @(Get-FileHash -LiteralPath $settingsPaths)
if (@(Compare-Object $originalSettings $currentSettings -Property Path, Hash).Count -gt 0) {
    throw 'A read-only command modified a settings file.'
}

Write-Output 'Java and Kotlin inspection, planning, and unsupported-input checks passed.'
