using Porting.Core.Inspection;
using Porting.Core.Planning;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Porting.Core.Generation;

/// <summary>Plans and exports editable desktop Gradle projects recovered from libGDX APKs.</summary>
public sealed class ApkProjectPorter
{
    private const string GradleVersion = "8.14.3";
    private const string WrapperSha256 = "7d3a4ac4de1c32b59bc6a4eb8ecb8e612ccd0cf1ae1e99f66902da64df296172";
    private const string DistributionSha256 = "bd71102213493060956ec229d946beee57158dbd89d0e62b91bca0fa2c5f3531";

    /// <summary>Checks recovery and resource mapping without publishing a project.</summary>
    public Task<ApkPortResult> PlanAsync(string apkPath, ApkPortOptions options, CancellationToken cancellationToken = default)
    {
        return PortAsync(apkPath, null, options, cancellationToken);
    }

    /// <summary>Publishes a complete editable project in a separate new directory.</summary>
    public Task<ApkPortResult> GenerateAsync(string apkPath, string outputDirectory, ApkPortOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        return PortAsync(apkPath, Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory)), options, cancellationToken);
    }

    private static async Task<ApkPortResult> PortAsync(string apkPath, string? output, ApkPortOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ApkInventory inventory = await new ApkInspector().InspectAsync(apkPath, [.. options.CompanionPaths, .. options.ObbPaths], cancellationToken);
        List<PortDiagnostic> diagnostics = [.. inventory.Diagnostics];
        RecoveredApk? recovered = null;
        ApkProjectAssets? assets = null;
        string? workspace = null;
        string? published = null;
        if (inventory.Package is null || HasErrors())
        {
            return Result();
        }

        if (output is not null && Path.Exists(output))
        {
            AddConflict();
        }
        if (output is not null && options.CompanionPaths.Concat(options.ObbPaths).Select(Path.GetFullPath)
            .Any(path => Directory.Exists(path) && DesktopProjectGenerator.IsAtOrBelow(path, output)))
        {
            diagnostics.Add(new("generation.output-overlap", DiagnosticSeverity.Error,
                "Choose an output directory outside the supplied companion directories.", output));
        }
        if (HasErrors())
        {
            return Result();
        }

        string location = inventory.ApkPath;
        try
        {
            if (output is null)
            {
                workspace = Directory.CreateTempSubdirectory("porting-apk-").FullName;
            }
            else
            {
                string parent = Path.GetDirectoryName(output)!;
                DesktopProjectGenerator.VerifyDirectoryAncestors(parent);
                workspace = Path.Combine(parent, $".porting-apk-{Guid.NewGuid():N}");
                _ = Directory.CreateDirectory(workspace);
            }

            string snapshot = Path.Combine(workspace, "input.apk");
            await ApkProjectAssets.CopyCheckedFileAsync(inventory.ApkPath, snapshot, inventory.Package.Sha256, cancellationToken);
            if (await ApkPreflight.CheckAsync(snapshot, inventory, options.ObbPaths, diagnostics, cancellationToken))
            {
                recovered = await ApkSourceRecovery.RecoverAsync(snapshot, inventory.ApkPath, workspace, options, diagnostics, cancellationToken);
            }

            if (recovered is not null && !HasErrors())
            {
                string project = Path.Combine(workspace, "project");
                assets = new(project, diagnostics);
                await assets.ReadAsync(inventory, recovered, options, snapshot, cancellationToken);
                if (output is not null && !HasErrors())
                {
                    location = output;
                    string? gradleHome = options.GradleHome ?? Environment.GetEnvironmentVariable("GRADLE_HOME");
                    if (await WriteWrapperAsync(project, gradleHome, diagnostics, cancellationToken))
                    {
                        await WriteProjectAsync(project, inventory, recovered, assets, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        Directory.Move(project, output);
                        published = output;
                    }
                }
            }
        }
        catch (IOException) when (output is not null && Path.Exists(output))
        {
            AddConflict();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            diagnostics.Add(new("apk.port-failed", DiagnosticSeverity.Error, exception.Message, location));
        }
        finally
        {
            if (workspace is not null && Directory.Exists(workspace))
            {
                try
                {
                    Directory.Delete(workspace, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(new("apk.cleanup-failed", DiagnosticSeverity.Warning, exception.Message, workspace));
                }
            }
        }

        return Result();

        bool HasErrors() => diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        void AddConflict() => diagnostics.Add(new("generation.output-exists", DiagnosticSeverity.Error,
            "Choose a new output directory or build the existing project.", output));

        ApkPortResult Result() => new(recovered?.PackageName, recovered?.EntryType, recovered?.LibGdxVersion,
            recovered?.SourceFiles.Count ?? 0, assets?.ResourceCount ?? 0, published, diagnostics.AsReadOnly());
    }

    private static async Task<bool> WriteWrapperAsync(string project, string? gradleHome, List<PortDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        string? distributionJar = string.IsNullOrWhiteSpace(gradleHome) ? null
            : Path.Combine(Path.GetFullPath(gradleHome), "lib", "plugins", $"gradle-wrapper-main-{GradleVersion}.jar");
        if (distributionJar is null || !File.Exists(distributionJar))
        {
            diagnostics.Add(new("apk.gradle-missing", DiagnosticSeverity.Error,
                $"Set GRADLE_HOME or pass --gradle-home pointing to an unpacked Gradle {GradleVersion} distribution.", distributionJar));
            return false;
        }

        string wrapper = Path.Combine(project, "gradle", "wrapper");
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(distributionJar);
            ZipArchiveEntry? entry = archive.GetEntry("gradle-wrapper.jar");
            if (entry is null)
            {
                throw new InvalidDataException("The distribution does not contain its Gradle Wrapper JAR.");
            }

            _ = Directory.CreateDirectory(wrapper);
            string jar = Path.Combine(wrapper, "gradle-wrapper.jar");
            await using (Stream source = entry.Open())
            await using (FileStream destination = File.Open(jar, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            await using FileStream copied = File.OpenRead(jar);
            if (Convert.ToHexStringLower(await SHA256.HashDataAsync(copied, cancellationToken)) != WrapperSha256)
            {
                throw new InvalidDataException("The wrapper JAR does not match the published Gradle checksum.");
            }

            await File.WriteAllTextAsync(Path.Combine(wrapper, "gradle-wrapper.properties"), $$"""
                distributionBase=GRADLE_USER_HOME
                distributionPath=wrapper/dists
                distributionUrl=https\://services.gradle.org/distributions/gradle-{{GradleVersion}}-bin.zip
                distributionSha256Sum={{DistributionSha256}}
                zipStoreBase=GRADLE_USER_HOME
                zipStorePath=wrapper/dists
                """ + "\n", cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            diagnostics.Add(new("apk.gradle-wrapper", DiagnosticSeverity.Error, exception.Message, distributionJar));
            return false;
        }
    }

    private static async Task WriteProjectAsync(string project, ApkInventory inventory, RecoveredApk recovered, ApkProjectAssets assets,
        CancellationToken cancellationToken)
    {
        foreach (string file in recovered.SourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string target = Path.Combine(project, "core", "src", "main", "java", Path.GetRelativePath(recovered.SourceRoot, file));
            _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        await File.WriteAllTextAsync(Path.Combine(project, "settings.gradle"), "rootProject.name = 'apk-desktop'\ninclude 'core'\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(project, "build.gradle"), "allprojects { repositories { mavenCentral() } }\n", cancellationToken);
        string version = recovered.LibGdxVersion;
        string box2D = recovered.UsesBox2D ? $"    api 'com.badlogicgames.gdx:gdx-box2d:{version}'\n" : "";
        await File.WriteAllTextAsync(Path.Combine(project, "core", "build.gradle"),
            $"plugins {{ id 'java-library' }}\n\ndependencies {{\n    api 'com.badlogicgames.gdx:gdx:{version}'\n{box2D}}}\n", cancellationToken);

        IReadOnlyList<string> assetDirectories = assets.ResourceCount == 0 ? [] : ["assets"];
        Lwjgl3Layout layout = new("settings.gradle", recovered.EntryType);
        ProjectInspection inspection = new(project, layout.SettingsFile, GradleVersion, null, ":core", null, recovered.EntryType, assetDirectories, [])
        {
            DesktopLibraries = new(version, recovered.UsesBox2D, recovered.UsesBox2D ? version : null)
        };
        PortPlan plan = new(inspection, Lwjgl3Layout.ModulePath, recovered.EntryType, assetDirectories, 1280, 720, [], [], [], []);
        await DesktopProjectGenerator.WriteDesktopModuleAsync(plan, layout, project, cancellationToken);
        await DesktopProjectGenerator.WriteProjectMetadataAsync(project, plan.DesktopModulePath, cancellationToken);

        await using FileStream provenance = File.Open(Path.Combine(project, "porting-apk-inputs.json"), FileMode.CreateNew, FileAccess.Write);
        await JsonSerializer.SerializeAsync(provenance, new
        {
            formatVersion = 1,
            inventory.Package,
            recovered.PackageName,
            recovered.EntryType,
            Companions = assets.Companions,
            Resources = assets.Resources,
            RuntimeUse = "unverified"
        }, OutputProjectMetadata.JsonOptions, cancellationToken);
    }
}