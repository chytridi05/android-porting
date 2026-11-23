Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

function Get-DesktopElement([string] $Id) {
    $condition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    $element = $desktopWindow.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
    if (!$element) { throw "The desktop window has no control with ID '$Id'." }
    return $element
}

function Set-DesktopValue([string] $Id, [string] $Value) {
    $pattern = (Get-DesktopElement $Id).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
    $pattern.SetValue($Value)
}

function Get-DesktopValue([string] $Id) {
    $pattern = (Get-DesktopElement $Id).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
    return $pattern.Current.Value
}

function Invoke-DesktopButton([string] $Id) {
    $pattern = (Get-DesktopElement $Id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

function Select-DesktopTab([string] $Id) {
    $pattern = (Get-DesktopElement $Id).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)
    $pattern.Select()
}

function Assert-DesktopText([string] $Grid, [string] $Text) {
    $condition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::NameProperty, $Text)
    if (!(Get-DesktopElement $Grid).FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)) {
        throw "Expected '$Text' in the desktop $Grid grid."
    }
}

function Wait-DesktopResult([string] $Action, [string] $Expected, [string] $Diagnostic) {
    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    do {
        Start-Sleep -Milliseconds 100
        if ($desktopProcess.HasExited) { throw "The desktop tool exited during '$Action'." }
        $status = (Get-DesktopElement 'Status').Current.Name
        $finished = $status -cin @('Completed', 'Failed', 'Canceled')
    } while (!$finished -and [DateTime]::UtcNow -lt $deadline)
    if ($status -cne $Expected) {
        $summary = Get-DesktopValue 'Summary'
        $diagnostics = (Get-DesktopElement 'Diagnostics').FindAll(
            [Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) |
            ForEach-Object { $_.Current.Name } | Where-Object { $_ } | Select-Object -Unique
        throw "Expected '$Expected' for '$Action'. Desktop status was '$status'. $summary $($diagnostics -join ' | ')"
    }
    if ($Diagnostic) { Assert-DesktopText 'Diagnostics' $Diagnostic }
}

function Invoke-DesktopStep([string] $Action, [string] $Expected = 'Completed', [string] $Diagnostic) {
    Invoke-DesktopButton $Action
    Wait-DesktopResult $Action $Expected $Diagnostic
}

function Add-DesktopAttachment([string] $Path, [switch] $Obb) {
    Set-DesktopValue 'CompanionPath' $Path
    $button = if ($Obb) { 'AddObbButton' } else { 'AddCompanionButton' }
    Invoke-DesktopButton $button
}

function Get-ExportFileHashes([string] $Directory) {
    Get-ChildItem -LiteralPath $Directory -Recurse -File -Force | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($Directory, $_.FullName)
        "$relative $((Get-FileHash -LiteralPath $_.FullName).Hash)"
    } | Sort-Object -CaseSensitive
}

function Assert-ExportFileHashes([string] $Directory, [string[]] $Expected) {
    $actual = @(Get-ExportFileHashes $Directory)
    $differences = Compare-Object -ReferenceObject $Expected -DifferenceObject $actual -CaseSensitive
    if ($differences) {
        throw "CLI and WPF exports differ in '$Directory'.`n$($differences | Out-String)"
    }
}

function Assert-DesktopApkParity([string] $Executable) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $desktopProcess = [Diagnostics.Process]::Start($start)
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        do {
            Start-Sleep -Milliseconds 100
            $desktopProcess.Refresh()
        } while (!$desktopProcess.HasExited -and $desktopProcess.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
        if ($desktopProcess.MainWindowHandle -eq 0) { throw 'The desktop tool did not open its main window.' }
        $desktopWindow = [Windows.Automation.AutomationElement]::FromHandle($desktopProcess.MainWindowHandle)
        $bundledClr = Join-Path (Split-Path -Parent $Executable) 'coreclr.dll'
        if ((Test-Path -LiteralPath $bundledClr) -and !($desktopProcess.Modules | Where-Object FileName -IEQ $bundledClr)) {
            throw 'The self-contained desktop tool did not load its bundled .NET runtime.'
        }

        $tiltExport = Join-Path $root 'WPF Tilt Maze & editable'
        Set-DesktopValue 'ApkPath' $tiltApk
        Set-DesktopValue 'OutputPath' $tiltExport
        Invoke-DesktopStep 'InspectButton'
        Assert-DesktopText 'Inventory' (Get-FileHash -LiteralPath $tiltApk -Algorithm SHA256).Hash.ToLowerInvariant()
        Invoke-DesktopStep 'PlanButton'
        if (!(Get-DesktopValue 'Summary').Contains('Recovered source files: 5')) {
            throw 'The port preview did not show the recovered Tilt Maze source files.'
        }
        Invoke-DesktopStep 'GenerateButton'
        Assert-ExportFileHashes $tiltExport $tiltFileHashes

        Select-DesktopTab 'ApkTab'
        $routeExport = Join-Path $root 'WPF OBB game & editable'
        Set-DesktopValue 'ApkPath' $routeApk
        Set-DesktopValue 'OutputPath' $routeExport
        Invoke-DesktopStep 'GenerateButton' 'Failed' 'apk.obb-missing'
        if (Test-Path -LiteralPath $routeExport) { throw 'WPF published a failed generation.' }

        Add-DesktopAttachment $obb
        Add-DesktopAttachment $opaqueDirectory
        Invoke-DesktopStep 'InspectButton'
        Assert-DesktopText 'Inventory' (Get-FileHash -LiteralPath $extensionless -Algorithm SHA256).Hash.ToLowerInvariant()
        $expander = (Get-DesktopElement 'PortOptionsPanel').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
        $expander.Expand()
        Set-DesktopValue 'RequiredAssets' 'levels/missing.txt'
        Invoke-DesktopStep 'PlanButton' 'Failed' 'apk.required-asset'

        Add-DesktopAttachment $conflict -Obb
        Invoke-DesktopStep 'PlanButton' 'Failed' 'apk.asset-conflict'
        $attachmentList = Get-DesktopElement 'Attachments'
        $selectable = [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::IsSelectionItemPatternAvailableProperty, $true)
        $items = $attachmentList.FindAll([Windows.Automation.TreeScope]::Descendants, $selectable)
        if ($items.Count -ne 3) { throw "Expected three attachments before removing the conflicting OBB." }
        $selection = $items[$items.Count - 1].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)
        $selection.Select()
        Invoke-DesktopButton 'RemoveAttachmentButton'

        Set-DesktopValue 'RequiredAssets' "levels/route.txt`r`n$additionalAsset"
        Invoke-DesktopButton 'GenerateButton'
        $deadline = [DateTime]::UtcNow.AddSeconds(3)
        while (!(Get-DesktopElement 'CancelButton').Current.IsEnabled -and [DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 30
        }
        Invoke-DesktopButton 'CancelButton'
        Wait-DesktopResult 'Cancel generation' 'Canceled'
        if (Test-Path -LiteralPath $routeExport) { throw 'WPF published a canceled generation.' }
        Invoke-DesktopStep 'GenerateButton'
        Assert-ExportFileHashes $routeExport $routeFileHashes
        if ((Get-DesktopValue 'ProjectPath') -cne $routeExport) { throw 'WPF did not select the generated project for building.' }
        if ($Offline) {
            $toggle = (Get-DesktopElement 'Offline').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
            $toggle.Toggle()
        }

        Invoke-DesktopStep 'BuildButton'
        if (!(Get-DesktopValue 'GradleLog').Contains('BUILD SUCCESSFUL')) { throw 'WPF did not show the Gradle build log.' }
        Set-DesktopValue 'ExecutableName' 'OBBRoute'
        $archive = Join-Path $root 'wpf-obb-original.zip'
        Set-DesktopValue 'ArchivePath' $archive
        Invoke-DesktopStep 'PackageButton'
        $archiveHash = (Get-FileHash -LiteralPath $archive).Hash
        Invoke-DesktopStep 'PackageButton' 'Failed' 'package.archive-exists'
        if ((Get-FileHash -LiteralPath $archive).Hash -cne $archiveHash) { throw 'WPF overwrote the existing ZIP.' }
        Assert-PackagedRoute $archive 'RUR' 'WPF original'

        $asset = Join-Path $routeExport 'assets/levels/route.txt'
        Set-Content -LiteralPath $asset -Value 'URU' -Encoding utf8
        Select-DesktopTab 'ApkTab'
        Invoke-DesktopStep 'GenerateButton' 'Failed' 'generation.output-exists'
        if ((Get-Content -LiteralPath $asset -Raw).Trim() -cne 'URU') { throw 'WPF overwrote the edited resource.' }
        Select-DesktopTab 'BuildTab'
        Invoke-DesktopStep 'BuildButton'
        $edited = Join-Path $root 'wpf-obb-edited.zip'
        Set-DesktopValue 'ArchivePath' $edited
        Invoke-DesktopStep 'PackageButton'
        Assert-PackagedRoute $edited 'URU' 'WPF edited'

        $toggle = (Get-DesktopElement 'UseExecutable').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
        $toggle.Toggle()
        $scriptArchive = Join-Path $root 'wpf-obb-launch-script.zip'
        Set-DesktopValue 'ArchivePath' $scriptArchive
        Invoke-DesktopStep 'PackageButton'
        $zip = [IO.Compression.ZipFile]::OpenRead($scriptArchive)
        try {
            if (!$zip.GetEntry('Launch.cmd') -or !$zip.GetEntry('runtime/bin/java.exe')) {
                throw 'WPF did not package the launch script with its private runtime.'
            }
        }
        finally { $zip.Dispose() }
        Write-Output 'CLI and WPF exports matched for both APKs, and the WPF packages ran both OBB routes.'
    }
    finally {
        if (!$desktopProcess.HasExited) {
            $null = $desktopProcess.CloseMainWindow()
            if (!$desktopProcess.WaitForExit(5000)) { $desktopProcess.Kill($true) }
        }
        $desktopProcess.Dispose()
    }
}
