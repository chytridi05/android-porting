param(
    [Parameter(Mandatory)] [string] $Cli,
    [Parameter(Mandatory)] [string] $Apk,
    [Parameter(Mandatory)] [string] $WorkDirectory
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$cliPath = (Resolve-Path -LiteralPath $Cli).Path
$apkPath = (Resolve-Path -LiteralPath $Apk).Path
$root = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $root) {
    throw "Choose a new work directory. '$root' already exists."
}

function Assert-Cli([string[]] $Command, [int] $ExpectedExitCode, [string[]] $ExpectedText) {
    $output = (& $cliPath @Command 2>&1) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne $ExpectedExitCode) {
        throw "Expected exit code $ExpectedExitCode for '$Command'. Got ${LASTEXITCODE}: $output"
    }
    foreach ($text in $ExpectedText) {
        if (!$output.Contains($text, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Expected '$text' in the output for '$Command'. Got: $output"
        }
    }
}

$directory = Join-Path $root 'companion tree'
$nested = [IO.Directory]::CreateDirectory((Join-Path $directory 'nested')).FullName
$loose = Join-Path $root 'unknown.format'
$extensionless = Join-Path $nested 'blob'
[IO.File]::WriteAllBytes($loose, [byte[]] @(0x28, 0x00, 0xFE))
[IO.File]::WriteAllBytes($extensionless, [byte[]] @(0x00, 0xCA, 0xFF, 0xFE))
$apkHash = (Get-FileHash -LiteralPath $apkPath).Hash
$looseHash = (Get-FileHash -LiteralPath $loose).Hash
$extensionlessHash = (Get-FileHash -LiteralPath $extensionless).Hash
$treeEntry = "$directory!/$([IO.Path]::GetRelativePath($directory, $extensionless))"

Assert-Cli @('inspect', $apkPath, '--companion', $loose, '--companion', $directory) 0 @(
    "APK SHA-256: $apkHash", 'DEX entries:', 'classes.dex', 'Native libraries:', 'lib/x86_64/libgdx.so',
    "$looseHash  $loose", "$extensionlessHash  $treeEntry"
)

$missing = Join-Path $root 'missing'
Assert-Cli @('inspect', $apkPath, '--companion', $missing, '--companion', $loose) 1 @(
    'Error apk.companion-read-failed:', $missing, "$looseHash  $loose"
)
Assert-Cli @('inspect', $apkPath, '--companion') 2 @('--companion', 'requires a value')

$invalid = Join-Path $root 'invalid.apk'
[IO.File]::WriteAllBytes($invalid, [byte[]] @(0xFF, 0x00))
Assert-Cli @('inspect', $invalid) 1 @('Error apk.read-failed:', $invalid)

$duplicate = Join-Path $root 'duplicate.apk'
$archive = [IO.Compression.ZipFile]::Open($duplicate, [IO.Compression.ZipArchiveMode]::Create)
try {
    $null = $archive.CreateEntry('classes.dex')
    $null = $archive.CreateEntry('classes.dex')
    $null = $archive.CreateEntry('assets/CON')
}
finally {
    $archive.Dispose()
}
Assert-Cli @('inspect', $duplicate) 1 @(
    'Error apk.entry-conflict:', "$duplicate!/classes.dex", 'Error apk.manifest-missing:', 'Error apk.archive-path:', "$duplicate!/assets/CON"
)

$native = Join-Path $root 'native.apk'
$archive = [IO.Compression.ZipFile]::Open($native, [IO.Compression.ZipArchiveMode]::Create)
try {
    $null = $archive.CreateEntry('AndroidManifest.xml')
    $null = $archive.CreateEntry('classes.dex')
    $null = $archive.CreateEntry('lib/armeabi-v7a/libgame.so')
}
finally {
    $archive.Dispose()
}
$unavailableJadx = Join-Path $root 'unavailable-jadx.jar'
$blocked = Join-Path $root 'blocked-export'
Assert-Cli @('generate', $native, $blocked, '--obb', $loose, '--jadx', $unavailableJadx) 1 @(
    'Error apk.engine-unsupported:', 'libgame.so', 'Error apk.obb-decode:', $loose
)
if (Test-Path -LiteralPath $blocked) { throw 'An unsupported APK produced a project.' }

$rsb = Join-Path $root 'main.1.fixture.native.obb'
$renamedRsb = Join-Path $root 'renamed.bin'
$extensionlessRsb = Join-Path $nested 'bundle'
$truncatedRsb = Join-Path $root 'truncated.obb'
$invalidRsb = Join-Path $root 'invalid-table.obb'
$unknownVersionRsb = Join-Path $root 'unknown-version.obb'
$header = [byte[]]::new(0x70 + 0xCC)
[Text.Encoding]::ASCII.GetBytes('1bsr').CopyTo($header, 0)
[BitConverter]::GetBytes([uint32] 4).CopyTo($header, 4)
[BitConverter]::GetBytes([uint32] $header.Length).CopyTo($header, 0x0C)
[BitConverter]::GetBytes([uint32] 1).CopyTo($header, 0x28)
[BitConverter]::GetBytes([uint32] 0x70).CopyTo($header, 0x2C)
[BitConverter]::GetBytes([uint32] 0xCC).CopyTo($header, 0x30)
foreach ($path in @($rsb, $renamedRsb, $extensionlessRsb)) {
    [IO.File]::WriteAllBytes($path, $header)
}
[IO.File]::WriteAllBytes($truncatedRsb, $header[0..7])
[BitConverter]::GetBytes([uint32]::MaxValue).CopyTo($header, 0x28)
[IO.File]::WriteAllBytes($invalidRsb, $header)
$header[4] = 5
[IO.File]::WriteAllBytes($unknownVersionRsb, $header[0..7])
$rsbHash = (Get-FileHash -LiteralPath $rsb).Hash
Assert-Cli @('inspect', $native, '--companion', $rsb, '--companion', $renamedRsb, '--companion', $directory) 0 @(
    "RSB v4, 1 resource groups  $rsb", "RSB v4, 1 resource groups  $renamedRsb",
    "RSB v4, 1 resource groups  $directory!/$([IO.Path]::GetRelativePath($directory, $extensionlessRsb))"
)
Assert-Cli @('inspect', $native, '--companion', $truncatedRsb, '--companion', $invalidRsb, '--companion', $unknownVersionRsb) 0 @(
    'Warning apk.rsb-header:', 'RSB v4 header is truncated', 'group table is outside', "RSB v5  $unknownVersionRsb"
)
Assert-Cli @('plan', $native, '--companion', $rsb, '--jadx', $unavailableJadx) 1 @(
    "RSB v4, 1 resource groups  $rsb", 'Error apk.engine-unsupported:'
)
Assert-Cli @('plan', $native, '--obb', $rsb, '--jadx', $unavailableJadx) 1 @(
    "RSB v4, 1 resource groups  $rsb", 'Error apk.obb-decode:', 'Error apk.engine-unsupported:'
)

if ((Get-FileHash -LiteralPath $apkPath).Hash -ne $apkHash -or
    (Get-FileHash -LiteralPath $loose).Hash -ne $looseHash -or
    (Get-FileHash -LiteralPath $extensionless).Hash -ne $extensionlessHash -or
    (Get-FileHash -LiteralPath $rsb).Hash -ne $rsbHash) {
    throw 'Inspection changed an input file.'
}

Write-Output 'APK inventory, RSB headers, companion hashes, and failure diagnostics passed.'
