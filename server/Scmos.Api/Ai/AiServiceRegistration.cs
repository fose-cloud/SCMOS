using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scmos.Api.Ai.Providers;
using Scmos.Api.Ai.Operations;

namespace Scmos.Api.Ai;

public static class AiServiceRegistration
{
    public static IServiceCollection AddAiFoundation(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.Section));
        services.AddSingleton<AgentRegistry>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        // 1D context pilot: per instance, in memory, structured facts only.
        services.AddSingleton<AiContextService>();
        services.AddScoped<IOperationsSource, OperationsSource>();
        services.AddScoped<OperationsReadService>();
        services.AddScoped<OperationsChangeService>();
        services.AddScoped<OperationsAttentionService>();
        services.AddMemoryCache();
        services.TryAddScoped<Scmos.Api.Data.JobRegisterCache>();
        services.AddScoped<ToolRegistry>();
        services.AddScoped<OperationsAgent>();
        services.AddScoped<IAgentExecutor<OperationsExecution>>(sp => sp.GetRequiredService<OperationsAgent>());
        // Phase 2 — the Data Agent reads the KPI through the service the KPI
        // screen uses (IKpiReports, registered beside KpiService in Program);
        // a host without one gets a read service that is not connected.
        services.AddScoped(sp => new Scmos.Api.Ai.Data.DataReadService(
            sp.GetService<Scmos.Api.Services.IKpiReports>(), sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<Scmos.Api.Ai.Data.DataAgent>();
        services.AddScoped<IAgentExecutor<Scmos.Api.Ai.Data.DataExecution>>(sp => sp.GetRequiredService<Scmos.Api.Ai.Data.DataAgent>());
        // Phase 4 — the Communication Agent reads the LINE and mail ledgers through
        // a source the checks can stand in for; a host without one is not connected.
        services.AddScoped(sp => new Scmos.Api.Ai.Communication.MessagesReadService(
            sp.GetService<Scmos.Api.Ai.Communication.ICommunicationSource>(), sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<Scmos.Api.Ai.Communication.CommunicationAgent>();
        services.AddScoped<IAgentExecutor<Scmos.Api.Ai.Communication.CommunicationExecution>>(sp => sp.GetRequiredService<Scmos.Api.Ai.Communication.CommunicationAgent>());
        // Phase 5 — the Document & Invoice Agent reads the register and the documents
        // table through one source; a host without it (the checks) leaves the tool unbound.
        services.AddScoped(sp => new Scmos.Api.Ai.Documents.DocumentsReadService(
            sp.GetService<Scmos.Api.Ai.Documents.IDocumentSource>(), sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<Scmos.Api.Ai.Documents.DocumentAgent>();
        services.AddScoped<IAgentExecutor<Scmos.Api.Ai.Documents.DocumentExecution>>(sp => sp.GetRequiredService<Scmos.Api.Ai.Documents.DocumentAgent>());
        // The Workspace's document reader, under the same limiter and audit (S4).
        services.AddScoped<Scmos.Api.Ai.Documents.ExtractionRun>();
        services.AddScoped<SqlAiExecutionAudit>();
        services.AddScoped<IAiExecutionAudit>(sp => sp.GetRequiredService<SqlAiExecutionAudit>());
        services.AddScoped<AiAuditReader>();
        services.AddSingleton<AiRunLimiter>();
        services.AddSingleton<OpenAiProvider>();
        services.AddSingleton<MockAiProvider>();
        services.AddSingleton<IAiProvider>(sp => sp.GetRequiredService<IOptions<AiOptions>>().Value.MockMode
            ? sp.GetRequiredService<MockAiProvider>() : sp.GetRequiredService<OpenAiProvider>());
        services.AddScoped<AgentOrchestrator>();
        services.AddScoped<OperationsControlService>();
        services.AddHttpContextAccessor();
        services.TryAddScoped<Scmos.Api.Services.AuditService>();
        services.AddScoped<IOperationsControl>(sp => sp.GetRequiredService<OperationsControlService>());
        return services;
    }
}
