using Microsoft.Win32;
using Porting.Core;
using Porting.Core.Building;
using Porting.Core.Generation;
using Porting.Core.Inspection;
using Porting.Core.Packaging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Porting.Desktop
{
    /// <summary>Collects APK port inputs and presents results from the shared core.</summary>
    internal partial class MainWindow : Window
    {
        private readonly ApkInspector inspector = new();
        private readonly ApkProjectPorter porter = new();
        private readonly IDesktopProjectBuilder builder = new GradleDesktopProjectBuilder();
        private readonly IDesktopProjectPackager packager = new GradleDesktopProjectPackager();
        private readonly ObservableCollection<Attachment> attachments = [];
        private readonly OperationLog log = new();
        private readonly DispatcherTimer logTimer = new(DispatcherPriority.Background);
        private CancellationTokenSource? currentOperation;
        private bool closeRequested;

        /// <summary>Creates the WPF window from its XAML startup resource.</summary>
        public MainWindow()
        {
            InitializeComponent();
            Attachments.ItemsSource = attachments;
            logTimer.Interval = TimeSpan.FromMilliseconds(150);
            logTimer.Tick += (_, _) => FlushLog();
        }

        private void BrowseApk(object sender, RoutedEventArgs args)
        {
            BrowseFile(ApkPath, "Android APK (*.apk)|*.apk");
        }

        private void BrowseJadx(object sender, RoutedEventArgs args)
        {
            BrowseFile(JadxJar, "Java archive (*.jar)|*.jar");
        }

        private void BrowseFile(TextBox target, string filter)
        {
            OpenFileDialog dialog = new() { Filter = filter };
            if (dialog.ShowDialog(this) == true)
            {
                target.Text = dialog.FileName;
            }
        }

        private void BrowseGradle(object sender, RoutedEventArgs args)
        {
            BrowseDirectory("Choose an unpacked Gradle distribution", path => GradleHome.Text = path);
        }

        private void BrowseProject(object sender, RoutedEventArgs args)
        {
            BrowseDirectory("Choose an editable project", path => ProjectPath.Text = path);
        }

        private void BrowseFolder(object sender, RoutedEventArgs args)
        {
            BrowseDirectory("Choose a companion folder", path => AddAttachment(path, isObb: false));
        }

        private void BrowseDirectory(string title, Action<string> select)
        {
            OpenFolderDialog dialog = new() { Title = title };
            if (dialog.ShowDialog(this) == true)
            {
                select(dialog.FolderName);
            }
        }

        private void BrowseFiles(object sender, RoutedEventArgs args)
        {
            BrowseAttachments("All files (*.*)|*.*", isObb: false);
        }

        private void BrowseObb(object sender, RoutedEventArgs args)
        {
            BrowseAttachments("ZIP expansion files (*.obb;*.zip)|*.obb;*.zip|All files (*.*)|*.*", isObb: true);
        }

        private void BrowseAttachments(string filter, bool isObb)
        {
            OpenFileDialog dialog = new() { Filter = filter, Multiselect = true };
            if (dialog.ShowDialog(this) == true)
            {
                foreach (string path in dialog.FileNames)
                {
                    AddAttachment(path, isObb);
                }
            }
        }

        private void BrowseArchive(object sender, RoutedEventArgs args)
        {
            SaveFileDialog dialog = new()
            {
                Filter = "ZIP archive (*.zip)|*.zip",
                DefaultExt = ".zip",
                OverwritePrompt = false
            };
            if (dialog.ShowDialog(this) == true)
            {
                ArchivePath.Text = dialog.FileName;
            }
        }

        private void AddCompanionPath(object sender, RoutedEventArgs args)
        {
            AddEnteredAttachment(isObb: false);
        }

        private void AddObbPath(object sender, RoutedEventArgs args)
        {
            AddEnteredAttachment(isObb: true);
        }

        private void AddEnteredAttachment(bool isObb)
        {
            string path = ReadPath(CompanionPath);
            if (path.Length > 0)
            {
                AddAttachment(path, isObb);
                CompanionPath.Clear();
                _ = CompanionPath.Focus();
            }
        }

        private void AddAttachment(string path, bool isObb)
        {
            Attachment? existing = attachments.FirstOrDefault(item => item.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                attachments.Add(new(path, isObb));
            }
            else if (existing.IsObb != isObb)
            {
                attachments[attachments.IndexOf(existing)] = new(path, isObb);
            }
        }

        private void RemoveAttachments(object sender, RoutedEventArgs args)
        {
            foreach (Attachment item in Attachments.SelectedItems.Cast<Attachment>().ToArray())
            {
                _ = attachments.Remove(item);
            }
        }

        private async void InspectApk(object sender, RoutedEventArgs args)
        {
            string apk = ReadPath(ApkPath);
            string[] paths = attachments.Select(static item => item.Path).ToArray();
            await RunAsync("Inspecting APK", token => inspector.InspectAsync(apk, paths, token), ShowInventory);
        }

        private async void PlanApk(object sender, RoutedEventArgs args)
        {
            string apk = ReadPath(ApkPath);
            ApkPortOptions options = ReadOptions();
            await RunAsync("Planning port", token => porter.PlanAsync(apk, options, token), ShowPort);
        }

        private async void GenerateApk(object sender, RoutedEventArgs args)
        {
            string apk = ReadPath(ApkPath);
            string output = ReadPath(OutputPath);
            ApkPortOptions options = ReadOptions();
            await RunAsync("Generating project", token => porter.GenerateAsync(apk, output, options, token), ShowPort);
        }

        private async void BuildProject(object sender, RoutedEventArgs args)
        {
            string project = ReadPath(ProjectPath);
            bool offline = Offline.IsChecked == true;
            await RunAsync("Building project", token => builder.BuildAsync(project, offline, log, log, token),
                result => Finish(result.Diagnostics, $"Built {result.DesktopModule} in {result.ProjectDirectory}", LogTab), LogTab);
        }

        private async void PackageProject(object sender, RoutedEventArgs args)
        {
            string project = ReadPath(ProjectPath);
            string archive = ReadPath(ArchivePath);
            string? name = UseExecutable.IsChecked == true ? ExecutableName.Text.Trim() : null;
            bool offline = Offline.IsChecked == true;
            await RunAsync("Packaging ZIP", token => name is null
                    ? packager.PackageAsync(project, archive, offline, log, log, token)
                    : packager.PackageExecutableAsync(project, archive, name, offline, log, log, token),
                result => Finish(result.Diagnostics, $"Packaged ZIP: {result.ArchivePath}", LogTab), LogTab);
        }

        private ApkPortOptions ReadOptions()
        {
            string[] assets = RequiredAssets.Text.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return new(attachments.Where(static item => !item.IsObb).Select(static item => item.Path).ToArray(),
                attachments.Where(static item => item.IsObb).Select(static item => item.Path).ToArray(),
                assets, Optional(ReadPath(JadxJar)), Optional(ReadPath(GradleHome)), Optional(EntryType.Text.Trim()));

            static string? Optional(string value) => value.Length == 0 ? null : value;
        }

        private static string ReadPath(TextBox field)
        {
            return field.Text.Trim().Trim('"');
        }

        private void ShowInventory(ApkInventory inventory)
        {
            IEnumerable<InventoryItem> entries = inventory.Entries.Select(static item => new InventoryItem("APK entry", item));
            IEnumerable<InventoryItem> companions = inventory.Companions.Select(static item => new InventoryItem("Companion", item));
            Inventory.ItemsSource = inventory.Package is ApkInputFile package
                ? new[] { new InventoryItem("APK", package) }.Concat(companions).Concat(entries).ToArray()
                : companions.Concat(entries).ToArray();
            Finish(inventory.Diagnostics, $"APK: {inventory.ApkPath}\nArchive entries: {inventory.Entries.Count}\n"
                + $"Companion files: {inventory.Companions.Count}", InventoryTab);
        }

        private void ShowPort(ApkPortResult result)
        {
            string details = result.PackageName is not null
                ? $"Android package: {result.PackageName}\nDesktop game entry: {result.EntryType}\n"
                    + $"libGDX version: {result.LibGdxVersion}\nRecovered source files: {result.SourceFiles}\nMapped resources: {result.Resources}"
                : "";
            if (result.OutputDirectory is string project)
            {
                ProjectPath.Text = project;
                Workflow.SelectedItem = BuildTab;
                details = $"Generated project: {project}\n{details}";
            }

            Finish(result.Diagnostics, details, DiagnosticsTab);
        }

        private async Task RunAsync<T>(string operation, Func<CancellationToken, Task<T>> execute, Action<T> show, TabItem? runningTab = null)
        {
            if (currentOperation is not null)
            {
                return;
            }

            using CancellationTokenSource source = new();
            currentOperation = source;
            Workflow.IsEnabled = false;
            CancelButton.IsEnabled = true;
            Progress.Visibility = Visibility.Visible;
            Progress.IsIndeterminate = true;
            Status.Text = operation;
            Summary.Clear();
            Diagnostics.ItemsSource = null;
            Inventory.ItemsSource = null;
            GradleLog.Clear();
            Results.SelectedItem = runningTab ?? DiagnosticsTab;
            logTimer.Start();
            try
            {
                // Run the synchronous parts of archive reads and process setup away from the UI dispatcher.
                T result = await Task.Run(() => execute(source.Token), source.Token);
                show(result);
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested)
            {
                Status.Text = "Canceled";
                Summary.Text = "Operation canceled.";
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                Finish([new("desktop.operation-failed", DiagnosticSeverity.Error, exception.Message)], "", DiagnosticsTab);
            }
            finally
            {
                logTimer.Stop();
                FlushLog();
                currentOperation = null;
                Workflow.IsEnabled = true;
                CancelButton.IsEnabled = false;
                Progress.IsIndeterminate = false;
                Progress.Visibility = Visibility.Hidden;
                if (closeRequested)
                {
                    Close();
                }
            }
        }

        private void Finish(IReadOnlyList<PortDiagnostic> diagnostics, string details, TabItem successTab)
        {
            bool hasError = diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            Summary.Text = hasError && successTab == LogTab ? "See Diagnostics and the Gradle log for details." : details;
            Diagnostics.ItemsSource = diagnostics;
            Results.SelectedItem = diagnostics.Count == 0 ? successTab : DiagnosticsTab;
            Status.Text = hasError ? "Failed" : "Completed";
        }

        private void FlushLog()
        {
            string chunk = log.Drain();
            if (chunk.Length == 0)
            {
                return;
            }

            GradleLog.AppendText(chunk);
            if (GradleLog.Text.Length > OperationLog.CharacterLimit)
            {
                GradleLog.Text = GradleLog.Text[^OperationLog.CharacterLimit..];
            }

            GradleLog.ScrollToEnd();
        }

        private void CancelOperation(object sender, RoutedEventArgs args)
        {
            RequestCancellation();
        }

        private void RequestCancellation()
        {
            CancelButton.IsEnabled = false;
            Status.Text = "Canceling";
            currentOperation?.Cancel();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (currentOperation is not null)
            {
                e.Cancel = true;
                closeRequested = true;
                RequestCancellation();
            }

            base.OnClosing(e);
        }

        private sealed record Attachment(string Path, bool IsObb)
        {
            /// <summary>The attachment kind shown in the input list.</summary>
            public string Type => IsObb ? "ZIP OBB" : "Companion";
        }

        private sealed record InventoryItem(string Type, ApkInputFile Input);
    }
}