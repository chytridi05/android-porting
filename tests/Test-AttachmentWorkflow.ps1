param(
    [Parameter(Mandatory)] [string] $Cli,
    [Parameter(Mandatory)] [string] $ApkSet,
    [Parameter(Mandatory)] [string] $SplitDirectory,
    [Parameter(Mandatory)] [string] $StandaloneApk,
    [Parameter(Mandatory)] [string] $ObfuscatedApk,
    [Parameter(Mandatory)] [string] $Mapping,
    [Parameter(Mandatory)] [string] $WorkDirectory,
    [string] $Desktop,
    [string] $SoftwareOpenGlDirectory,
    [switch] $Offline
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$cliPath = (Resolve-Path -LiteralPath $Cli).Path
$apkSetPath = (Resolve-Path -LiteralPath $ApkSet).Path
$splitRoot = (Resolve-Path -LiteralPath $SplitDirectory).Path
$baseApk = (Resolve-Path -LiteralPath (Join-Path $splitRoot 'base-master.apk')).Path
$packApk = (Resolve-Path -LiteralPath (Join-Path $splitRoot 'routepack-master.apk')).Path
$standalone = (Resolve-Path -LiteralPath $StandaloneApk).Path
$obfuscated = (Resolve-Path -LiteralPath $ObfuscatedApk).Path
$mappingPath = (Resolve-Path -LiteralPath $Mapping).Path
$root = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $root) { throw "Choose a new work directory. '$root' already exists." }
$null = [IO.Directory]::CreateDirectory($root)
$splitFiles = @(Get-ChildItem -LiteralPath $splitRoot -Filter '*.apk' -File | Where-Object FullName -ne $baseApk)
if (!($splitFiles.Name -contains 'feature-master.apk')) { throw 'The fixture must contain an independent feature APK.' }
$splitArguments = @(foreach ($split in $splitFiles) { '--split'; $split.FullName })
$featureCheck = @('--require-asset', 'levels/feature.txt')
$routeCheck = @('--require-asset', 'levels/route.txt')
$gradleOptions = @()
if ($Offline) { $gradleOptions = @('--offline') }

function Invoke-CheckedCli([string] $Name, [string[]] $Command, [int] $ExpectedExit = 0, [string] $ExpectedText = '') {
    $output = @(& $cliPath @Command 2>&1)
    $exitCode = $LASTEXITCODE
    $log = Join-Path $root "$Name.log"
    $output | Set-Content -LiteralPath $log -Encoding utf8
    if ($exitCode -ne $ExpectedExit -or ($ExpectedText -and !($output -join [Environment]::NewLine).Contains($ExpectedText))) {
        throw "Expected exit $ExpectedExit and '$ExpectedText'. Got $exitCode. See '$log'."
    }
}

function Wait-RouteTitle([Diagnostics.Process] $Process, [string] $Title) {
    $until = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $Process.Refresh()
        if ($Process.MainWindowTitle -ceq $Title) { return }
        Start-Sleep -Milliseconds 100
    } while (!$Process.HasExited -and [DateTime]::UtcNow -lt $until)
    throw "Expected '$Title'. Last title was '$($Process.MainWindowTitle)'."
}

function Assert-PackagedRoute([string] $Name, [string] $Project, [string] $Route) {
    $archive = Join-Path $root "$Name.zip"
    Invoke-CheckedCli "$Name-package" (@('package', $Project, $archive, '--exe', 'AttachmentRoute') + $gradleOptions)
    $image = Join-Path $root "$Name image"
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $image)
    if (!(Test-Path -LiteralPath (Join-Path $image 'runtime/lib/modules') -PathType Leaf)) {
        throw "The package '$archive' lacks a private Java runtime."
    }
    if ($SoftwareOpenGlDirectory) {
        Get-ChildItem -LiteralPath $SoftwareOpenGlDirectory -Filter '*.dll' -File | Copy-Item -Destination $image
    }

    $profile = [IO.Directory]::CreateDirectory((Join-Path $root "$Name profile")).FullName
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $image 'AttachmentRoute.exe'))
    $start.WorkingDirectory = $profile
    $start.UseShellExecute = $false
    $start.Environment.Clear()
    $start.Environment['SystemRoot'] = $env:SystemRoot
    $start.Environment['WINDIR'] = $env:SystemRoot
    $start.Environment['PATH'] = Join-Path $env:SystemRoot 'System32'
    $start.Environment['USERPROFILE'] = $profile
    foreach ($entry in @{ TEMP = 'Temp'; TMP = 'Temp'; LOCALAPPDATA = 'AppData/Local'; APPDATA = 'AppData/Roaming' }.GetEnumerator()) {
        $start.Environment[$entry.Key] = [IO.Directory]::CreateDirectory((Join-Path $profile $entry.Value)).FullName
    }
    if ($SoftwareOpenGlDirectory) { $start.Environment['GALLIUM_DRIVER'] = 'llvmpipe' }
    $process = [Diagnostics.Process]::Start($start)
    try {
        Wait-RouteTitle $process "Attachment Route Ready $Route"
        for ($index = 0; $index -lt $Route.Length; $index++) {
            $key = if ($Route[$index] -ceq 'R') { 0x27 } else { 0x26 }
            if (![Porting.Verification.WindowInput]::Press($process.MainWindowHandle, $key)) { throw 'Keyboard input failed.' }
            $expected = if ($index + 1 -lt $Route.Length) { "Attachment Route Step $($index + 1)" } else { "Attachment Route Complete $Route" }
            Wait-RouteTitle $process $expected
        }
        Write-Output "$Name completed route $Route from the Windows x64 package."
    }
    finally {
        if (!$process.HasExited -and (!$process.CloseMainWindow() -or !$process.WaitForExit(5000))) { $process.Kill() }
        $process.Dispose()
    }
}

if (!('Porting.Verification.WindowInput' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'verification/WindowInput.cs')
}
$packAsset = Join-Path $root 'route'
$archive = [IO.Compression.ZipFile]::OpenRead($packApk)
try {
    $entry = $archive.GetEntry('assets/levels/route.txt')
    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $packAsset)
}
finally { $archive.Dispose() }
$assetRoot = Join-Path $root 'unpacked assets'
$levels = [IO.Directory]::CreateDirectory((Join-Path $assetRoot 'levels')).FullName
Copy-Item -LiteralPath $packAsset -Destination (Join-Path $levels 'route.txt')

$badMap = Join-Path $root 'wrong-mapping.txt'
$unconfirmedMap = Join-Path $root 'mapping-without-id.txt'
$mappingText = Get-Content -LiteralPath $mappingPath -Raw
if ($mappingText -notmatch '(?m)^# pg_map_id:') { throw 'The R8 fixture mapping must contain a mapping ID.' }
$mappingText -replace '(?m)^# pg_map_id:[^\r\n]*', '# pg_map_id: deliberately-different' | Set-Content -LiteralPath $badMap
$mappingText -replace '(?m)^# pg_map_id:[^\r\n]*', '' | Set-Content -LiteralPath $unconfirmedMap

Invoke-CheckedCli 'inspect-set' @('inspect', $apkSetPath) 0 'feature-master.apk'
Invoke-CheckedCli 'bad-variant' @('inspect', $apkSetPath, '--apk-variant', '999999') 1 'apk.set-read'
Invoke-CheckedCli 'duplicate-split' (@('inspect', $baseApk, '--split', $packApk) + $splitArguments) 1 'apk.split-conflict'
Invoke-CheckedCli 'missing-pack' @('plan', $standalone, '--require-asset', 'levels/route.txt') 1 'apk.required-asset'
Invoke-CheckedCli 'unpacked-pack' (@('plan', $standalone, '--asset-pack', $assetRoot) + $routeCheck)
Invoke-CheckedCli 'wrong-mapping' @('plan', $obfuscated, '--r8-mapping', $badMap) 1 'apk.mapping-mismatch'
Invoke-CheckedCli 'unconfirmed-mapping' (@('plan', $obfuscated, '--r8-mapping', $unconfirmedMap, '--asset-pack', $packApk) + $routeCheck) 0 'apk.mapping-unconfirmed'
Invoke-CheckedCli 'no-mapping' (@('plan', $obfuscated, '--asset-pack', $packApk) + $routeCheck) 0 'Desktop game entry:'

$cases = @(
    @{ Name = 'splits'; Apk = $baseApk; Options = $splitArguments + $featureCheck; Route = 'RUU'; Fields = @{ SplitApks = ($splitFiles.FullName -join "`r`n"); RequiredAssets = "levels/route.txt`r`nlevels/feature.txt" } }
    @{ Name = 'apk-set'; Apk = $apkSetPath; Options = $featureCheck; Route = 'RUU'; Fields = @{ RequiredAssets = "levels/route.txt`r`nlevels/feature.txt" } }
    @{ Name = 'asset-pack'; Apk = $standalone; Options = @('--asset-pack', $packApk); Route = 'RU'; Fields = @{ AssetPacks = $packApk } }
    @{ Name = 'loose'; Apk = $standalone; Options = @('--map-asset', $packAsset, 'levels/route.txt'); Route = 'RU'; Fields = @{ MappedAssets = "$packAsset | levels/route.txt" } }
    @{ Name = 'loose-tree'; Apk = $standalone; Options = @('--map-asset', $assetRoot, '.'); Route = 'RU'; Fields = @{ MappedAssets = "$assetRoot | ." } }
    @{ Name = 'mapped'; Apk = $obfuscated; Options = @('--r8-mapping', $mappingPath, '--asset-pack', $packApk); Route = 'RU'; Fields = @{ R8MappingPath = $mappingPath; AssetPacks = $packApk } }
)

foreach ($case in $cases) {
    $case.Project = Join-Path $root "$($case.Name) project"
    Invoke-CheckedCli "$($case.Name)-generate" (@('generate', $case.Apk, $case.Project) + $case.Options + $routeCheck)
}

foreach ($name in @('splits', 'apk-set')) {
    $project = ($cases | Where-Object Name -eq $name).Project
    if (!(Test-Path -LiteralPath (Join-Path $project 'core/src/main/java/fixture/extension/FeatureRoute.java'))) {
        throw "The $name export lacks the feature class."
    }
    $provenance = Get-Content -LiteralPath (Join-Path $project 'porting-apk-inputs.json') -Raw | ConvertFrom-Json
    if ($provenance.splits.Count -ne $splitFiles.Count) { throw "The $name export lacks split provenance." }
    foreach ($split in $provenance.splits) {
        $snapshot = Join-Path $project $split.preservedPath
        if ((Get-FileHash -LiteralPath $snapshot).Hash -ne $split.file.sha256) {
            throw "The $name export changed the bytes of '$($split.file.originalPath)'."
        }
    }
}
$mapped = ($cases | Where-Object Name -eq 'mapped').Project
$metadata = Get-Content -LiteralPath (Join-Path $mapped 'porting-apk-inputs.json') -Raw | ConvertFrom-Json
if ($metadata.entryType -ne 'fixture.attachment.RouteGame' -or $metadata.r8Mapping.match -ne 'matched' -or
    $metadata.r8Mapping.file.sha256 -ne (Get-FileHash -LiteralPath $mappingPath).Hash) {
    throw 'The matched R8 mapping did not restore the expected entry and provenance.'
}

if ($Desktop) {
    . (Join-Path $PSScriptRoot 'verification/DesktopAutomation.ps1')
    $start = [Diagnostics.ProcessStartInfo]::new((Resolve-Path -LiteralPath $Desktop).Path)
    $start.UseShellExecute = $false
    $desktopProcess = [Diagnostics.Process]::Start($start)
    try {
        Wait-RouteTitle $desktopProcess 'Android Porting'
        $desktopWindow = [Windows.Automation.AutomationElement]::FromHandle($desktopProcess.MainWindowHandle)
        foreach ($case in $cases) {
            Select-DesktopTab 'ApkTab'
            $panel = (Get-DesktopElement 'PortOptionsPanel').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
            $panel.Expand()
            Set-DesktopValue 'ApkPath' $case.Apk
            foreach ($field in 'SplitApks', 'AssetPacks', 'MappedAssets', 'R8MappingPath', 'RequiredAssets') { Set-DesktopValue $field '' }
            Set-DesktopValue 'RequiredAssets' 'levels/route.txt'
            foreach ($field in $case.Fields.GetEnumerator()) { Set-DesktopValue $field.Key $field.Value }
            $export = Join-Path $root "WPF $($case.Name)"
            Set-DesktopValue 'OutputPath' $export
            Invoke-DesktopStep 'GenerateButton'
            Assert-ExportFileHashes $export @(Get-ExportFileHashes $case.Project)
        }
        Select-DesktopTab 'ApkTab'
        Set-DesktopValue 'R8MappingPath' $badMap
        Invoke-DesktopStep 'PlanButton' 'Failed' 'apk.mapping-mismatch'
        Write-Output "CLI and WPF export hashes matched for all $($cases.Count) attachment cases."
    }
    finally {
        if (!$desktopProcess.HasExited -and (!$desktopProcess.CloseMainWindow() -or !$desktopProcess.WaitForExit(5000))) { $desktopProcess.Kill() }
        $desktopProcess.Dispose()
    }
}

foreach ($case in $cases) { Assert-PackagedRoute $case.Name $case.Project $case.Route }
$loose = ($cases | Where-Object Name -eq 'loose').Project
Set-Content -LiteralPath (Join-Path $loose 'assets/levels/route.txt') -Value 'UR'
Assert-PackagedRoute 'loose-edited' $loose 'UR'
