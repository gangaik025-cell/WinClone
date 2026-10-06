using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinClone;

public partial class MainWindow : Window
{
    private readonly List<DiskInfo> _detectedDisks = new();

    private VssSnapshotInfo? _currentSnapshot;

    private string? _sourceFingerprint;

    private string? _destinationFingerprint;

    private bool _clonePlanLocked;

    private CancellationTokenSource? _cloneCancellation;

    public MainWindow()
    {
        InitializeComponent();
    }

    // ============================================================
    // DETECT DRIVES
    // ============================================================

    private async void DetectDrivesButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            DetectDrivesButton.IsEnabled = false;

            StatusText.Text = "Scanning disks...";
            StatusText.Foreground = Brushes.Orange;

            DiskListPanel.Children.Clear();

            List<DiskInfo> disks =
                await GetDisksAsync();

            _detectedDisks.Clear();
            _detectedDisks.AddRange(disks);

            SourceDriveComboBox.ItemsSource = null;
            DestinationDriveComboBox.ItemsSource = null;

            SourceDriveComboBox.ItemsSource = _detectedDisks;
            DestinationDriveComboBox.ItemsSource = _detectedDisks;

            DiskInfo? windowsDisk =
                _detectedDisks.FirstOrDefault(
                    d => d.IsWindowsDisk);

            if (windowsDisk != null)
            {
                SourceDriveComboBox.SelectedItem =
                    windowsDisk;
            }

            DestinationDriveComboBox.SelectedItem = null;

            DisplayDetectedDisks();

            AnalyzeButton.IsEnabled =
                _detectedDisks.Count >= 2;

            ReviewCloneButton.IsEnabled = false;
            PrepareSnapshotButton.IsEnabled = false;
            PrepareCloneLayoutButton.IsEnabled = false;
            _clonePlanLocked = true;
            ValidateWriteGuardButton.IsEnabled = true;

            LockClonePlanButton.IsEnabled = false;

            // 16.8 Important: reset the button on a fresh detection
            TestTargetBackendButton.IsEnabled = false;

            FinalConfirmationPanel.Visibility =
                Visibility.Collapsed;

            _currentSnapshot = null;
            _sourceFingerprint = null;
            _destinationFingerprint = null;
            _clonePlanLocked = false;

            int windowsCount =
                _detectedDisks.Count(
                    d => d.IsWindowsDisk);

            StatusText.Text =
                $"Detected {_detectedDisks.Count} disk(s) • " +
                $"Windows disk(s): {windowsCount}";

            StatusText.Foreground =
                Brushes.LightGreen;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Disk detection failed.";
            StatusText.Foreground = Brushes.Red;

            MessageBox.Show(
                ex.Message,
                "Disk Detection Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            DetectDrivesButton.IsEnabled = true;
        }
    }


    // ============================================================
    // ANALYZE
    // ============================================================

    private void AnalyzeButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SourceDriveComboBox.SelectedItem
                is not DiskInfo source ||
            DestinationDriveComboBox.SelectedItem
                is not DiskInfo destination)
        {
            AnalysisText.Text =
                "Please select both a source and destination disk.";

            return;
        }

        if (source.Number == destination.Number)
        {
            AnalysisText.Text =
                "ERROR: Source and destination cannot be the same disk.";

            return;
        }

        if (!source.IsWindowsDisk)
        {
            AnalysisText.Text =
                "ERROR: The selected source disk does not contain " +
                "the current Windows installation.";

            return;
        }

        if (destination.Size < source.Size)
        {
            AnalysisText.Text =
                "ERROR: Destination disk is too small.\n\n" +
                $"Source:      {FormatBytes(source.Size)}\n" +
                $"Destination: {FormatBytes(destination.Size)}";

            return;
        }

        StringBuilder analysis =
            new StringBuilder();

        analysis.AppendLine("✓ SOURCE");
        analysis.AppendLine(
            $"Disk {source.Number} - {source.FriendlyName}");
        analysis.AppendLine(
            $"Capacity: {FormatBytes(source.Size)}");
        analysis.AppendLine(
            $"Partition Style: {source.PartitionStyle}");
        analysis.AppendLine();

        analysis.AppendLine("✓ DESTINATION");
        analysis.AppendLine(
            $"Disk {destination.Number} - {destination.FriendlyName}");
        analysis.AppendLine(
            $"Capacity: {FormatBytes(destination.Size)}");
        analysis.AppendLine(
            $"Partition Style: {destination.PartitionStyle}");
        analysis.AppendLine();

        analysis.AppendLine(
            "✓ Source and destination are different.");

        analysis.AppendLine(
            "✓ Destination is large enough.");

        analysis.AppendLine();

        analysis.AppendLine(
            "VSS snapshot will be used so the Windows " +
            "source can be captured consistently.");

        analysis.AppendLine();

        analysis.AppendLine(
            "READ-ONLY ANALYSIS");

        analysis.AppendLine(
            "No partitions will be created.");

        analysis.AppendLine(
            "No files will be copied.");

        analysis.AppendLine(
            "No data will be erased.");

        analysis.AppendLine(
            "No disk will be modified.");

        AnalysisText.Text =
            analysis.ToString();

        ReviewCloneButton.IsEnabled = true;

        StatusText.Text =
            "Clone analysis complete — READ-ONLY.";

        StatusText.Foreground =
            Brushes.LightGreen;
    }


    // ============================================================
    // REVIEW
    // ============================================================

    private void ReviewCloneButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SourceDriveComboBox.SelectedItem
                is not DiskInfo source ||
            DestinationDriveComboBox.SelectedItem
                is not DiskInfo destination)
        {
            return;
        }

        FinalConfirmationText.Text =
            $"SOURCE\n" +
            $"Disk {source.Number} - {source.FriendlyName}\n" +
            $"Capacity: {FormatBytes(source.Size)}\n\n" +

            $"DESTINATION\n" +
            $"Disk {destination.Number} - " +
            $"{destination.FriendlyName}\n" +
            $"Capacity: {FormatBytes(destination.Size)}\n\n" +

            "The source disk will not be modified by the " +
            "current preparation stages.\n\n" +

            "The destination is the disk that the future " +
            "clone engine would overwrite.";

        ConfirmationTextBox.Text = "";

        ConfirmClonePlanButton.IsEnabled = false;

        FinalConfirmationPanel.Visibility =
            Visibility.Visible;

        FinalConfirmationPanel.BringIntoView();

        StatusText.Text =
            "Review the clone plan and type CLONE.";

        StatusText.Foreground =
            Brushes.Orange;
    }


    // ============================================================
    // CONFIRMATION TEXT
    // ============================================================

    private void ConfirmationTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        string value =
            ConfirmationTextBox.Text.Trim();

        ConfirmClonePlanButton.IsEnabled =
            string.Equals(
                value,
                "CLONE",
                StringComparison.OrdinalIgnoreCase);
    }


    // ============================================================
    // CONFIRM CLONE PLAN
    // ============================================================

    private void ConfirmClonePlanButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!string.Equals(
                ConfirmationTextBox.Text.Trim(),
                "CLONE",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (SourceDriveComboBox.SelectedItem
                is not DiskInfo source ||
            DestinationDriveComboBox.SelectedItem
                is not DiskInfo destination)
        {
            return;
        }

        if (source.Number == destination.Number)
        {
            MessageBox.Show(
                "Source and destination cannot be the same disk.",
                "WinClone",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
        }

        _sourceFingerprint =
            BuildDiskFingerprint(source);

        _destinationFingerprint =
            BuildDiskFingerprint(destination);

        PrepareSnapshotButton.IsEnabled = true;

        PrepareCloneLayoutButton.IsEnabled = false;
        _clonePlanLocked = true;
        LockClonePlanButton.IsEnabled = false;

        ConfirmClonePlanButton.IsEnabled = false;
        ConfirmationTextBox.IsEnabled = false;

        StatusText.Text =
            "Clone plan confirmed — actual clone engine is not active.";

        StatusText.Foreground =
            Brushes.Orange;

        MessageBox.Show(
            "Clone plan confirmed.\n\n" +
            "No disk has been modified.\n\n" +
            "The actual cloning engine is not active yet.",
            "WinClone",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }


    // ============================================================
    // PREPARE WINDOWS SNAPSHOT
    // ============================================================

    private async void PrepareSnapshotButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            if (!IsRunningAsAdministrator())
            {
                MessageBox.Show(
                    "WinClone needs Administrator privileges " +
                    "to create a Windows VSS snapshot.\n\n" +
                    "Close WinClone and run it as Administrator.",
                    "Administrator Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (SourceDriveComboBox.SelectedItem
                    is not DiskInfo source)
            {
                MessageBox.Show(
                    "Please select the Windows source disk.",
                    "WinClone",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (!source.IsWindowsDisk)
            {
                MessageBox.Show(
                    "The selected source does not contain the " +
                    "current Windows installation.",
                    "WinClone",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            _sourceFingerprint =
                BuildDiskFingerprint(source);

            PrepareSnapshotButton.IsEnabled = false;

            StatusText.Text =
                "Creating Windows VSS snapshot...";

            StatusText.Foreground =
                Brushes.Orange;

            VssSnapshotInfo snapshot =
                await CreateWindowsSnapshotAsync();

            _currentSnapshot =
                snapshot;

            // Re-scan after snapshot creation.
            List<DiskInfo> disks =
                await GetDisksAsync();

            DiskInfo? currentSource =
                disks.FirstOrDefault(
                    d => d.Number == source.Number);

            if (currentSource == null)
            {
                throw new InvalidOperationException(
                    "The source disk could not be found " +
                    "after VSS snapshot creation.");
            }

            string currentFingerprint =
                BuildDiskFingerprint(currentSource);

            if (!string.Equals(
                    _sourceFingerprint,
                    currentFingerprint,
                    StringComparison.Ordinal))
            {
                _currentSnapshot = null;

                throw new InvalidOperationException(
                    "The source disk changed after the VSS " +
                    "snapshot was created.\n\n" +
                    "WinClone stopped for safety.");
            }

            AnalysisText.Text =
                "VSS SNAPSHOT VERIFIED\n\n" +

                $"Shadow ID:\n" +
                $"{snapshot.ShadowId}\n\n" +

                $"Device Object:\n" +
                $"{snapshot.DeviceObject}\n\n" +

                $"Original Volume:\n" +
                $"{snapshot.VolumeName}\n\n" +

                "SOURCE VERIFICATION\n" +
                "✓ Windows source disk found again\n" +
                "✓ Source disk fingerprint is unchanged\n" +
                "✓ VSS snapshot identity captured\n\n" +

                "DESTINATION\n" +
                "✓ Destination disk has NOT been modified\n\n" +

                "READY FOR CLONE LAYOUT PREPARATION.";

            PrepareCloneLayoutButton.IsEnabled = true;

            StatusText.Text =
                "VSS snapshot verified successfully.";

            StatusText.Foreground =
                Brushes.LightGreen;
        }
        catch (Exception ex)
        {
            _currentSnapshot = null;

            StatusText.Text =
                "VSS snapshot preparation failed.";

            StatusText.Foreground =
                Brushes.Red;

            MessageBox.Show(
                ex.Message,
                "VSS Snapshot Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            PrepareSnapshotButton.IsEnabled = true;
        }
    }


    // ============================================================
    // PREPARE CLONE LAYOUT
    // ============================================================

    private async void PrepareCloneLayoutButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            if (_currentSnapshot == null)
            {
                MessageBox.Show(
                    "A verified VSS snapshot is required first.",
                    "WinClone",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (SourceDriveComboBox.SelectedItem
                    is not DiskInfo source ||
                DestinationDriveComboBox.SelectedItem
                    is not DiskInfo destination)
            {
                MessageBox.Show(
                    "Please select both disks.",
                    "WinClone",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            PrepareCloneLayoutButton.IsEnabled = false;

            StatusText.Text =
                "Re-checking disks and preparing layout...";

            StatusText.Foreground =
                Brushes.Orange;

            List<DiskInfo> disks =
                await GetDisksAsync();

            DiskInfo? currentSource =
                disks.FirstOrDefault(
                    d => d.Number == source.Number);

            DiskInfo? currentDestination =
                disks.FirstOrDefault(
                    d => d.Number == destination.Number);

            if (currentSource == null)
            {
                throw new InvalidOperationException(
                    "The source disk is no longer available.");
            }

            if (currentDestination == null)
            {
                throw new InvalidOperationException(
                    "The destination disk is no longer available.");
            }

            if (currentSource.Number ==
                currentDestination.Number)
            {
                throw new InvalidOperationException(
                    "Source and destination cannot be the same disk.");
            }

            string currentSourceFingerprint =
                BuildDiskFingerprint(currentSource);

            if (!string.Equals(
                    _sourceFingerprint,
                    currentSourceFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The source disk changed after the VSS snapshot.\n\n" +
                    "WinClone stopped for safety.");
            }

            string currentDestinationFingerprint =
                BuildDiskFingerprint(currentDestination);

            _destinationFingerprint =
                BuildDiskFingerprint(destination);

            if (!string.Equals(
                    _destinationFingerprint,
                    currentDestinationFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The destination disk changed.\n\n" +
                    "WinClone stopped for safety.");
            }

            if (!currentSource.IsWindowsDisk)
            {
                throw new InvalidOperationException(
                    "The selected source no longer contains " +
                    "the current Windows installation.");
            }

            if (currentDestination.Size <
                currentSource.Size)
            {
                throw new InvalidOperationException(
                    "The destination disk is too small.");
            }

            long additionalSpace =
                currentDestination.Size -
                currentSource.Size;

            AnalysisText.Text =
                BuildCloneLayoutPreview(
                    currentSource,
                    currentDestination,
                    additionalSpace);

            LockClonePlanButton.IsEnabled = true;

            StatusText.Text =
                "Clone layout prepared — READ-ONLY.";

            StatusText.Foreground =
                Brushes.LightGreen;
        }
        catch (Exception ex)
        {
            StatusText.Text =
                "Clone layout preparation failed.";

            StatusText.Foreground =
                Brushes.Red;

            MessageBox.Show(
                ex.Message,
                "Clone Layout Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            PrepareCloneLayoutButton.IsEnabled = true;
        }
    }


    // ============================================================
    // BUILD CLONE LAYOUT PREVIEW
    // ============================================================

    private static string BuildCloneLayoutPreview(
        DiskInfo source,
        DiskInfo destination,
        long additionalSpace)
    {
        StringBuilder plan =
            new StringBuilder();

        plan.AppendLine(
            "FINAL CLONE LAYOUT — PREVIEW ONLY");

        plan.AppendLine(
            "=================================");

        plan.AppendLine();

        plan.AppendLine("SOURCE");
        plan.AppendLine("------");

        plan.AppendLine(
            $"Disk {source.Number} - " +
            $"{source.FriendlyName}");

        plan.AppendLine(
            $"Capacity: {FormatBytes(source.Size)}");

        plan.AppendLine(
            $"Partition Style: {source.PartitionStyle}");

        plan.AppendLine();

        plan.AppendLine("DESTINATION");
        plan.AppendLine("-----------");

        plan.AppendLine(
            $"Disk {destination.Number} - " +
            $"{destination.FriendlyName}");

        plan.AppendLine(
            $"Capacity: {FormatBytes(destination.Size)}");

        plan.AppendLine(
            $"Partition Style: {destination.PartitionStyle}");

        plan.AppendLine();

        plan.AppendLine("CAPACITY");
        plan.AppendLine("--------");

        plan.AppendLine(
            $"Source:            {FormatBytes(source.Size)}");

        plan.AppendLine(
            $"Destination:       {FormatBytes(destination.Size)}");

        plan.AppendLine(
            $"Additional space: {FormatBytes(additionalSpace)}");

        plan.AppendLine();

        plan.AppendLine("PARTITION PLAN");
        plan.AppendLine("--------------");

        foreach (PartitionInfo partition
                 in source.Partitions)
        {
            long targetSize =
                partition.Size;

            if (partition.IsWindows)
            {
                targetSize =
                    checked(
                        partition.Size +
                        additionalSpace);
            }

            string type =
                string.IsNullOrWhiteSpace(partition.Type)
                    ? "Unknown"
                    : partition.Type;

            string drive =
                string.IsNullOrWhiteSpace(
                    partition.DriveLetter)
                    ? "-"
                    : partition.DriveLetter;

            plan.AppendLine(
                $"Partition {partition.Number,-2} " +
                $"{type,-15} " +
                $"{FormatBytes(partition.Size),12} -> " +
                $"{FormatBytes(targetSize),12} " +
                $"Drive: {drive}");
        }

        plan.AppendLine();

        plan.AppendLine("SAFETY CHECKS");
        plan.AppendLine("-------------");

        plan.AppendLine(
            "✓ Source and destination are different");

        plan.AppendLine(
            "✓ Destination is large enough");

        plan.AppendLine(
            "✓ VSS snapshot has been verified");

        plan.AppendLine(
            "✓ Source disk fingerprint is unchanged");

        plan.AppendLine(
            "✓ Destination disk identity is unchanged");

        plan.AppendLine();

        plan.AppendLine("WRITE STATUS");
        plan.AppendLine("------------");

        plan.AppendLine(
            "NO PARTITIONS CREATED");

        plan.AppendLine(
            "NO FILES COPIED");

        plan.AppendLine(
            "NO DATA ERASED");

        plan.AppendLine(
            "NO DISK MODIFIED");

        plan.AppendLine();

        plan.AppendLine("IMPORTANT");
        plan.AppendLine("---------");

        plan.AppendLine(
            "This is a READ-ONLY preview.");

        plan.AppendLine(
            "The actual clone engine is not active.");

        return plan.ToString();
    }


    // ============================================================
    // LOCK CLONE PLAN
    // ============================================================

    private async void LockClonePlanButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            if (_currentSnapshot == null)
            {
                MessageBox.Show(
                    "A verified VSS snapshot is required.",
                    "WinClone",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (SourceDriveComboBox.SelectedItem
                    is not DiskInfo source ||
                DestinationDriveComboBox.SelectedItem
                    is not DiskInfo destination)
            {
                MessageBox.Show(
                    "Please select both disks.",
                    "WinClone",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (string.IsNullOrWhiteSpace(
                    _sourceFingerprint))
            {
                throw new InvalidOperationException(
                    "The source fingerprint is missing.");
            }

            if (string.IsNullOrWhiteSpace(
                    _destinationFingerprint))
            {
                throw new InvalidOperationException(
                    "The destination fingerprint is missing.");
            }

            StatusText.Text =
                "Performing final disk verification...";

            StatusText.Foreground =
                Brushes.Orange;

            List<DiskInfo> disks =
                await GetDisksAsync();

            DiskInfo? currentSource =
                disks.FirstOrDefault(
                    d => d.Number == source.Number);

            DiskInfo? currentDestination =
                disks.FirstOrDefault(
                    d => d.Number == destination.Number);

            if (currentSource == null)
            {
                throw new InvalidOperationException(
                    "The source disk is no longer available.");
            }

            if (currentDestination == null)
            {
                throw new InvalidOperationException(
                    "The destination disk is no longer available.");
            }

            if (currentSource.Number ==
                currentDestination.Number)
            {
                throw new InvalidOperationException(
                    "Source and destination are the same disk.");
            }

            string sourceFingerprint =
                BuildDiskFingerprint(currentSource);

            string destinationFingerprint =
                BuildDiskFingerprint(currentDestination);

            if (!string.Equals(
                    _sourceFingerprint,
                    sourceFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "SOURCE DISK CHANGED.\n\n" +
                    "WinClone stopped for safety.");
            }

            if (!string.Equals(
                    _destinationFingerprint,
                    destinationFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "DESTINATION DISK CHANGED.\n\n" +
                    "WinClone stopped for safety.");
            }

            if (!currentSource.IsWindowsDisk)
            {
                throw new InvalidOperationException(
                    "The selected source no longer contains Windows.");
            }

            if (currentDestination.Size <
                currentSource.Size)
            {
                throw new InvalidOperationException(
                    "The destination is no longer large enough.");
            }

            AnalysisText.Text =
                "FINAL CLONE PLAN LOCKED\n\n" +

                "SOURCE\n" +
                $"Disk {currentSource.Number} - " +
                $"{currentSource.FriendlyName}\n" +
                $"Capacity: {FormatBytes(currentSource.Size)}\n\n" +

                "DESTINATION\n" +
                $"Disk {currentDestination.Number} - " +
                $"{currentDestination.FriendlyName}\n" +
                $"Capacity: {FormatBytes(currentDestination.Size)}\n\n" +

                "FINAL VERIFICATION\n" +
                "✓ VSS snapshot exists\n" +
                "✓ Source disk unchanged\n" +
                "✓ Destination disk unchanged\n" +
                "✓ Source still contains Windows\n" +
                "✓ Destination is large enough\n" +
                "✓ Source and destination are different\n\n" +

                "WRITE STATUS\n" +
                "-------------\n" +
                "NO PARTITIONS CREATED\n" +
                "NO FILES COPIED\n" +
                "NO DATA ERASED\n" +
                "NO DISK MODIFIED\n\n" +

                "CLONE ENGINE STATUS\n" +
                "-------------------\n" +
                "NOT IMPLEMENTED\n\n" +

                "The clone plan is locked.\n" +
                "The physical clone engine is still disabled.";

            SourceDriveComboBox.IsEnabled = false;
            DestinationDriveComboBox.IsEnabled = false;

            DetectDrivesButton.IsEnabled = false;
            AnalyzeButton.IsEnabled = false;
            ReviewCloneButton.IsEnabled = false;

            ConfirmClonePlanButton.IsEnabled = false;
            PrepareSnapshotButton.IsEnabled = false;
            PrepareCloneLayoutButton.IsEnabled = false;
            LockClonePlanButton.IsEnabled = false;
            SimulateCloneButton.IsEnabled = true;
            StatusText.Text =
                "Clone plan locked — READ-ONLY.";

            StatusText.Foreground =
                Brushes.LightGreen;
        }
        catch (Exception ex)
        {
            StatusText.Text =
                "Final verification failed.";

            StatusText.Foreground =
                Brushes.Red;

            MessageBox.Show(
                ex.Message,
                "Final Verification Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }


    // ============================================================
    // DISK DETECTION
    // ============================================================

    private static async Task<List<DiskInfo>> GetDisksAsync()
    {
        string script = """
$ErrorActionPreference = 'Stop'

$windowsPartition =
    Get-Partition -DriveLetter C -ErrorAction SilentlyContinue

$windowsDiskNumber = $null

if ($null -ne $windowsPartition) {
    $windowsDiskNumber = $windowsPartition.DiskNumber
}

$disks = @(
    Get-Disk | ForEach-Object {

        $disk = $_

        $partitions = @(
            Get-Partition -DiskNumber $disk.Number -ErrorAction SilentlyContinue |
            ForEach-Object {

                $partition = $_

                $volume = $null

                if ($partition.DriveLetter) {
                    $volume =
                        Get-Volume `
                            -DriveLetter $partition.DriveLetter `
                            -ErrorAction SilentlyContinue
                }

                $isWindows = $false

                if ($partition.DriveLetter -eq 'C') {
                    $isWindows = $true
                }

                [PSCustomObject]@{
                    Number =
                        [int]$partition.PartitionNumber

                    DriveLetter =
                        if ($partition.DriveLetter) {
                            [string]$partition.DriveLetter
                        }
                        else {
                            ""
                        }

                    Size =
                        [long]$partition.Size

                    Type =
                        if ($partition.Type) {
                            [string]$partition.Type
                        }
                        else {
                            ""
                        }

                    FileSystem =
                        if ($null -ne $volume -and $volume.FileSystem) {
                            [string]$volume.FileSystem
                        }
                        else {
                            ""
                        }

                    FileSystemLabel =
                        if ($null -ne $volume -and $volume.FileSystemLabel) {
                            [string]$volume.FileSystemLabel
                        }
                        else {
                            ""
                        }

                    IsBoot =
                        [bool]$partition.IsBoot

                    IsSystem =
                        [bool]$partition.IsSystem

                    IsHidden =
                        [bool]$partition.IsHidden

                    IsWindows =
                        [bool]$isWindows
                }
            }
        )

        [PSCustomObject]@{
            Number =
                [int]$disk.Number

            FriendlyName =
                [string]$disk.FriendlyName

            Size =
                [long]$disk.Size

            PartitionStyle =
                [string]$disk.PartitionStyle

            HealthStatus =
                [string]$disk.HealthStatus

            OperationalStatus =
                [string]$disk.OperationalStatus

            IsOffline =
                [bool]$disk.IsOffline

            IsReadOnly =
                [bool]$disk.IsReadOnly

            IsWindowsDisk =
                ($null -ne $windowsDiskNumber -and
                 $disk.Number -eq $windowsDiskNumber)

            Partitions =
                $partitions
        }
    }
)

$disks | ConvertTo-Json -Depth 8 -Compress
""";

        string output =
            await RunPowerShellAsync(script);

        if (string.IsNullOrWhiteSpace(output))
        {
            return new List<DiskInfo>();
        }

        using JsonDocument document =
            JsonDocument.Parse(output);

        JsonElement root =
            document.RootElement;

        var result =
            new List<DiskInfo>();

        if (root.ValueKind ==
            JsonValueKind.Array)
        {
            foreach (JsonElement item
                     in root.EnumerateArray())
            {
                result.Add(
                    ParseDisk(item));
            }
        }
        else if (root.ValueKind ==
                 JsonValueKind.Object)
        {
            result.Add(
                ParseDisk(root));
        }

        return result;
    }


    // ============================================================
    // POWERSHELL
    // ============================================================

    private static async Task<string> RunPowerShellAsync(
        string script)
    {
        string encoded =
            Convert.ToBase64String(
                Encoding.Unicode.GetBytes(script));

        ProcessStartInfo startInfo =
            new ProcessStartInfo
            {
                FileName =
                    "powershell.exe",

                Arguments =
                    $"-NoProfile -NonInteractive " +
                    $"-EncodedCommand {encoded}",

                RedirectStandardOutput = true,
                RedirectStandardError = true,

                UseShellExecute = false,
                CreateNoWindow = true
            };

        using Process process =
            new Process
            {
                StartInfo = startInfo
            };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "Could not start Windows PowerShell.");
        }

        Task<string> outputTask =
            process.StandardOutput.ReadToEndAsync();

        Task<string> errorTask =
            process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        string output =
            await outputTask;

        string error =
            await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "PowerShell command failed.\n\n" +
                output +
                "\n\n" +
                error);
        }

        return output.Trim();
    }


    // ============================================================
    // PARSE DISK
    // ============================================================

    private static DiskInfo ParseDisk(
        JsonElement item)
    {
        DiskInfo disk =
            new DiskInfo
            {
                Number =
                    GetInt(item, "Number"),

                FriendlyName =
                    GetString(item, "FriendlyName"),

                Size =
                    GetLong(item, "Size"),

                PartitionStyle =
                    GetString(item, "PartitionStyle"),

                HealthStatus =
                    GetString(item, "HealthStatus"),

                OperationalStatus =
                    GetString(item, "OperationalStatus"),

                IsOffline =
                    GetBool(item, "IsOffline"),

                IsReadOnly =
                    GetBool(item, "IsReadOnly"),

                IsWindowsDisk =
                    GetBool(item, "IsWindowsDisk")
            };

        if (item.TryGetProperty(
                "Partitions",
                out JsonElement partitions))
        {
            if (partitions.ValueKind ==
                JsonValueKind.Array)
            {
                foreach (JsonElement partition
                         in partitions.EnumerateArray())
                {
                    disk.Partitions.Add(
                        ParsePartition(partition));
                }
            }
            else if (partitions.ValueKind ==
                     JsonValueKind.Object)
            {
                disk.Partitions.Add(
                    ParsePartition(partitions));
            }
        }

        return disk;
    }


    // ============================================================
    // PARSE PARTITION
    // ============================================================

    private static PartitionInfo ParsePartition(
        JsonElement item)
    {
        return new PartitionInfo
        {
            Number =
                GetInt(item, "Number"),

            DriveLetter =
                GetString(item, "DriveLetter"),

            Size =
                GetLong(item, "Size"),

            Type =
                GetString(item, "Type"),

            FileSystem =
                GetString(item, "FileSystem"),

            FileSystemLabel =
                GetString(item, "FileSystemLabel"),

            IsBoot =
                GetBool(item, "IsBoot"),

            IsSystem =
                GetBool(item, "IsSystem"),

            IsHidden =
                GetBool(item, "IsHidden"),

            IsWindows =
                GetBool(item, "IsWindows")
        };
    }


    // ============================================================
    // JSON HELPERS
    // ============================================================

    private static string GetString(
        JsonElement item,
        string name)
    {
        if (!item.TryGetProperty(
                name,
                out JsonElement property))
        {
            return "";
        }

        return property.ValueKind ==
               JsonValueKind.Null
            ? ""
            : property.ToString();
    }

    private static int GetInt(
        JsonElement item,
        string name)
    {
        if (!item.TryGetProperty(
                name,
                out JsonElement property))
        {
            return 0;
        }

        if (property.TryGetInt32(
                out int value))
        {
            return value;
        }

        if (int.TryParse(
                property.ToString(),
                out value))
        {
            return value;
        }

        return 0;
    }

    private static long GetLong(
        JsonElement item,
        string name)
    {
        if (!item.TryGetProperty(
                name,
                out JsonElement property))
        {
            return 0;
        }

        if (property.TryGetInt64(
                out long value))
        {
            return value;
        }

        if (long.TryParse(
                property.ToString(),
                out value))
        {
            return value;
        }

        return 0;
    }

    private static bool GetBool(
        JsonElement item,
        string name)
    {
        if (!item.TryGetProperty(
                name,
                out JsonElement property))
        {
            return false;
        }

        if (property.ValueKind ==
            JsonValueKind.True)
        {
            return true;
        }

        if (property.ValueKind ==
            JsonValueKind.False)
        {
            return false;
        }

        if (bool.TryParse(
                property.ToString(),
                out bool value))
        {
            return value;
        }

        return false;
    }


    // ============================================================
    // VSS
    // ============================================================

    private static async Task<VssSnapshotInfo>
        CreateWindowsSnapshotAsync()
    {
        string script = """
$ErrorActionPreference = 'Stop'

$shadowClass =
    [WMIClass]"root\cimv2:Win32_ShadowCopy"

$result =
    $shadowClass.Create(
        "C:\",
        "ClientAccessible"
    )

if ($null -eq $result) {
    throw "VSS returned no result."
}

$returnValue =
    [int]$result.ReturnValue

if ($returnValue -ne 0) {
    throw (
        "Win32_ShadowCopy.Create failed. " +
        "Return code: " +
        $returnValue
    )
}

$shadowId =
    [string]$result.ShadowID

$shadow = $null

if (-not [string]::IsNullOrWhiteSpace($shadowId)) {
    $shadow =
        Get-WmiObject Win32_ShadowCopy |
        Where-Object {
            $_.ID -eq $shadowId
        }
}

if ($null -eq $shadow) {
    $shadow =
        Get-WmiObject Win32_ShadowCopy |
        Where-Object {
            $_.VolumeName -eq "C:\"
        } |
        Sort-Object InstallDate -Descending |
        Select-Object -First 1
}

if ($null -eq $shadow) {
    throw (
        "VSS reported success, but the new shadow copy " +
        "could not be found."
    )
}

if ([string]::IsNullOrWhiteSpace(
        [string]$shadow.DeviceObject))
{
    throw "VSS shadow copy has no DeviceObject."
}

[PSCustomObject]@{
    ShadowID =
        [string]$shadow.ID

    DeviceObject =
        [string]$shadow.DeviceObject

    VolumeName =
        [string]$shadow.VolumeName

    ClientAccessible =
        [bool]$shadow.ClientAccessible
} |
ConvertTo-Json -Compress
""";

        string output =
            await RunPowerShellAsync(script);

        if (string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException(
                "Windows VSS returned no snapshot information.");
        }

        using JsonDocument document =
            JsonDocument.Parse(output);

        JsonElement root =
            document.RootElement;

        string shadowId =
            GetString(root, "ShadowID");

        string deviceObject =
            GetString(root, "DeviceObject");

        string volumeName =
            GetString(root, "VolumeName");

        bool clientAccessible =
            GetBool(root, "ClientAccessible");

        if (string.IsNullOrWhiteSpace(shadowId))
        {
            throw new InvalidOperationException(
                "VSS returned no Shadow ID.");
        }

        if (string.IsNullOrWhiteSpace(deviceObject))
        {
            throw new InvalidOperationException(
                "VSS returned no Device Object.");
        }

        return new VssSnapshotInfo
        {
            ShadowId = shadowId,
            DeviceObject = deviceObject,
            VolumeName = volumeName,
            ClientAccessible = clientAccessible
        };
    }


    // ============================================================
    // ADMIN CHECK
    // ============================================================

    private static bool IsRunningAsAdministrator()
    {
        using System.Security.Principal.WindowsIdentity identity =
            System.Security.Principal.WindowsIdentity.GetCurrent();

        System.Security.Principal.WindowsPrincipal principal =
            new System.Security.Principal.WindowsPrincipal(
                identity);

        return principal.IsInRole(
            System.Security.Principal.WindowsBuiltInRole.Administrator);
    }


    // ============================================================
    // FINGERPRINT
    // ============================================================

    private static string BuildDiskFingerprint(
        DiskInfo disk)
    {
        string partitions =
            string.Join(
                "|",
                disk.Partitions.Select(
                    p =>
                        $"{p.Number}:{p.Size}:{p.Type}:" +
                        $"{p.FileSystem}:{p.DriveLetter}:" +
                        $"{p.IsSystem}:{p.IsBoot}:{p.IsWindows}"));

        return
            $"{disk.Number}|" +
            $"{disk.FriendlyName}|" +
            $"{disk.Size}|" +
            $"{disk.PartitionStyle}|" +
            $"{disk.IsWindowsDisk}|" +
            $"{partitions}";
    }


    // ============================================================
    // UI DISK DISPLAY
    // ============================================================

    private void DisplayDetectedDisks()
    {
        DiskListPanel.Children.Clear();

        foreach (DiskInfo disk
                 in _detectedDisks)
        {
            Border card =
                new Border
                {
                    Background =
                        disk.IsWindowsDisk
                            ? new SolidColorBrush(
                                Color.FromRgb(
                                    28,
                                    61,
                                    39))
                            : new SolidColorBrush(
                                Color.FromRgb(
                                    70,
                                    70,
                                    70)),

                    BorderBrush =
                        disk.IsWindowsDisk
                            ? Brushes.Green
                            : Brushes.Gray,

                    BorderThickness =
                        new Thickness(1),

                    CornerRadius =
                        new CornerRadius(10),

                    Padding =
                        new Thickness(18),

                    Margin =
                        new Thickness(0, 0, 0, 14)
                };

            StackPanel panel =
                new StackPanel();

            TextBlock title =
                new TextBlock
                {
                    Text =
                        disk.IsWindowsDisk
                            ? $"Disk {disk.Number} - " +
                              $"{disk.FriendlyName} [ WINDOWS DISK ]"
                            : $"Disk {disk.Number} - " +
                              $"{disk.FriendlyName}",

                    Foreground =
                        Brushes.White,

                    FontSize = 20,

                    FontWeight =
                        FontWeights.Bold
                };

            panel.Children.Add(title);

            panel.Children.Add(
                new TextBlock
                {
                    Text =
                        $"Size: {FormatBytes(disk.Size)}",

                    Foreground =
                        Brushes.White,

                    Margin =
                        new Thickness(0, 8, 0, 0)
                });

            panel.Children.Add(
                new TextBlock
                {
                    Text =
                        $"Partition Style: " +
                        $"{disk.PartitionStyle}",

                    Foreground =
                        Brushes.White
                });

            panel.Children.Add(
                new TextBlock
                {
                    Text =
                        $"Health: {disk.HealthStatus}   " +
                        $"Status: {disk.OperationalStatus}",

                    Foreground =
                        Brushes.White
                });

            if (disk.IsWindowsDisk)
            {
                panel.Children.Add(
                    new TextBlock
                    {
                        Text =
                            "✓ Current Windows installation " +
                            "detected on this disk.",

                        Foreground =
                            Brushes.LightGreen,

                        Margin =
                            new Thickness(0, 8, 0, 0)
                    });
            }

            TextBlock partitionHeading =
                new TextBlock
                {
                    Text = "Partitions",

                    Foreground =
                        Brushes.White,

                    FontWeight =
                        FontWeights.Bold,

                    Margin =
                        new Thickness(0, 14, 0, 6)
                };

            panel.Children.Add(partitionHeading);

            foreach (PartitionInfo partition
                     in disk.Partitions)
            {
                panel.Children.Add(
                    new TextBlock
                    {
                        Text =
                            $"Partition {partition.Number}   " +
                            $"{FormatBytes(partition.Size)}   " +
                            $"{partition.FileSystem}   " +
                            $"{partition.Type}   " +
                            $"{partition.DriveLetter}",

                        Foreground =
                            Brushes.White,

                        Margin =
                            new Thickness(0, 2, 0, 0)
                    });
            }

            card.Child = panel;

            DiskListPanel.Children.Add(card);
        }
    }


    // ============================================================
    // FORMAT BYTES
    // ============================================================

    private static string FormatBytes(
        long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024L * 1024)
        {
            return
                $"{bytes / 1024.0:F2} KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return
                $"{bytes / (1024.0 * 1024):F2} MB";
        }

        if (bytes < 1024L * 1024 * 1024 * 1024)
        {
            return
                $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        return
            $"{bytes / (1024.0 * 1024 * 1024 * 1024):F2} TB";
    }

    // ============================================================
    // STEP 13 - CLONE ENGINE DRY RUN
    // ============================================================

    private async void SimulateCloneButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            SimulateCloneButton.IsEnabled = false;

            if (_currentSnapshot == null)
            {
                throw new InvalidOperationException(
                    "No verified VSS snapshot exists.");
            }

            if (string.IsNullOrWhiteSpace(_sourceFingerprint))
            {
                throw new InvalidOperationException(
                    "The source fingerprint is missing.");
            }

            if (string.IsNullOrWhiteSpace(_destinationFingerprint))
            {
                throw new InvalidOperationException(
                    "The destination fingerprint is missing.");
            }

            if (SourceDriveComboBox.SelectedItem
                    is not DiskInfo source)
            {
                throw new InvalidOperationException(
                    "The source disk is not selected.");
            }

            if (DestinationDriveComboBox.SelectedItem
                    is not DiskInfo destination)
            {
                throw new InvalidOperationException(
                    "The destination disk is not selected.");
            }

            StatusText.Text =
                "Running clone engine simulation...";

            StatusText.Foreground =
                Brushes.Orange;

            // Re-read the disks one final time.
            List<DiskInfo> disks =
                await GetDisksAsync();

            DiskInfo? currentSource =
                disks.FirstOrDefault(
                    d => d.Number == source.Number);

            DiskInfo? currentDestination =
                disks.FirstOrDefault(
                    d => d.Number == destination.Number);

            if (currentSource == null)
            {
                throw new InvalidOperationException(
                    "Source disk is no longer available.");
            }

            if (currentDestination == null)
            {
                throw new InvalidOperationException(
                    "Destination disk is no longer available.");
            }

            if (currentSource.Number ==
                currentDestination.Number)
            {
                throw new InvalidOperationException(
                    "Source and destination are the same disk.");
            }

            if (!currentSource.IsWindowsDisk)
            {
                throw new InvalidOperationException(
                    "The source no longer contains Windows.");
            }

            if (currentDestination.Size <
                currentSource.Size)
            {
                throw new InvalidOperationException(
                    "The destination is too small.");
            }

            string sourceFingerprint =
                BuildDiskFingerprint(currentSource);

            string destinationFingerprint =
                BuildDiskFingerprint(currentDestination);

            if (!string.Equals(
                    _sourceFingerprint,
                    sourceFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "SOURCE DISK CHANGED.\n\n" +
                    "Dry-run aborted for safety.");
            }

            if (!string.Equals(
                    _destinationFingerprint,
                    destinationFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "DESTINATION DISK CHANGED.\n\n" +
                    "Dry-run aborted for safety.");
            }

            long additionalSpace =
                currentDestination.Size -
                currentSource.Size;

            StringBuilder log =
                new StringBuilder();

            log.AppendLine(
                "WINCLONE CLONE ENGINE — DRY RUN");

            log.AppendLine(
                "================================");

            log.AppendLine();

            log.AppendLine(
                "THIS IS A SIMULATION ONLY.");

            log.AppendLine(
                "NO DISK WRITE OPERATIONS ARE EXECUTED.");

            log.AppendLine();

            // --------------------------------------------------------
            // SOURCE
            // --------------------------------------------------------

            log.AppendLine("1. SOURCE VALIDATION");
            log.AppendLine("--------------------");

            log.AppendLine(
                $"Source Disk: {currentSource.Number}");

            log.AppendLine(
                $"Source Name: {currentSource.FriendlyName}");

            log.AppendLine(
                $"Source Size: {FormatBytes(currentSource.Size)}");

            log.AppendLine(
                $"Windows Disk: {currentSource.IsWindowsDisk}");

            log.AppendLine(
                "✓ Source validated");

            log.AppendLine();

            // --------------------------------------------------------
            // DESTINATION
            // --------------------------------------------------------

            log.AppendLine("2. DESTINATION VALIDATION");
            log.AppendLine("-------------------------");

            log.AppendLine(
                $"Destination Disk: {currentDestination.Number}");

            log.AppendLine(
                $"Destination Name: {currentDestination.FriendlyName}");

            log.AppendLine(
                $"Destination Size: " +
                $"{FormatBytes(currentDestination.Size)}");

            log.AppendLine(
                "✓ Destination validated");

            log.AppendLine();

            // --------------------------------------------------------
            // VSS
            // --------------------------------------------------------

            log.AppendLine("3. VSS SNAPSHOT");
            log.AppendLine("---------------");

            log.AppendLine(
                $"Shadow ID: {_currentSnapshot.ShadowId}");

            log.AppendLine(
                $"Device Object: {_currentSnapshot.DeviceObject}");

            log.AppendLine(
                $"Original Volume: {_currentSnapshot.VolumeName}");

            log.AppendLine(
                "✓ VSS snapshot available");

            log.AppendLine();

            // --------------------------------------------------------
            // PLANNED DESTINATION RESET
            // --------------------------------------------------------

            log.AppendLine("4. DESTINATION PREPARATION");
            log.AppendLine("--------------------------");

            log.AppendLine(
                "WOULD REMOVE EXISTING DESTINATION PARTITIONS");

            log.AppendLine(
                $"Target Disk: Disk {currentDestination.Number}");

            log.AppendLine(
                "SIMULATION ONLY — NOTHING WILL BE REMOVED");

            log.AppendLine();

            // --------------------------------------------------------
            // GPT
            // --------------------------------------------------------

            log.AppendLine("5. GPT INITIALIZATION");
            log.AppendLine("--------------------");

            log.AppendLine(
                $"Current Source Style: " +
                $"{currentSource.PartitionStyle}");

            log.AppendLine(
                $"Destination Style: " +
                $"{currentDestination.PartitionStyle}");

            log.AppendLine(
                "WOULD CREATE GPT PARTITION TABLE");

            log.AppendLine(
                "SIMULATION ONLY");

            log.AppendLine();

            // --------------------------------------------------------
            // PARTITIONS
            // --------------------------------------------------------

            log.AppendLine("6. PARTITION CREATION PLAN");
            log.AppendLine("--------------------------");

            foreach (PartitionInfo partition
                     in currentSource.Partitions)
            {
                long targetSize =
                    partition.Size;

                if (partition.IsWindows)
                {
                    targetSize =
                        checked(
                            partition.Size +
                            additionalSpace);
                }

                string role;

                if (partition.IsSystem)
                {
                    role = "EFI/System";
                }
                else if (partition.Type
                         .Contains(
                             "Reserved",
                             StringComparison.OrdinalIgnoreCase))
                {
                    role = "MSR/Reserved";
                }
                else if (partition.IsWindows)
                {
                    role = "Windows";
                }
                else
                {
                    role = "Recovery/Other";
                }

                log.AppendLine(
                    $"WOULD CREATE: " +
                    $"{role} | " +
                    $"{FormatBytes(targetSize)}");
            }

            log.AppendLine();

            // --------------------------------------------------------
            // DATA COPY
            // --------------------------------------------------------

            log.AppendLine("7. WINDOWS DATA COPY");
            log.AppendLine("--------------------");

            log.AppendLine(
                "WOULD COPY WINDOWS DATA FROM:");

            log.AppendLine(
                _currentSnapshot.DeviceObject);

            log.AppendLine();

            log.AppendLine(
                "WOULD COPY:");

            log.AppendLine(
                "• Windows system files");

            log.AppendLine(
                "• Installed applications");

            log.AppendLine(
                "• User profile data");

            log.AppendLine(
                "• Windows configuration");

            log.AppendLine();

            log.AppendLine(
                "SIMULATION ONLY — ZERO FILES COPIED");

            log.AppendLine();

            // --------------------------------------------------------
            // BOOT
            // --------------------------------------------------------

            log.AppendLine("8. BOOT CONFIGURATION");
            log.AppendLine("---------------------");

            log.AppendLine(
                "WOULD PREPARE EFI SYSTEM PARTITION");

            log.AppendLine(
                "WOULD CREATE / UPDATE WINDOWS BOOT FILES");

            log.AppendLine(
                "WOULD CONFIGURE BCD");

            log.AppendLine(
                "SIMULATION ONLY");

            log.AppendLine();

            // --------------------------------------------------------
            // VERIFICATION
            // --------------------------------------------------------

            log.AppendLine("9. POST-CLONE VERIFICATION");
            log.AppendLine("--------------------------");

            log.AppendLine(
                "WOULD VERIFY:");

            log.AppendLine(
                "✓ Partition layout");

            log.AppendLine(
                "✓ File system integrity");

            log.AppendLine(
                "✓ Windows boot files");

            log.AppendLine(
                "✓ Destination capacity");

            log.AppendLine(
                "✓ Source/destination separation");

            log.AppendLine();

            // --------------------------------------------------------
            // FINAL RESULT
            // --------------------------------------------------------

            log.AppendLine("10. SIMULATION RESULT");
            log.AppendLine("---------------------");

            log.AppendLine(
                "✓ Source validated");

            log.AppendLine(
                "✓ Destination validated");

            log.AppendLine(
                "✓ VSS snapshot validated");

            log.AppendLine(
                "✓ Fingerprints validated");

            log.AppendLine(
                "✓ Partition plan calculated");

            log.AppendLine();

            log.AppendLine(
                "NO DISK WAS OPENED FOR WRITING.");

            log.AppendLine(
                "NO PARTITION WAS CREATED.");

            log.AppendLine(
                "NO FILE WAS COPIED.");

            log.AppendLine(
                "NO DATA WAS ERASED.");

            log.AppendLine();

            log.AppendLine(
                "ACTUAL CLONE ENGINE:");

            log.AppendLine(
                "NOT ACTIVE");

            log.AppendLine();

            log.AppendLine(
                "DRY RUN COMPLETE.");

            AnalysisText.Text =
                log.ToString();

            StatusText.Text =
                "Clone engine simulation completed — NO WRITE.";

            StatusText.Foreground =
                Brushes.LightGreen;
        }
        catch (Exception ex)
        {
            StatusText.Text =
                "Clone simulation failed.";

            StatusText.Foreground =
                Brushes.Red;

            SimulateCloneButton.IsEnabled =
                true;

            MessageBox.Show(
                ex.Message,
                "Clone Simulation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
    // ============================================================
    // STEP 14 - WRITE SAFETY GATE
    // ============================================================

    private async void ValidateWriteGuardButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            ValidateWriteGuardButton.IsEnabled = false;

            StatusText.Text =
                "Validating write safety gate...";

            StatusText.Foreground =
                Brushes.Orange;

            if (!_clonePlanLocked)
            {
                throw new InvalidOperationException(
                    "The clone plan is not locked.");
            }

            if (_currentSnapshot == null)
            {
                throw new InvalidOperationException(
                    "No verified VSS snapshot exists.");
            }

            if (SourceDriveComboBox.SelectedItem
                    is not DiskInfo source)
            {
                throw new InvalidOperationException(
                    "Source disk is not selected.");
            }

            if (DestinationDriveComboBox.SelectedItem
                    is not DiskInfo destination)
            {
                throw new InvalidOperationException(
                    "Destination disk is not selected.");
            }

            if (source.Number == destination.Number)
            {
                throw new InvalidOperationException(
                    "SAFETY STOP: Source and destination are the same disk.");
            }

            if (!source.IsWindowsDisk)
            {
                throw new InvalidOperationException(
                    "SAFETY STOP: Selected source is not the Windows disk.");
            }

            if (destination.IsWindowsDisk)
            {
                throw new InvalidOperationException(
                    "SAFETY STOP: Destination is also marked as a Windows disk.");
            }

            if (destination.Size < source.Size)
            {
                throw new InvalidOperationException(
                    "SAFETY STOP: Destination is smaller than source.");
            }

            if (string.IsNullOrWhiteSpace(_sourceFingerprint))
            {
                throw new InvalidOperationException(
                    "Source fingerprint is missing.");
            }

            if (string.IsNullOrWhiteSpace(_destinationFingerprint))
            {
                throw new InvalidOperationException(
                    "Destination fingerprint is missing.");
            }

            List<DiskInfo> disks =
                await GetDisksAsync();

            DiskInfo? currentSource =
                disks.FirstOrDefault(
                    d => d.Number == source.Number);

            DiskInfo? currentDestination =
                disks.FirstOrDefault(
                    d => d.Number == destination.Number);

            if (currentSource == null)
            {
                throw new InvalidOperationException(
                    "Source disk is no longer available.");
            }

            if (currentDestination == null)
            {
                throw new InvalidOperationException(
                    "Destination disk is no longer available.");
            }

            string currentSourceFingerprint =
                BuildDiskFingerprint(currentSource);

            string currentDestinationFingerprint =
                BuildDiskFingerprint(currentDestination);

            if (!string.Equals(
                    _sourceFingerprint,
                    currentSourceFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "SAFETY STOP: Source disk fingerprint changed.");
            }

            if (!string.Equals(
                    _destinationFingerprint,
                    currentDestinationFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "SAFETY STOP: Destination disk fingerprint changed.");
            }

            StringBuilder report =
                new StringBuilder();

            report.AppendLine(
                "WINCLONE WRITE SAFETY GATE");

            report.AppendLine(
                "=========================");

            report.AppendLine();

            report.AppendLine(
                "✓ Clone plan is locked");

            report.AppendLine(
                "✓ VSS snapshot is present");

            report.AppendLine(
                "✓ Source disk is unchanged");

            report.AppendLine(
                "✓ Destination disk is unchanged");

            report.AppendLine(
                "✓ Source contains Windows");

            report.AppendLine(
                "✓ Source and destination are different");

            report.AppendLine(
                "✓ Destination is large enough");

            report.AppendLine();

            report.AppendLine(
                "SOURCE");

            report.AppendLine(
                $"Disk {currentSource.Number} - " +
                $"{currentSource.FriendlyName}");

            report.AppendLine();

            report.AppendLine(
                "DESTINATION");

            report.AppendLine(
                $"Disk {currentDestination.Number} - " +
                $"{currentDestination.FriendlyName}");

            report.AppendLine();

            report.AppendLine(
                "PHYSICAL WRITE ENGINE");

            report.AppendLine(
                "---------------------");

            report.AppendLine(
                "DISABLED");

            report.AppendLine();

            report.AppendLine(
                "WRITE GATE RESULT");

            report.AppendLine(
                "-----------------");

            report.AppendLine(
                "✓ ALL SAFETY CONDITIONS PASSED");

            report.AppendLine();

            report.AppendLine(
                "NO PHYSICAL DISK WAS OPENED FOR WRITING.");

            report.AppendLine(
                "NO PARTITIONS WERE CREATED.");

            report.AppendLine(
                "NO FILES WERE COPIED.");

            report.AppendLine(
                "NO DATA WAS ERASED.");

            report.AppendLine();

            report.AppendLine(
                "READY FOR FUTURE CLONE ENGINE IMPLEMENTATION.");

            AnalysisText.Text =
                report.ToString();

            StatusText.Text =
                "Write safety gate passed — WRITE ENGINE DISABLED.";

            StatusText.Foreground =
                Brushes.LightGreen;

            // 16.3 Enable it after the engine self-test (or Write Guard validation in this snapshot)
            TestTargetBackendButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StatusText.Text =
                "Write safety gate failed.";

            StatusText.Foreground =
                Brushes.Red;

            ValidateWriteGuardButton.IsEnabled =
                _clonePlanLocked;

            MessageBox.Show(
                ex.Message,
                "Write Safety Gate",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // ============================================================
    // STEP 16 - CLONE TARGET ABSTRACTION
    // ============================================================
    private interface ICloneTarget : IDisposable
    {
        long Length { get; }

        Task InitializeAsync(
            long size,
            CancellationToken cancellationToken);

        Task WriteAsync(
            long offset,
            byte[] data,
            CancellationToken cancellationToken);

        Task<byte[]> ReadAsync(
            long offset,
            int count,
            CancellationToken cancellationToken);

        Task FlushAsync(
            CancellationToken cancellationToken);
    }

    private sealed class TestFileCloneTarget : ICloneTarget
    {
        private readonly string _path;

        private FileStream? _stream;

        public TestFileCloneTarget(string path)
        {
            _path = path;
        }

        public long Length =>
            _stream?.Length ?? 0;

        public async Task InitializeAsync(
            long size,
            CancellationToken cancellationToken)
        {
            string? directory =
                Path.GetDirectoryName(_path);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _stream =
                new FileStream(
                    _path,
                    FileMode.Create,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1024 * 1024,
                    options:
                        FileOptions.Asynchronous |
                        FileOptions.SequentialScan);

            _stream.SetLength(size);

            await _stream.FlushAsync(
                cancellationToken);
        }

        public async Task WriteAsync(
            long offset,
            byte[] data,
            CancellationToken cancellationToken)
        {
            if (_stream == null)
            {
                throw new InvalidOperationException(
                    "Test target is not initialized.");
            }

            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset));
            }

            if (offset + data.LongLength >
                _stream.Length)
            {
                throw new InvalidOperationException(
                    "Test write exceeds target size.");
            }

            _stream.Position = offset;

            await _stream.WriteAsync(
                data.AsMemory(),
                cancellationToken);
        }

        public async Task<byte[]> ReadAsync(
            long offset,
            int count,
            CancellationToken cancellationToken)
        {
            if (_stream == null)
            {
                throw new InvalidOperationException(
                    "Test target is not initialized.");
            }

            if (offset < 0 || count < 0)
            {
                throw new ArgumentOutOfRangeException();
            }

            if (offset + count >
                _stream.Length)
            {
                throw new InvalidOperationException(
                    "Test read exceeds target size.");
            }

            byte[] buffer =
                new byte[count];

            _stream.Position = offset;

            int totalRead = 0;

            while (totalRead < count)
            {
                int read =
                    await _stream.ReadAsync(
                        buffer.AsMemory(
                            totalRead,
                            count - totalRead),
                        cancellationToken);

                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            if (totalRead != count)
            {
                throw new InvalidOperationException(
                    "Test target returned incomplete data.");
            }

            return buffer;
        }

        public async Task FlushAsync(
            CancellationToken cancellationToken)
        {
            if (_stream == null)
            {
                throw new InvalidOperationException(
                    "Test target is not initialized.");
            }

            await _stream.FlushAsync(
                cancellationToken);
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _stream = null;
        }
    }

    private sealed class TestTargetCloneEngine
    {
        public async Task<CloneEngineResult> ExecuteAsync(
            ICloneTarget target,
            IProgress<CloneProgress> progress,
            CancellationToken cancellationToken)
        {
            const long targetSize =
                64L * 1024 * 1024;

            Report(
                progress,
                0,
                "TEST TARGET",
                "Creating 64 MiB test image.");

            await target.InitializeAsync(
                targetSize,
                cancellationToken);

            // ----------------------------------------------------
            // TEST BLOCK 1
            // ----------------------------------------------------

            byte[] header =
                Encoding.ASCII.GetBytes(
                    "WINCLONE-TEST-IMAGE");

            Report(
                progress,
                20,
                "WRITE TEST",
                "Writing test header.");

            await target.WriteAsync(
                0,
                header,
                cancellationToken);

            // ----------------------------------------------------
            // TEST BLOCK 2
            // ----------------------------------------------------

            byte[] blockA =
                CreatePattern(
                    1024 * 1024,
                    0xA5);

            Report(
                progress,
                40,
                "BLOCK WRITE",
                "Writing test data block A.");

            await target.WriteAsync(
                1024 * 1024,
                blockA,
                cancellationToken);

            // ----------------------------------------------------
            // TEST BLOCK 3
            // ----------------------------------------------------

            byte[] blockB =
                CreatePattern(
                    1024 * 1024,
                    0x5A);

            Report(
                progress,
                60,
                "BLOCK WRITE",
                "Writing test data block B.");

            await target.WriteAsync(
                8 * 1024 * 1024,
                blockB,
                cancellationToken);

            // ----------------------------------------------------
            // FLUSH
            // ----------------------------------------------------

            Report(
                progress,
                70,
                "FLUSH",
                "Flushing test target.");

            await target.FlushAsync(
                cancellationToken);

            // ----------------------------------------------------
            // VERIFY A
            // ----------------------------------------------------

            Report(
                progress,
                80,
                "VERIFY",
                "Reading block A back.");

            byte[] readA =
                await target.ReadAsync(
                    1024 * 1024,
                    blockA.Length,
                    cancellationToken);

            if (!blockA.SequenceEqual(readA))
            {
                throw new InvalidOperationException(
                    "Test verification failed for block A.");
            }

            // ----------------------------------------------------
            // VERIFY B
            // ----------------------------------------------------

            Report(
                progress,
                90,
                "VERIFY",
                "Reading block B back.");

            byte[] readB =
                await target.ReadAsync(
                    8 * 1024 * 1024,
                    blockB.Length,
                    cancellationToken);

            if (!blockB.SequenceEqual(readB))
            {
                throw new InvalidOperationException(
                    "Test verification failed for block B.");
            }

            // ----------------------------------------------------
            // HEADER VERIFY
            // ----------------------------------------------------

            byte[] readHeader =
                await target.ReadAsync(
                    0,
                    header.Length,
                    cancellationToken);

            if (!header.SequenceEqual(readHeader))
            {
                throw new InvalidOperationException(
                    "Test verification failed for header.");
            }

            Report(
                progress,
                100,
                "COMPLETE",
                "Test target write/read verification passed.");

            return new CloneEngineResult
            {
                Success = true,

                WasDryRun = false,

                Message =
                    "Safe test-target backend completed. " +
                    "Only a temporary test image was written.",

                PlannedBytes =
                    blockA.Length +
                    blockB.Length +
                    header.Length
            };
        }

        private static byte[] CreatePattern(
            int length,
            byte value)
        {
            byte[] data =
                new byte[length];

            Array.Fill(
                data,
                value);

            return data;
        }

        private static void Report(
            IProgress<CloneProgress> progress,
            int percent,
            string stage,
            string message)
        {
            progress.Report(
                new CloneProgress
                {
                    Percent = percent,
                    Stage = stage,
                    Message = message
                });
        }
    }

    private async void EngineSelfTestButton_Click(
    object sender,
    RoutedEventArgs e)
    {
        await RunFinalMvpSuiteAsync();
    }


    private async void TestTargetBackendButton_Click(
    object sender,
    RoutedEventArgs e)
    {
        try
        {
            if (!_clonePlanLocked)
            {
                throw new InvalidOperationException(
                    "Lock the clone plan first.");
            }

            TestTargetBackendButton.IsEnabled =
    false;
            CloneProgressBar.Value = 0;

            StatusText.Text =
                "Testing safe clone target backend...";

            StatusText.Foreground =
                Brushes.Orange;

            string directory =
                Path.Combine(
                    Path.GetTempPath(),
                    "WinClone-Test");

            string testImage =
                Path.Combine(
                    directory,
                    "clone-test.img");

            _cloneCancellation =
                new CancellationTokenSource();

            Progress<CloneProgress> progress =
                new Progress<CloneProgress>(
                    p =>
                    {
                        CloneProgressBar.Value =
                            p.Percent;

                        StatusText.Text =
                            $"{p.Stage}: {p.Message}";
                    });

            using ICloneTarget target =
                new TestFileCloneTarget(
                    testImage);

            TestTargetCloneEngine engine =
                new TestTargetCloneEngine();

            CloneEngineResult result =
                await engine.ExecuteAsync(
                    target,
                    progress,
                    _cloneCancellation.Token);

            AnalysisText.Text =
                "STEP 16 — TEST TARGET BACKEND\n\n" +

                "TARGET\n" +
                "------\n" +
                $"{testImage}\n\n" +

                "RESULT\n" +
                "------\n" +
                "✓ Test image created\n" +
                "✓ Test header written\n" +
                "✓ Block A written\n" +
                "✓ Block B written\n" +
                "✓ Data flushed\n" +
                "✓ Block A verified\n" +
                "✓ Block B verified\n" +
                "✓ Header verified\n\n" +

                "SAFETY\n" +
                "------\n" +
                "✓ No PhysicalDrive handle opened\n" +
                "✓ No partition table modified\n" +
                "✓ No SSD modified\n" +
                "✓ No files outside the test image modified\n\n" +

                $"Test image:\n{testImage}\n\n" +

                result.Message;

            StatusText.Text =
                "Test target backend passed — SAFE.";

            StatusText.Foreground =
                Brushes.LightGreen;

            EngineSelfTestButton.IsEnabled =
                true;
        }
        catch (OperationCanceledException)
        {
            CloneProgressBar.Value = 0;

            StatusText.Text =
                "Test target cancelled.";

            StatusText.Foreground =
                Brushes.Orange;

            EngineSelfTestButton.IsEnabled =
                true;
        }
        catch (Exception ex)
        {
            StatusText.Text =
                "Test target backend failed.";

            StatusText.Foreground =
                Brushes.Red;

            EngineSelfTestButton.IsEnabled =
                true;

            MessageBox.Show(
                ex.Message,
                "Test Target Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _cloneCancellation?.Dispose();
            _cloneCancellation = null;
        }
    }


    // ============================================================
    // MODELS
    // ============================================================

    private class DiskInfo
    {
        public int Number
        {
            get;
            set;
        }

        public string FriendlyName
        {
            get;
            set;
        } = "";

        public long Size
        {
            get;
            set;
        }

        public string DisplayName =>
            $"Disk {Number} - {FriendlyName} " +
            $"({MainWindow.FormatBytes(Size)})";

        public string PartitionStyle
        {
            get;
            set;
        } = "";

        public string HealthStatus
        {
            get;
            set;
        } = "";

        public string OperationalStatus
        {
            get;
            set;
        } = "";

        public bool IsOffline
        {
            get;
            set;
        }

        public bool IsReadOnly
        {
            get;
            set;
        }

        public bool IsWindowsDisk
        {
            get;
            set;
        }

        public List<PartitionInfo> Partitions
        {
            get;
            set;
        } = new();
    }


    private class PartitionInfo
    {
        public int Number
        {
            get;
            set;
        }

        public string DriveLetter
        {
            get;
            set;
        } = "";

        public long Size
        {
            get;
            set;
        }

        public string Type
        {
            get;
            set;
        } = "";

        public string FileSystem
        {
            get;
            set;
        } = "";

        public string FileSystemLabel
        {
            get;
            set;
        } = "";

        public bool IsBoot
        {
            get;
            set;
        }

        public bool IsSystem
        {
            get;
            set;
        }

        public bool IsHidden
        {
            get;
            set;
        }

        public bool IsWindows
        {
            get;
            set;
        }
    }


    private class VssSnapshotInfo
    {
        public string ShadowId
        {
            get;
            set;
        } = "";

        public string DeviceObject
        {
            get;
            set;
        } = "";

        public string VolumeName
        {
            get;
            set;
        } = "";

        public bool ClientAccessible
        {
            get;
            set;
        }
    }

    public class CloneProgress
    {
        public int Percent { get; set; }
        public string Stage { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class CloneEngineResult
    {
        public bool Success { get; set; }
        public bool WasDryRun { get; set; }
        public string Message { get; set; } = string.Empty;
        public long PlannedBytes { get; set; }
    }
    // ============================================================
    // FINAL SAFE MVP SUITE - STEPS 17 TO 24
    // TEST IMAGE ONLY - NO PHYSICAL DISK ACCESS
    // ============================================================

    private async Task RunFinalMvpSuiteAsync()
    {
        using CancellationTokenSource cts =
            new CancellationTokenSource();

        try
        {
            const long testDiskSize =
                128L * 1024 * 1024;

            const int blockSize =
                1024 * 1024;

            string root =
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "WinClone-FinalTest");

            System.IO.Directory.CreateDirectory(root);

            string sourceImage =
                System.IO.Path.Combine(
                    root,
                    "source-test.img");

            string destinationImage =
                System.IO.Path.Combine(
                    root,
                    "destination-test.img");

            string manifestFile =
                System.IO.Path.Combine(
                    root,
                    "partition-manifest.txt");

            string recoveryFile =
                System.IO.Path.Combine(
                    root,
                    "recovery.log");

            var report =
                new System.Text.StringBuilder();

            report.AppendLine(
                "WINCLONE FINAL MVP TEST SUITE");

            report.AppendLine(
                "============================");

            report.AppendLine();

            report.AppendLine(
                "MODE: SAFE TEST IMAGE");

            report.AppendLine(
                "PHYSICAL DISK WRITE: DISABLED");

            report.AppendLine();

            // ====================================================
            // STEP 17 - TEST DISK CREATION
            // ====================================================

            StatusText.Text =
                "STEP 17: Creating test images...";

            StatusText.Foreground =
                Brushes.Orange;

            CloneProgressBar.Value = 5;

            await CreateTestImageLocalAsync(
                sourceImage,
                testDiskSize,
                cts.Token);

            await CreateTestImageLocalAsync(
                destinationImage,
                testDiskSize,
                cts.Token);

            report.AppendLine(
                "17. TEST DISK CREATION");

            report.AppendLine(
                "✓ Source test image created");

            report.AppendLine(
                "✓ Destination test image created");

            report.AppendLine();

            // ====================================================
            // STEP 18 - PARTITION PLAN
            // ====================================================

            StatusText.Text =
                "STEP 18: Building partition plan...";

            CloneProgressBar.Value = 15;

            const long alignment =
                1024 * 1024;

            const long efiSize =
                20 * 1024 * 1024;

            const long msrSize =
                16 * 1024 * 1024;

            const long recoverySize =
                20 * 1024 * 1024;

            long efiOffset =
                alignment;

            long msrOffset =
                efiOffset + efiSize;

            long windowsOffset =
                msrOffset + msrSize;

            long windowsSize =
                testDiskSize -
                windowsOffset -
                recoverySize -
                alignment;

            long recoveryOffset =
                windowsOffset +
                windowsSize;

            string manifest =
                "WINCLONE TEST PARTITION MANIFEST\n" +
                "================================\n\n" +

                $"Partition 1 | EFI | " +
                $"Offset={efiOffset} | Size={efiSize}\n" +

                $"Partition 2 | MSR | " +
                $"Offset={msrOffset} | Size={msrSize}\n" +

                $"Partition 3 | Windows | " +
                $"Offset={windowsOffset} | Size={windowsSize}\n" +

                $"Partition 4 | Recovery | " +
                $"Offset={recoveryOffset} | Size={recoverySize}\n";

            await System.IO.File.WriteAllTextAsync(
                manifestFile,
                manifest,
                cts.Token);

            report.AppendLine(
                "18. PARTITION PLANNING");

            report.AppendLine(
                "✓ EFI partition planned");

            report.AppendLine(
                "✓ MSR partition planned");

            report.AppendLine(
                "✓ Windows partition planned");

            report.AppendLine(
                "✓ Recovery partition planned");

            report.AppendLine();

            // ====================================================
            // STEP 19 - PARTITION METADATA
            // ====================================================

            StatusText.Text =
                "STEP 19: Writing test partition metadata...";

            CloneProgressBar.Value = 25;

            report.AppendLine(
                "19. PARTITION METADATA");

            report.AppendLine(
                $"✓ Manifest written to {manifestFile}");

            report.AppendLine(
                "✓ No real partition table modified");

            report.AppendLine();

            // ====================================================
            // STEP 20 - CREATE SOURCE PAYLOAD
            // ====================================================

            StatusText.Text =
                "STEP 20: Creating deterministic source payload...";

            CloneProgressBar.Value = 35;

            byte[] buffer =
                new byte[blockSize];

            using System.Security.Cryptography.SHA256 sourceSha =
                System.Security.Cryptography.SHA256.Create();

            await using (
                System.IO.FileStream sourceStream =
                    new System.IO.FileStream(
                        sourceImage,
                        System.IO.FileMode.Open,
                        System.IO.FileAccess.ReadWrite,
                        System.IO.FileShare.Read))
            {
                sourceStream.Position =
                    windowsOffset;

                long remaining =
                    windowsSize;

                int sequence =
                    0;

                while (remaining > 0)
                {
                    cts.Token.ThrowIfCancellationRequested();

                    int count =
                        (int)Math.Min(
                            blockSize,
                            remaining);

                    for (int i = 0; i < count; i++)
                    {
                        buffer[i] =
                            (byte)(
                                (i +
                                 sequence * 17) &
                                0xFF);
                    }

                    await sourceStream.WriteAsync(
                        buffer.AsMemory(0, count),
                        cts.Token);

                    sourceSha.TransformBlock(
                        buffer,
                        0,
                        count,
                        null,
                        0);

                    remaining -= count;
                    sequence++;

                    int progress =
                        (int)(
                            35 +
                            15 *
                            (1.0 -
                             (double)remaining /
                             windowsSize));

                    CloneProgressBar.Value =
                        progress;
                }

                sourceSha.TransformFinalBlock(
                    Array.Empty<byte>(),
                    0,
                    0);
            }

            string sourceHash =
                Convert.ToHexString(
                    sourceSha.Hash!);

            report.AppendLine(
                "20. SOURCE PAYLOAD");

            report.AppendLine(
                $"✓ Payload size: {windowsSize} bytes");

            report.AppendLine(
                $"✓ Source SHA-256: {sourceHash}");

            report.AppendLine();

            // ====================================================
            // STEP 21 - BLOCK COPY
            // ====================================================

            StatusText.Text =
                "STEP 21: Copying test blocks...";

            CloneProgressBar.Value = 55;

            byte[] copyBuffer =
                new byte[blockSize];

            await using (
                System.IO.FileStream input =
                    new System.IO.FileStream(
                        sourceImage,
                        System.IO.FileMode.Open,
                        System.IO.FileAccess.Read,
                        System.IO.FileShare.Read))
            await using (
                System.IO.FileStream output =
                    new System.IO.FileStream(
                        destinationImage,
                        System.IO.FileMode.Open,
                        System.IO.FileAccess.Write,
                        System.IO.FileShare.None))
            {
                input.Position =
                    windowsOffset;

                output.Position =
                    windowsOffset;

                long remaining =
                    windowsSize;

                while (remaining > 0)
                {
                    cts.Token.ThrowIfCancellationRequested();

                    int count =
                        (int)Math.Min(
                            blockSize,
                            remaining);

                    int totalRead =
                        0;

                    while (totalRead < count)
                    {
                        int read =
                            await input.ReadAsync(
                                copyBuffer.AsMemory(
                                    totalRead,
                                    count - totalRead),
                                cts.Token);

                        if (read == 0)
                        {
                            throw new InvalidOperationException(
                                "Unexpected end of test source.");
                        }

                        totalRead += read;
                    }

                    await output.WriteAsync(
                        copyBuffer.AsMemory(
                            0,
                            count),
                        cts.Token);

                    remaining -= count;

                    int progress =
                        (int)(
                            55 +
                            15 *
                            (1.0 -
                             (double)remaining /
                             windowsSize));

                    CloneProgressBar.Value =
                        progress;
                }

                await output.FlushAsync(
                    cts.Token);
            }

            report.AppendLine(
                "21. BLOCK COPY");

            report.AppendLine(
                "✓ Source blocks read");

            report.AppendLine(
                "✓ Destination blocks written");

            report.AppendLine(
                "✓ Destination flushed");

            report.AppendLine();

            // ====================================================
            // STEP 22 - SHA256 VERIFICATION
            // ====================================================

            StatusText.Text =
                "STEP 22: Verifying copied data...";

            CloneProgressBar.Value = 75;

            using System.Security.Cryptography.SHA256 destinationSha =
                System.Security.Cryptography.SHA256.Create();

            await using (
                System.IO.FileStream verifyStream =
                    new System.IO.FileStream(
                        destinationImage,
                        System.IO.FileMode.Open,
                        System.IO.FileAccess.Read,
                        System.IO.FileShare.Read))
            {
                verifyStream.Position =
                    windowsOffset;

                long remaining =
                    windowsSize;

                byte[] verifyBuffer =
                    new byte[blockSize];

                while (remaining > 0)
                {
                    cts.Token.ThrowIfCancellationRequested();

                    int count =
                        (int)Math.Min(
                            blockSize,
                            remaining);

                    int read =
                        await verifyStream.ReadAsync(
                            verifyBuffer.AsMemory(
                                0,
                                count),
                            cts.Token);

                    if (read != count)
                    {
                        throw new InvalidOperationException(
                            "Destination verification read failed.");
                    }

                    destinationSha.TransformBlock(
                        verifyBuffer,
                        0,
                        read,
                        null,
                        0);

                    remaining -= read;
                }

                destinationSha.TransformFinalBlock(
                    Array.Empty<byte>(),
                    0,
                    0);
            }

            string destinationHash =
                Convert.ToHexString(
                    destinationSha.Hash!);

            if (!string.Equals(
                    sourceHash,
                    destinationHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "SHA-256 verification failed.\n\n" +
                    $"Source: {sourceHash}\n" +
                    $"Destination: {destinationHash}");
            }

            report.AppendLine(
                "22. POST-COPY VERIFICATION");

            report.AppendLine(
                $"✓ Source SHA-256: {sourceHash}");

            report.AppendLine(
                $"✓ Destination SHA-256: {destinationHash}");

            report.AppendLine(
                "✓ Hashes match");

            report.AppendLine();

            // ====================================================
            // STEP 23 - EFI / BCD PLAN
            // ====================================================

            StatusText.Text =
                "STEP 23: Building boot deployment plan...";

            CloneProgressBar.Value = 90;

            string bootPlan =
                "EFI / BCD DEPLOYMENT PLAN\n" +
                "=========================\n\n" +
                "WOULD CREATE EFI SYSTEM FILES\n" +
                "WOULD PREPARE WINDOWS BOOT MANAGER\n" +
                "WOULD CREATE / UPDATE BCD\n" +
                "WOULD VERIFY WINDOWS BOOT PATHS\n\n" +
                "REAL EFI/BCD MODIFICATION: DISABLED";

            report.AppendLine(
                "23. EFI / BCD PLAN");

            report.AppendLine(
                bootPlan);

            report.AppendLine();

            // ====================================================
            // STEP 24 - RECOVERY
            // ====================================================

            StatusText.Text =
                "STEP 24: Running final recovery checks...";

            CloneProgressBar.Value = 95;

            string recovery =
                "WINCLONE RECOVERY PLAN\n" +
                "======================\n\n" +
                $"SOURCE={sourceImage}\n" +
                $"DESTINATION={destinationImage}\n\n" +
                "On verification failure:\n" +
                "1. Stop destination writes.\n" +
                "2. Preserve source.\n" +
                "3. Remove incomplete test destination.\n" +
                "4. Re-create destination.\n" +
                "5. Re-run verification.\n\n" +
                "PHYSICAL DISK RECOVERY: DISABLED";

            await System.IO.File.WriteAllTextAsync(
                recoveryFile,
                recovery,
                cts.Token);

            report.AppendLine(
                "24. RECOVERY");

            report.AppendLine(
                $"✓ Recovery log created: {recoveryFile}");

            report.AppendLine();

            // ====================================================
            // FINAL
            // ====================================================

            CloneProgressBar.Value = 100;

            report.AppendLine(
                "FINAL RESULT");

            report.AppendLine(
                "============");

            report.AppendLine(
                "✓ Test image creation");

            report.AppendLine(
                "✓ Partition planning");

            report.AppendLine(
                "✓ Partition metadata");

            report.AppendLine(
                "✓ Source payload generation");

            report.AppendLine(
                "✓ Block copy");

            report.AppendLine(
                "✓ SHA-256 verification");

            report.AppendLine(
                "✓ EFI/BCD planning");

            report.AppendLine(
                "✓ Recovery planning");

            report.AppendLine();

            report.AppendLine(
                "SAFETY");

            report.AppendLine(
                "------");

            report.AppendLine(
                "✓ No PhysicalDrive handle opened");

            report.AppendLine(
                "✓ No real partition table modified");

            report.AppendLine(
                "✓ No SSD sector written");

            report.AppendLine(
                "✓ No real Windows files modified");

            report.AppendLine(
                "✓ No data erased");

            report.AppendLine();

            report.AppendLine(
                $"TEST DIRECTORY:\n{root}");

            AnalysisText.Text =
                report.ToString();

            StatusText.Text =
                "Steps 17-24 completed — TEST IMAGE ONLY.";

            StatusText.Foreground =
                Brushes.LightGreen;
        }
        catch (OperationCanceledException)
        {
            CloneProgressBar.Value = 0;

            StatusText.Text =
                "Final MVP test cancelled.";

            StatusText.Foreground =
                Brushes.Orange;
        }
        catch (Exception ex)
        {
            StatusText.Text =
                "Final MVP test failed.";

            StatusText.Foreground =
                Brushes.Red;

            MessageBox.Show(
                ex.Message,
                "WinClone Final MVP Test",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        // --------------------------------------------------------
        // Local helper
        // --------------------------------------------------------

        static async Task CreateTestImageLocalAsync(
            string path,
            long size,
            CancellationToken cancellationToken)
        {
            string? directory =
                System.IO.Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                System.IO.Directory.CreateDirectory(
                    directory);
            }

            await using System.IO.FileStream stream =
                new System.IO.FileStream(
                    path,
                    System.IO.FileMode.Create,
                    System.IO.FileAccess.ReadWrite,
                    System.IO.FileShare.None);

            stream.SetLength(size);

            await stream.FlushAsync(
                cancellationToken);
        }
    }
    // ============================================================
    // WINCLONE - REAL WINDOWS CLONE ENGINE
    //
    // REAL DESTINATION WRITE.
    // SOURCE IS NEVER USED AS A WRITE TARGET.
    //
    // Destination:
    //   GPT
    //   EFI       S:
    //   MSR
    //   Windows   W:
    //   Recovery  R:
    //
    // Windows is copied from the verified VSS snapshot.
    // ============================================================

    private async Task ExecutePhysicalCloneAsync()
    {
        string? vssMount = null;
        string? diskPartFile = null;

        try
        {
            // ----------------------------------------------------
            // ADMIN
            // ----------------------------------------------------

            if (!IsRunningAsAdministrator())
            {
                MessageBox.Show(
                    "WinClone must be run as Administrator.",
                    "Administrator Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            // ----------------------------------------------------
            // PLAN
            // ----------------------------------------------------

            if (!_clonePlanLocked)
            {
                throw new InvalidOperationException(
                    "Clone plan is not locked.");
            }

            if (_currentSnapshot == null)
            {
                throw new InvalidOperationException(
                    "No verified VSS snapshot exists.");
            }

            // ----------------------------------------------------
            // SOURCE
            // ----------------------------------------------------

            if (SourceDriveComboBox.SelectedItem
                is not DiskInfo source)
            {
                throw new InvalidOperationException(
                    "Source disk is not selected.");
            }

            // ----------------------------------------------------
            // DESTINATION
            // ----------------------------------------------------

            if (DestinationDriveComboBox.SelectedItem
                is not DiskInfo destination)
            {
                throw new InvalidOperationException(
                    "Destination disk is not selected.");
            }

            // ----------------------------------------------------
            // HARD SAFETY CHECKS
            // ----------------------------------------------------

            if (source.Number == destination.Number)
            {
                throw new InvalidOperationException(
                    "SAFETY STOP:\n\n" +
                    "Source and destination are the same disk.");
            }

            if (!source.IsWindowsDisk)
            {
                throw new InvalidOperationException(
                    "SAFETY STOP:\n\n" +
                    "Source is not the Windows disk.");
            }

            if (destination.IsWindowsDisk)
            {
                throw new InvalidOperationException(
                    "SAFETY STOP:\n\n" +
                    "Destination is marked as a Windows disk.");
            }

            if (destination.Size < source.Size)
            {
                throw new InvalidOperationException(
                    "SAFETY STOP:\n\n" +
                    "Destination is smaller than source.");
            }

            if (string.IsNullOrWhiteSpace(
                    _sourceFingerprint))
            {
                throw new InvalidOperationException(
                    "Source fingerprint is missing.");
            }

            if (string.IsNullOrWhiteSpace(
                    _destinationFingerprint))
            {
                throw new InvalidOperationException(
                    "Destination fingerprint is missing.");
            }

            // ----------------------------------------------------
            // DRIVE LETTER SAFETY
            // ----------------------------------------------------

            if (IsDriveLetterInUse('S'))
            {
                throw new InvalidOperationException(
                    "Drive S: is already in use.");
            }

            if (IsDriveLetterInUse('W'))
            {
                throw new InvalidOperationException(
                    "Drive W: is already in use.");
            }

            if (IsDriveLetterInUse('R'))
            {
                throw new InvalidOperationException(
                    "Drive R: is already in use.");
            }

            // ----------------------------------------------------
            // FINAL IDENTITY CHECK
            // ----------------------------------------------------

            List<DiskInfo> disks =
                await GetDisksAsync();

            DiskInfo? currentSource =
                disks.FirstOrDefault(
                    d => d.Number == source.Number);

            DiskInfo? currentDestination =
                disks.FirstOrDefault(
                    d => d.Number == destination.Number);

            if (currentSource == null ||
                currentDestination == null)
            {
                throw new InvalidOperationException(
                    "A selected disk is no longer available.");
            }

            string sourceFingerprint =
                BuildDiskFingerprint(currentSource);

            string destinationFingerprint =
                BuildDiskFingerprint(currentDestination);

            if (!string.Equals(
                    sourceFingerprint,
                    _sourceFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "SAFETY STOP:\n\n" +
                    "Source disk identity changed.");
            }

            if (!string.Equals(
                    destinationFingerprint,
                    _destinationFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "SAFETY STOP:\n\n" +
                    "Destination disk identity changed.");
            }

            // ----------------------------------------------------
            // FINAL HUMAN CONFIRMATION
            // ----------------------------------------------------

            MessageBoxResult answer =
                MessageBox.Show(
                    "REAL CLONE OPERATION\n\n" +

                    "SOURCE — NOT MODIFIED:\n" +
                    $"Disk {source.Number} - " +
                    $"{source.FriendlyName}\n\n" +

                    "DESTINATION — WILL BE ERASED:\n" +
                    $"Disk {destination.Number} - " +
                    $"{destination.FriendlyName}\n\n" +

                    "ALL DATA ON THE DESTINATION DISK " +
                    "WILL BE DESTROYED.\n\n" +

                    "Continue?",
                    "FINAL DESTRUCTIVE WARNING",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
            {
                StatusText.Text =
                    "Real clone cancelled.";

                StatusText.Foreground =
                    Brushes.Orange;

                return;
            }

            // ----------------------------------------------------
            // LOCK UI
            // ----------------------------------------------------

            EngineSelfTestButton.IsEnabled = false;
            ValidateWriteGuardButton.IsEnabled = false;

            CloneProgressBar.Value = 0;

            // ====================================================
            // STEP 1 - DESTINATION
            // ====================================================

            StatusText.Text =
                "STEP 1/6 — Preparing destination disk...";

            StatusText.Foreground =
                Brushes.Red;

            CloneProgressBar.Value = 5;

            string diskScript =
                BuildDiskPartScript(
                    destination.Number);

            diskPartFile =
                Path.Combine(
                    Path.GetTempPath(),
                    $"WinClone-{Guid.NewGuid():N}.txt");

            await File.WriteAllTextAsync(
                diskPartFile,
                diskScript);

            ProcessResult diskResult =
                await RunProcessAsync(
                    "diskpart.exe",
                    "/s",
                    diskPartFile);

            if (diskResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "DiskPart failed.\n\n" +
                    diskResult.Output +
                    "\n" +
                    diskResult.Error);
            }

            // ====================================================
            // STEP 2 - VERIFY TARGET PARTITIONS
            // ====================================================

            StatusText.Text =
                "STEP 2/6 — Verifying target partitions...";

            CloneProgressBar.Value = 15;

            await VerifyDriveBelongsToDiskAsync(
                'S',
                destination.Number);

            await VerifyDriveBelongsToDiskAsync(
                'W',
                destination.Number);

            await VerifyDriveBelongsToDiskAsync(
                'R',
                destination.Number);

            // ====================================================
            // STEP 3 - VSS
            // ====================================================

            StatusText.Text =
                "STEP 3/6 — Mounting VSS snapshot...";

            CloneProgressBar.Value = 20;

            vssMount =
                Path.Combine(
                    Path.GetTempPath(),
                    $"WinClone-VSS-{Guid.NewGuid():N}");

            Directory.CreateDirectory(vssMount);

            string shadowDevice =
                _currentSnapshot.DeviceObject.TrimEnd('\\') +
                "\\";

            string mklinkCommand =
                $"mklink /D " +
                $"\"{vssMount}\" " +
                $"\"{shadowDevice}\"";

            ProcessResult linkResult =
                await RunProcessAsync(
                    "cmd.exe",
                    "/c",
                    mklinkCommand);

            if (linkResult.ExitCode != 0 ||
                !Directory.Exists(vssMount))
            {
                throw new InvalidOperationException(
                    "Could not mount VSS snapshot.\n\n" +
                    linkResult.Output +
                    "\n" +
                    linkResult.Error);
            }

            string snapshotWindows =
                Path.Combine(
                    vssMount,
                    "Windows");

            if (!Directory.Exists(
                    snapshotWindows))
            {
                throw new InvalidOperationException(
                    "VSS snapshot does not contain Windows.");
            }

            // ====================================================
            // STEP 4 - WINDOWS COPY
            // ====================================================

            StatusText.Text =
                "STEP 4/6 — Copying Windows...";

            StatusText.Foreground =
                Brushes.Orange;

            CloneProgressBar.Value = 30;

            string robocopyLog =
                Path.Combine(
                    Path.GetTempPath(),
                    $"WinClone-Copy-{DateTime.Now:yyyyMMdd-HHmmss}.log");

            ProcessResult roboResult =
                await RunProcessAsync(
                    "robocopy.exe",

                    vssMount,
                    @"W:\",

                    "/MIR",
                    "/COPYALL",
                    "/DCOPY:DAT",

                    "/XJ",
                    "/B",

                    "/R:1",
                    "/W:1",

                    "/MT:8",

                    "/XD",
                    "System Volume Information",
                    "$Recycle.Bin",

                    "/XF",
                    "pagefile.sys",
                    "hiberfil.sys",
                    "swapfile.sys",

                    "/NP",

                    "/LOG+:" + robocopyLog);

            // Robocopy 0-7 = success/informational.
            // 8+ = failure.
            if (roboResult.ExitCode >= 8)
            {
                throw new InvalidOperationException(
                    "Robocopy failed.\n\n" +
                    $"Exit code: {roboResult.ExitCode}\n\n" +
                    $"Log:\n{robocopyLog}\n\n" +
                    roboResult.Error);
            }

            CloneProgressBar.Value = 70;

            // ====================================================
            // WINRE
            // ====================================================

            StatusText.Text =
                "Copying Windows Recovery Environment...";

            string sourceWinRe =
                Path.Combine(
                    vssMount,
                    "Windows",
                    "System32",
                    "Recovery",
                    "Winre.wim");

            string destinationRecovery =
                @"R:\Recovery\WindowsRE";

            Directory.CreateDirectory(
                destinationRecovery);

            string destinationWinRe =
                Path.Combine(
                    destinationRecovery,
                    "Winre.wim");

            if (File.Exists(sourceWinRe))
            {
                File.Copy(
                    sourceWinRe,
                    destinationWinRe,
                    true);
            }
            else
            {
                string copiedWinRe =
                    Path.Combine(
                        @"W:\Windows",
                        "System32",
                        "Recovery",
                        "Winre.wim");

                if (!File.Exists(copiedWinRe))
                {
                    throw new InvalidOperationException(
                        "Winre.wim was not found.");
                }

                File.Copy(
                    copiedWinRe,
                    destinationWinRe,
                    true);
            }

            // ====================================================
            // UEFI BOOT
            // ====================================================

            StatusText.Text =
                "STEP 5/6 — Creating UEFI boot files...";

            CloneProgressBar.Value = 75;

            ProcessResult bcdbootResult =
                await RunProcessAsync(
                    "bcdboot.exe",
                    @"W:\Windows",
                    "/s",
                    "S:",
                    "/f",
                    "UEFI");

            if (bcdbootResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "BCDBoot failed.\n\n" +
                    bcdbootResult.Output +
                    "\n" +
                    bcdbootResult.Error);
            }

            string bootManager =
                @"S:\EFI\Microsoft\Boot\bootmgfw.efi";

            if (!File.Exists(
                    bootManager))
            {
                throw new InvalidOperationException(
                    "Windows UEFI boot manager was not created.");
            }

            // ====================================================
            // WINRE REGISTRATION
            // ====================================================

            ProcessResult reagentResult =
                await RunProcessAsync(
                    @"C:\Windows\System32\ReAgentC.exe",
                    "/setreimage",
                    "/path",
                    @"R:\Recovery\WindowsRE",
                    "/target",
                    @"W:\Windows");

            if (reagentResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "REAgentC failed.\n\n" +
                    reagentResult.Output +
                    "\n" +
                    reagentResult.Error);
            }

            ProcessResult reagentInfo =
                await RunProcessAsync(
                    @"C:\Windows\System32\ReAgentC.exe",
                    "/info",
                    "/target",
                    @"W:\Windows");

            if (reagentInfo.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "WinRE verification failed.\n\n" +
                    reagentInfo.Output +
                    "\n" +
                    reagentInfo.Error);
            }

            // ====================================================
            // STEP 6 - FINAL VERIFICATION
            // ====================================================

            StatusText.Text =
                "STEP 6/6 — Final verification...";

            CloneProgressBar.Value = 90;

            if (!Directory.Exists(
                    @"W:\Windows"))
            {
                throw new InvalidOperationException(
                    "W:\\Windows is missing.");
            }

            if (!Directory.Exists(
                    @"W:\Windows\System32"))
            {
                throw new InvalidOperationException(
                    "W:\\Windows\\System32 is missing.");
            }

            if (!File.Exists(
                    bootManager))
            {
                throw new InvalidOperationException(
                    "EFI boot manager verification failed.");
            }

            if (!File.Exists(
                    destinationWinRe))
            {
                throw new InvalidOperationException(
                    "WinRE verification failed.");
            }

            CloneProgressBar.Value = 95;

            // ====================================================
            // REMOVE TEMP LETTERS
            // ====================================================

            await RemoveTemporaryDriveLettersAsync(
                destination.Number);

            // ====================================================
            // REMOVE VSS LINK
            // ====================================================

            if (!string.IsNullOrWhiteSpace(
                    vssMount))
            {
                try
                {
                    await RunProcessAsync(
                        "cmd.exe",
                        "/c",
                        $"rmdir \"{vssMount}\"");
                }
                catch
                {
                    // Best effort.
                }

                vssMount = null;
            }

            // ====================================================
            // DELETE VSS SNAPSHOT
            // ====================================================

            await DeleteCurrentVssSnapshotAsync();

            // ====================================================
            // SUCCESS
            // ====================================================

            CloneProgressBar.Value = 100;

            AnalysisText.Text =
                "WINCLONE — REAL CLONE COMPLETE\n" +
                "================================\n\n" +

                $"SOURCE:\n" +
                $"Disk {source.Number} - " +
                $"{source.FriendlyName}\n\n" +

                $"DESTINATION:\n" +
                $"Disk {destination.Number} - " +
                $"{destination.FriendlyName}\n\n" +

                "COMPLETED\n" +
                "✓ GPT layout created\n" +
                "✓ EFI partition created\n" +
                "✓ MSR created\n" +
                "✓ Windows partition created\n" +
                "✓ Recovery partition created\n" +
                "✓ Windows copied from VSS\n" +
                "✓ UEFI boot files created\n" +
                "✓ BCD created\n" +
                "✓ WinRE configured\n" +
                "✓ Final verification passed\n\n" +

                "The source disk was not written.\n" +
                "The destination disk was modified.\n\n" +

                "REBOOT REQUIRED.";

            StatusText.Text =
                "REAL CLONE COMPLETE — REBOOT REQUIRED.";

            StatusText.Foreground =
                Brushes.LightGreen;

            MessageBox.Show(
                "REAL CLONE COMPLETED.\n\n" +
                "Restart the computer and select the cloned SSD " +
                "from the UEFI boot menu.",
                "WinClone",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(
                    vssMount))
            {
                try
                {
                    await RunProcessAsync(
                        "cmd.exe",
                        "/c",
                        $"rmdir \"{vssMount}\"");
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(
                    diskPartFile))
            {
                TryDeleteFile(
                    diskPartFile);
            }

            StatusText.Text =
                "REAL CLONE FAILED.";

            StatusText.Foreground =
                Brushes.Red;

            CloneProgressBar.Value = 0;

            EngineSelfTestButton.IsEnabled = true;
            ValidateWriteGuardButton.IsEnabled = true;

            MessageBox.Show(
                ex.Message,
                "WinClone — Clone Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }


    // ============================================================
    // DISKPART
    // ============================================================

    private static string BuildDiskPartScript(
        int diskNumber)
    {
        return
            $"select disk {diskNumber}" +
            Environment.NewLine +

            "attributes disk clear readonly" +
            Environment.NewLine +

            "online disk noerr" +
            Environment.NewLine +

            "clean" +
            Environment.NewLine +

            "convert gpt" +
            Environment.NewLine +

            "create partition efi size=260" +
            Environment.NewLine +

            "format quick fs=fat32 label=\"System\"" +
            Environment.NewLine +

            "assign letter=S" +
            Environment.NewLine +

            "create partition msr size=16" +
            Environment.NewLine +

            "create partition primary" +
            Environment.NewLine +

            "shrink minimum=1024" +
            Environment.NewLine +

            "format quick fs=ntfs label=\"Windows\"" +
            Environment.NewLine +

            "assign letter=W" +
            Environment.NewLine +

            "create partition primary" +
            Environment.NewLine +

            "format quick fs=ntfs label=\"Recovery\"" +
            Environment.NewLine +

            "assign letter=R" +
            Environment.NewLine +

            "set id=\"de94bba4-06d1-4d40-a16a-bfd50179d6ac\"" +
            Environment.NewLine +

            "gpt attributes=0x8000000000000001" +
            Environment.NewLine +

            "exit" +
            Environment.NewLine;
    }


    // ============================================================
    // VERIFY DRIVE -> DISK
    // ============================================================

    private static async Task
        VerifyDriveBelongsToDiskAsync(
            char driveLetter,
            int expectedDiskNumber)
    {
        ProcessResult result =
            await RunProcessAsync(
                "powershell.exe",
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                $"(Get-Partition -DriveLetter " +
                $"{char.ToUpperInvariant(driveLetter)}).DiskNumber");

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not verify {driveLetter}:.");
        }

        if (!int.TryParse(
                result.Output.Trim(),
                out int actualDisk))
        {
            throw new InvalidOperationException(
                $"Could not determine disk for {driveLetter}:.");
        }

        if (actualDisk != expectedDiskNumber)
        {
            throw new InvalidOperationException(
                "SAFETY STOP:\n\n" +
                $"{driveLetter}: belongs to Disk {actualDisk}, " +
                $"not Disk {expectedDiskNumber}.");
        }
    }


    // ============================================================
    // DRIVE LETTER CHECK
    // ============================================================

    private static bool IsDriveLetterInUse(
        char driveLetter)
    {
        string root =
            $"{char.ToUpperInvariant(driveLetter)}:\\";

        return DriveInfo
            .GetDrives()
            .Any(
                d => string.Equals(
                    d.Name,
                    root,
                    StringComparison.OrdinalIgnoreCase));
    }


    // ============================================================
    // REMOVE TEMPORARY LETTERS
    // ============================================================

    private static async Task
        RemoveTemporaryDriveLettersAsync(
            int diskNumber)
    {
        string script =
            $"select disk {diskNumber}" +
            Environment.NewLine +

            "select volume S" +
            Environment.NewLine +
            "remove letter=S noerr" +
            Environment.NewLine +

            "select volume W" +
            Environment.NewLine +
            "remove letter=W noerr" +
            Environment.NewLine +

            "select volume R" +
            Environment.NewLine +
            "remove letter=R noerr" +
            Environment.NewLine +

            "exit" +
            Environment.NewLine;

        string path =
            Path.Combine(
                Path.GetTempPath(),
                $"WinClone-Cleanup-{Guid.NewGuid():N}.txt");

        await File.WriteAllTextAsync(
            path,
            script);

        try
        {
            await RunProcessAsync(
                "diskpart.exe",
                "/s",
                path);
        }
        finally
        {
            TryDeleteFile(path);
        }
    }


    // ============================================================
    // DELETE VSS SNAPSHOT
    // ============================================================

    private async Task DeleteCurrentVssSnapshotAsync()
    {
        if (_currentSnapshot == null)
        {
            return;
        }

        string shadowId =
            _currentSnapshot.ShadowId;

        if (string.IsNullOrWhiteSpace(
                shadowId))
        {
            return;
        }

        string escaped =
            shadowId.Replace(
                "'",
                "''");

        await RunProcessAsync(
            "powershell.exe",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Get-CimInstance " +
            $"-ClassName Win32_ShadowCopy " +
            $"-Filter \"ID='{escaped}'\" " +
            "| Remove-CimInstance");

        _currentSnapshot = null;
    }


    // ============================================================
    // PROCESS RESULT
    // ============================================================

    private sealed class ProcessResult
    {
        public int ExitCode { get; init; }

        public string Output { get; init; } = "";

        public string Error { get; init; } = "";
    }


    // ============================================================
    // PROCESS RUNNER
    // ============================================================

    private static async Task<ProcessResult>
        RunProcessAsync(
            string fileName,
            params string[] arguments)
    {
        using Process process =
            new Process();

        ProcessStartInfo info =
            new ProcessStartInfo
            {
                FileName = fileName,

                UseShellExecute = false,

                RedirectStandardOutput = true,

                RedirectStandardError = true,

                CreateNoWindow = true
            };

        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(
                argument);
        }

        process.StartInfo =
            info;

        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Could not start {fileName}.");
        }

        Task<string> outputTask =
            process.StandardOutput.ReadToEndAsync();

        Task<string> errorTask =
            process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return new ProcessResult
        {
            ExitCode =
                process.ExitCode,

            Output =
                await outputTask,

            Error =
                await errorTask
        };
    }


    // ============================================================
    // DELETE TEMP FILE
    // ============================================================

    private static void TryDeleteFile(
        string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}