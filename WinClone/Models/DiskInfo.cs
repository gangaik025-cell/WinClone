using System.Collections.Generic;

namespace WinClone.Models;

public class DiskInfo
{
    public int Number { get; set; }
    public string FriendlyName { get; set; } = "";
    public long Size { get; set; }
    public string PartitionStyle { get; set; } = "";
    public string HealthStatus { get; set; } = "";
    public string OperationalStatus { get; set; } = "";
    public bool IsOffline { get; set; }
    public bool IsReadOnly { get; set; }

    public List<PartitionInfo> Partitions { get; set; } = new();
}

public class PartitionInfo
{
    public int Number { get; set; }
    public string DriveLetter { get; set; } = "";
    public long Size { get; set; }
    public string Type { get; set; } = "";
    public string FileSystem { get; set; } = "";
    public string FileSystemLabel { get; set; } = "";
    public bool IsBoot { get; set; }
    public bool IsSystem { get; set; }
    public bool IsHidden { get; set; }
}