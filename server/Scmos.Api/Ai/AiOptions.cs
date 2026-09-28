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
    // Phase 6 first read: fixed public GitHub metadata, Administrator only.
    public bool EngineeringAgentEnabled { get; set; }
    // Phase 6 second increment: the repository's own source, read-only and bounded, for the same agent. Off unless set.
    public bool EngineeringSourceEnabled { get; set; }
    // Phase 7: the SRE Agent — the platform's own health, deployments and failure counts; restarts nothing. Off unless set.
    public bool SreAgentEnabled { get; set; }
    public bool IncidentAgentEnabled { get; set; }
    // Phase 5: the Document & Invoice Agent (the registry's former billing-agent, never connected under that name).
    public bool DocumentAgentEnabled { get; set; }
    // Phase 10: document extraction/classification and billing explanations.
    // Both are separately default-off; the global Enabled switch still wins.
    public bool DocumentAiEnabled { get; set; }
    public bool BillingAiEnabled { get; set; }
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
    // Agent Platform foundation (27 Sep 2026): the circuit breaker, read from the audit's recent runs —
    // this many platform failures in a row show DEGRADED, this many rest the agent for the cool-down.
    // The rule-first agents (Agent Platform, 28 Sep 2026): the OTD Agent and the Validation Agent,
    // each off unless its own flag is set, scanned every AgentScanMinutes (0 = never).
    public bool OtdAgentEnabled { get; set; }
    public bool ValidationAgentEnabled { get; set; }
    public int AgentScanMinutes { get; set; } = 15;
    // The OTD Agent's near-plan windows: not yet running this close to plan is WATCH; no truck this close is HIGH.
    public int OtdWatchMinutes { get; set; } = 120;
    public int OtdHighMinutes { get; set; } = 60;
    public int BreakerDegradedAfter { get; set; } = 3;
    public int BreakerPauseAfter { get; set; } = 5;
    public int BreakerCoolDownMinutes { get; set; } = 10;
    // Prices per million tokens, "model=input/output; …" — empty means no cost is shown (configuration required).
    public string PriceList { get; set; } = "";
    public string PriceCurrency { get; set; } = "USD";

    public bool Valid => TimeoutSeconds is >= 1 and <= 60 && MaxOutputTokens is >= 64 and <= 2000
        && ContextMinutes is >= 1 and <= 60
        && BreakerDegradedAfter is >= 1 and <= 20 && BreakerPauseAfter >= BreakerDegradedAfter && BreakerPauseAfter <= 50
        && BreakerCoolDownMinutes is >= 1 and <= 240 && PriceCurrency.Length is >= 1 and <= 8
        && AgentScanMinutes is >= 0 and <= 1440 && OtdHighMinutes is >= 1 and <= 720 && OtdWatchMinutes >= OtdHighMinutes && OtdWatchMinutes <= 1440;
}
