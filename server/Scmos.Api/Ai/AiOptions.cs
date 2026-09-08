namespace Scmos.Api.Ai;

/// <summary>Optional enhancement. Bad AI settings must not stop the core API.</summary>
public sealed class AiOptions
{
    public const string Section = "AI";
    public bool Enabled { get; set; }
    public bool ChatEnabled { get; set; }
    public bool MockMode { get; set; }
    public bool OperationsAgentEnabled { get; set; }
    public bool VendorAgentEnabled { get; set; }
    public bool RateAgentEnabled { get; set; }
    public bool KpiAgentEnabled { get; set; }
    public bool IncidentAgentEnabled { get; set; }
    public bool BillingAgentEnabled { get; set; }
    public bool ComplianceAgentEnabled { get; set; }
    public bool ManagementAgentEnabled { get; set; }
    // Reserved, NOT an authorization to wire writes. Phase B always refuses them.
    public bool WriteToolsEnabled { get; set; }
    public int TimeoutSeconds { get; set; } = 20;
    public int MaxOutputTokens { get; set; } = 800;

    public bool Valid => TimeoutSeconds is >= 1 and <= 60 && MaxOutputTokens is >= 64 and <= 2000;
}
