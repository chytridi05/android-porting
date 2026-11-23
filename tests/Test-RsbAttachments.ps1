param(
    [Parameter(Mandatory)] [string] $Cli,
    [Parameter(Mandatory)] [string] $Apk,
    [Parameter(Mandatory)] [string] $WorkDirectory,
    [string] $Desktop
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$cliPath = (Resolve-Path -LiteralPath $Cli).Path
$apkPath = (Resolve-Path -LiteralPath $Apk).Path
$root = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $root) { throw "Choose a new work directory. '$root' already exists." }
$null = [IO.Directory]::CreateDirectory($root)
. (Join-Path $PSScriptRoot 'verification/RsbFixture.ps1')

function Assert-Cli([string] $Case, [string[]] $Command, [int] $ExpectedExit = 0, [string] $ExpectedText) {
    $lines = @(& $cliPath @Command 2>&1)
    $exitCode = $LASTEXITCODE
    $lines | Set-Content -LiteralPath (Join-Path $root "$Case.log") -Encoding utf8
    if ($exitCode -ne $ExpectedExit -or ($ExpectedText -and !($lines -join "`n").Contains($ExpectedText))) {
        throw "Expected exit code $ExpectedExit and '$ExpectedText' for '$Case'. Got ${exitCode}: $($lines -join "`n")"
    }
}

$bundle = Join-Path $root 'main.1.fixture.attachment.obb'
$renamed = Join-Path $root 'alternate.bin'
$extensionless = Join-Path $root 'bundle'
$required = @('--require-asset', 'levels/route.txt')
New-RsbFixture $bundle
New-RsbFixture $renamed 2
New-RsbFixture $extensionless 1

$project = Join-Path $root 'RSB project'
Assert-Cli 'generate-rsb' (@('generate', $apkPath, $project, '--companion', $bundle) + $required) 0 'RSB v4'
Assert-Cli 'plan-renamed' (@('plan', $apkPath, '--companion', $renamed) + $required) 0 'Mapped resources:'
Assert-Cli 'plan-extensionless' (@('plan', $apkPath, '--companion', $extensionless) + $required) 0 'Mapped resources:'
$raw = Join-Path $root 'raw.rsb'
New-RsbFixture $raw 0
Assert-Cli 'plan-raw' (@('plan', $apkPath, '--companion', $raw) + $required) 0 'Mapped resources:'
Assert-Cli 'rsb-as-zip' @('plan', $apkPath, '--obb', $bundle) 1 'apk.obb-decode'
Assert-Cli 'missing' @('plan', $apkPath, '--require-asset', 'levels/route.txt') 1 'apk.required-asset'

$metadata = Get-Content -LiteralPath (Join-Path $project 'porting-apk-inputs.json') -Raw | ConvertFrom-Json
$preserved = $metadata.companions | Where-Object { $_.input.originalPath -ceq $bundle }
$bundleHash = (Get-FileHash -LiteralPath $bundle).Hash
if ($preserved.decodedFormat -cne 'rsb-v4' -or $preserved.expansionRole -cne 'main' -or $preserved.input.sha256 -ne $bundleHash -or
    (Get-FileHash -LiteralPath (Join-Path $project $preserved.preservedPath)).Hash -ne $bundleHash) {
    throw 'RSB expansion association, decoded format or original byte preservation did not match.'
}

foreach ($name in @('levels/route.txt', 'levels/hint.txt', 'fixture.ptx')) {
    $mapped = $metadata.resources | Where-Object resourcePath -CEQ $name
    $file = Join-Path (Join-Path $project 'assets') $name
    if ($mapped.sourceGroup.name -cne 'FIXTURE' -or $mapped.sourceGroup.index -ne 0 -or
        $mapped.input.originalPath -cne $name.Replace('/', '\') -or $mapped.input.containerPath -cne $bundle -or
        (Get-FileHash -LiteralPath $file).Hash -ne $mapped.input.sha256) {
        throw "RSB provenance for '$name' did not match."
    }
}
$routeFile = Join-Path $project 'assets/levels/route.txt'
if (([IO.File]::ReadAllText($routeFile)) -cne 'RUR') { throw 'Incorrect decoded route.' }

$conflict = Join-Path $root 'conflicting-route'
[IO.File]::WriteAllText($conflict, 'URU')
Assert-Cli 'conflict' @('plan', $apkPath, '--companion', $bundle, '--map-asset', $conflict, 'levels/route.txt') 1 'apk.asset-conflict'

$storedLength = [BitConverter]::ToUInt32([IO.File]::ReadAllBytes($bundle), 0x101C)
foreach ($case in @(
    @{ Name = 'bad-packet-span'; Offset = 0xF4; Value = [uint32]::MaxValue; Expected = 'embedded packet area' }
    @{ Name = 'bad-directory-span'; Offset = 0x104C; Value = [uint32]::MaxValue; Expected = 'resource directory' }
    @{ Name = 'unsupported-compression'; Offset = 0x1010; Value = 4; Expected = 'unsupported compression flags' }
    @{ Name = 'wrong-output-size'; Offset = 0x1020; Value = 80; Expected = 'shorter than its declared length' }
    @{ Name = 'bad-zlib'; Offset = 0x2000; Value = 0; Expected = 'apk.rsb-decode' }
    @{ Name = 'missing-zlib-trailer'; Offset = 0x101C; Value = ($storedLength - 2); Expected = 'zlib stream ended' }
    @{ Name = 'bad-resource-span'; Offset = (0x105C + 18 * 4); Value = [uint32]::MaxValue; Expected = 'extends beyond its data section' }
)) {
    $invalid = Join-Path $root "$($case.Name).rsb"
    $bytes = [IO.File]::ReadAllBytes($bundle)
    [BitConverter]::GetBytes([uint32] $case.Value).CopyTo($bytes, $case.Offset)
    [IO.File]::WriteAllBytes($invalid, $bytes)
    $failedProject = Join-Path $root 'failed-export'
    Assert-Cli $case.Name (@('generate', $apkPath, $failedProject, '--companion', $invalid) + $required) 1 $case.Expected
    if (Test-Path -LiteralPath $failedProject) { throw 'A failed RSB export was published.' }
}

$truncated = Join-Path $root 'truncated.rsb'
$bytes = [IO.File]::ReadAllBytes($bundle)
[IO.File]::WriteAllBytes($truncated, $bytes[0..($bytes.Length - 2)])
Assert-Cli 'truncated' @('plan', $apkPath, '--companion', $truncated) 1 'apk.rsb-decode'

$unsafe = Join-Path $root 'unsafe-path.rsb'
$bytes[0x105C] = [byte] [char] ':'
[IO.File]::WriteAllBytes($unsafe, $bytes)
Assert-Cli 'unsafe-path' @('plan', $apkPath, '--companion', $unsafe) 1 'apk.asset-path'

if ($Desktop) {
    . (Join-Path $PSScriptRoot 'verification/DesktopAutomation.ps1')
    $desktopPath = (Resolve-Path -LiteralPath $Desktop).Path
    $desktopProcess = [Diagnostics.Process]::Start([Diagnostics.ProcessStartInfo]::new($desktopPath))
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        do {
            Start-Sleep -Milliseconds 100
            $desktopProcess.Refresh()
        } while (!$desktopProcess.HasExited -and $desktopProcess.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
        if ($desktopProcess.MainWindowHandle -eq 0) { throw 'The desktop tool did not open its main window.' }
        $desktopWindow = [Windows.Automation.AutomationElement]::FromHandle($desktopProcess.MainWindowHandle)

        $desktopProject = Join-Path $root 'WPF RSB project'
        Set-DesktopValue 'ApkPath' $apkPath
        Set-DesktopValue 'OutputPath' $desktopProject
        Add-DesktopAttachment $bundle
        Invoke-DesktopStep 'InspectButton'
        Assert-DesktopText 'Inventory' 'RSB v4, 1 resource groups'
        Add-DesktopAttachment $bundle -Obb
        Invoke-DesktopStep 'PlanButton' 'Failed' 'apk.obb-decode'
        Add-DesktopAttachment $bundle
        Invoke-DesktopButton 'GenerateButton'
        Invoke-DesktopButton 'CancelButton'
        Wait-DesktopResult 'Cancel RSB export' 'Canceled'
        if (Test-Path -LiteralPath $desktopProject) { throw 'WPF published a canceled RSB export.' }
        Invoke-DesktopStep 'GenerateButton'
        Assert-ExportFileHashes $desktopProject (Get-ExportFileHashes $project)
        Write-Output 'WPF RSB inspection, explicit ZIP selection, cancellation and CLI export parity passed.'
    }
    finally {
        if (!$desktopProcess.HasExited) {
            $null = $desktopProcess.CloseMainWindow()
            if (!$desktopProcess.WaitForExit(5000)) { $desktopProcess.Kill($true) }
        }
        $desktopProcess.Dispose()
    }
}

Write-Output 'RSB ZIP routing, resource bytes, provenance, and failure diagnostics passed.'
