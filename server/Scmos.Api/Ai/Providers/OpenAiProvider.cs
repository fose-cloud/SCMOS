using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using Scmos.Api.Services;

namespace Scmos.Api.Ai.Providers;

/// <summary>Reuses server-only OpenAI:ApiKey/Model; never accepts browser credentials.</summary>
public sealed class OpenAiProvider(IOptions<OpenAiOptions> providerOptions, IOptions<AiOptions> aiOptions,
    ChatClient? testClient = null) : IAiProvider
{
    private readonly OpenAiOptions _provider = providerOptions.Value;
    private readonly AiOptions _options = aiOptions.Value;
    public bool IsMock => false;
    public bool Configured => _options.Valid && !string.IsNullOrWhiteSpace(_provider.ApiKey)
        && !string.IsNullOrWhiteSpace(_provider.Model) && ValidEndpoint(_provider.Endpoint);

    private static bool ValidEndpoint(string endpoint) => string.IsNullOrWhiteSpace(endpoint)
        || (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment));

    public async Task<AiProviderResult> CompleteAsync(AiProviderRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Configured) return new("not_configured");
        if (request.Message.Length > AiRequestValidator.MaxMessageChars || request.Instructions.Length > 8000
            || request.Tools.Count > 8) return new("invalid_request");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        try
        {
            var sdkOptions = new OpenAIClientOptions
            {
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
                NetworkTimeout = TimeSpan.FromSeconds(_options.TimeoutSeconds),
            };
            if (!string.IsNullOrWhiteSpace(_provider.Endpoint)) sdkOptions.Endpoint = new Uri(_provider.Endpoint);
            var client = testClient ?? new ChatClient(_provider.Model, new ApiKeyCredential(_provider.ApiKey), sdkOptions);
            var completionOptions = new ChatCompletionOptions { MaxOutputTokenCount = _options.MaxOutputTokens };
            foreach (var tool in request.Tools)
                completionOptions.Tools.Add(ChatTool.CreateFunctionTool(tool.Name, tool.Description,
                    BinaryData.FromString(tool.InputSchema.Json), functionSchemaIsStrict: true));
            if (request.Tools.Count > 0) completionOptions.AllowParallelToolCalls = false;
            var completion = (await client.CompleteChatAsync(
                [new SystemChatMessage(request.Instructions), new UserChatMessage(request.Message)],
                completionOptions, timeout.Token)).Value;
            if (!string.IsNullOrEmpty(completion.Refusal)) return new("refused");
            var usage = completion.Usage is { } tokens ? new AiUsage(tokens.InputTokenCount, tokens.OutputTokenCount) : null;
            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                // Provider output is untrusted. This adapter never dispatches a handler.
                if (completion.ToolCalls.Count != 1) return new("invalid_output");
                var call = completion.ToolCalls[0];
                var tool = request.Tools.FirstOrDefault(t => t.Name == call.FunctionName);
                var arguments = call.FunctionArguments?.ToString() ?? "";
                if (tool is null || string.IsNullOrWhiteSpace(call.Id) || call.Id.Length > 200
                    || !tool.InputSchema.Valid(arguments)) return new("invalid_output");
                return new("ok", ToolCalls: [new(call.Id, call.FunctionName, arguments)], Usage: usage);
            }
            if (completion.FinishReason != ChatFinishReason.Stop || completion.ToolCalls.Count != 0)
                return new("invalid_output");
            var text = string.Concat(completion.Content.Select(c => c.Text));
            if (string.IsNullOrWhiteSpace(text) || text.Length > 12000) return new("invalid_output");
            return new("ok", text, Usage: usage);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return new("timeout"); }
        catch (OperationCanceledException) { throw; }
        catch (ClientResultException error) { return new(error.Status == 429 ? "provider_busy" : "provider_unavailable"); }
        // Never return provider bodies, exception messages, keys, prompts or headers.
        catch (Exception) { return new("provider_unavailable"); }
    }
}
