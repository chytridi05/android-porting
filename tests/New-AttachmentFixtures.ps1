param(
    [Parameter(Mandatory)] [string] $Bundletool,
    [Parameter(Mandatory)] [string] $WorkDirectory,
    [switch] $Offline
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$jar = (Resolve-Path -LiteralPath $Bundletool).Path
$root = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path -LiteralPath $root) { throw "Choose a new fixture work directory. '$root' already exists." }
if (!$env:JAVA_HOME -or !$env:GRADLE_HOME) { throw 'Set JAVA_HOME and GRADLE_HOME before building the fixtures.' }

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fixtures/attachments-libgdx') -Destination $root -Recurse
$java = Join-Path $env:JAVA_HOME 'bin/java.exe'
$gradle = Join-Path $env:GRADLE_HOME 'bin/gradle.bat'
$gradleArguments = @('-p', $root, '--no-daemon', '--console=plain')
if ($Offline) { $gradleArguments += '--offline' }

& $gradle @gradleArguments ':app:bundleDebug' | Out-Host
$device = Join-Path $root 'device.json'
@{ supportedAbis = @('x86_64'); supportedLocales = @('en'); screenDensity = 160; sdkVersion = 36 } |
    ConvertTo-Json | Set-Content -LiteralPath $device -Encoding utf8
$set = Join-Path $root 'fixture.apks'
$bundle = Join-Path $root 'app/build/outputs/bundle/debug/app-debug.aab'
& $java -jar $jar build-apks "--bundle=$bundle" "--output=$set" "--device-spec=$device" | Out-Host
$splits = Join-Path $root 'splits'
& $java -jar $jar extract-apks "--apks=$set" "--output-dir=$splits" "--device-spec=$device" | Out-Host
& $gradle @gradleArguments '-Pstandalone' ':app:assembleDebug' ':app:assembleMinified' | Out-Host

@{
    ApkSet = $set
    SplitDirectory = $splits
    StandaloneApk = Join-Path $root 'app/build/outputs/apk/debug/app-debug.apk'
    ObfuscatedApk = Join-Path $root 'app/build/outputs/apk/minified/app-minified.apk'
    Mapping = Join-Path $root 'app/build/outputs/mapping/minified/mapping.txt'
}
