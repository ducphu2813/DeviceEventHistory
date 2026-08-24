namespace DeviceEventHistory.Worker.Configuration;

public sealed class WorkerOptions
{
    public const string SectionName = "DeviceEventHistory";

    public bool Enabled { get; set; }

    public string WorkerId { get; set; } = string.Empty;
}
