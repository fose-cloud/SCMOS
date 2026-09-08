namespace Scmos.Api.Ai.Providers;

public sealed class MockAiProvider(IHostEnvironment environment) : IAiProvider
{
    public bool Configured => environment.IsDevelopment();
    public bool IsMock => true;
    public Task<AiProviderResult> CompleteAsync(AiProviderRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(Configured
            ? new AiProviderResult("ok", "[DEVELOPMENT MOCK] AI foundation is connected. No SCMOS records were read or changed; this mock does not execute business tools.", Mock: true)
            : new AiProviderResult("mock_not_allowed"));
    }
}
