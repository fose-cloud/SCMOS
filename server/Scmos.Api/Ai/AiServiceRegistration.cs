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
