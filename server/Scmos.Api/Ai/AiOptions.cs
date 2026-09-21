namespace Scmos.Api.Ai;

/// <summary>Optional enhancement. Bad AI settings must not stop the core API.</summary>
public sealed class AiOptions
{
    public const string Section = "AI";
    public bool Enabled { get; set; }
    public bool ChatEnabled { get; set; }
    public bool MockMode { get; set; }
    public bool OperationsAgentEnabled { get; set; }
    // Server-side emergency stop always wins over the durable UI switch.
    public bool OperationsEmergencyDisabled { get; set; }
    public bool VendorAgentEnabled { get; set; }
    public bool RateAgentEnabled { get; set; }
    // Phase 2: the Data Agent (the registry's former kpi-agent, never connected under that name).
    public bool DataAgentEnabled { get; set; }
    // Phase 4: the Communication Agent — reads the LINE and mail ledgers, sends nothing.
    public bool CommunicationAgentEnabled { get; set; }
    public bool IncidentAgentEnabled { get; set; }
    // Phase 5: the Document & Invoice Agent (the registry's former billing-agent, never connected under that name).
    public bool DocumentAgentEnabled { get; set; }
    public bool ComplianceAgentEnabled { get; set; }
    public bool ManagementAgentEnabled { get; set; }
    // Reserved, NOT an authorization to wire writes. Phase B always refuses them.
    public bool WriteToolsEnabled { get; set; }
    // Separate, default-off gate for human-confirmed Operations changes only.
    public bool OperationsWritesEnabled { get; set; }
    public int TimeoutSeconds { get; set; } = 20;
    public int MaxOutputTokens { get; set; } = 800;
    // 1D context pilot: what the last run did for a person, kept in memory a
    // few minutes so a follow-up reads against it. Off by default; no table.
    public bool ContextEnabled { get; set; }
    public int ContextMinutes { get; set; } = 10;

    public bool Valid => TimeoutSeconds is >= 1 and <= 60 && MaxOutputTokens is >= 64 and <= 2000
        && ContextMinutes is >= 1 and <= 60;
}
