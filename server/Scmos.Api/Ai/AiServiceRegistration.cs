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
        services.AddScoped<IOperationsSource, OperationsSource>();
        services.AddScoped<OperationsReadService>();
        services.AddScoped<ToolRegistry>();
        services.AddScoped<OperationsAgent>();
        services.AddScoped<SqlAiExecutionAudit>();
        services.AddScoped<IAiExecutionAudit>(sp => sp.GetRequiredService<SqlAiExecutionAudit>());
        services.AddScoped<AiAuditReader>();
        services.AddSingleton<AiRunLimiter>();
        services.AddSingleton<OpenAiProvider>();
        services.AddSingleton<MockAiProvider>();
        services.AddSingleton<IAiProvider>(sp => sp.GetRequiredService<IOptions<AiOptions>>().Value.MockMode
            ? sp.GetRequiredService<MockAiProvider>() : sp.GetRequiredService<OpenAiProvider>());
        services.AddScoped<AgentOrchestrator>();
        return services;
    }
}
