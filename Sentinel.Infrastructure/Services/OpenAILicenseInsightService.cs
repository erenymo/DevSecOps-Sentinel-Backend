using Microsoft.Extensions.Options;
using OpenAI.Chat;
using Sentinel.Application.Common.Interfaces;
using Sentinel.Application.DTOs.Insights;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sentinel.Infrastructure.Services
{
    public class OpenAILicenseInsightService : IOpenAILicenseInsightService
    {
        private readonly ChatClient _chatClient;

        public OpenAILicenseInsightService(IOptions<Sentinel.Infrastructure.Configuration.OpenAISettings> options)
        {
            _chatClient = new ChatClient(options.Value.Model, options.Value.ApiKey);
        }

        public async Task<LicenseInsightResponse> GenerateInsightsAsync(List<LicenseInsightRequest> requests, CancellationToken cancellationToken = default)
        {
            if (requests == null || requests.Count == 0)
                return new LicenseInsightResponse();

            var prompt = "You are a technical risk advisor. I will provide you with a list of open-source packages and their current problematic licenses. " +
                         "For each package, output a JSON object containing an 'Insights' array. Each item in the array must include 'PackageName', 'Purl', a 'RiskExplanationForManagement' " +
                         "(explaining the risk of this license in business terms, easily understandable by managers), " +
                         "'ProblematicUseCases' (an array of strings explaining under which conditions this license creates issues), " +
                         "'SafeUseCases' (an array of strings explaining under which conditions this license is safe), " +
                         "and exactly 2 'RecommendedAlternatives' (popular alternatives licensed strictly under MIT or Apache). " +
                         "For 'ReasonForRecommendation' in alternatives, emphasize what the package does, its functionality, and its advantages over the problematic one. " +
                         "Write all content in English language strictly. " +
                         "The JSON schema must exactly match: { \"Insights\": [ { \"PackageName\": \"...\", \"Purl\": \"...\", \"RiskExplanationForManagement\": \"...\", \"ProblematicUseCases\": [\"...\"], \"SafeUseCases\": [\"...\"], \"RecommendedAlternatives\": [ { \"PackageName\": \"...\", \"LicenseType\": \"...\", \"ReasonForRecommendation\": \"...\" } ] } ] }";

            var requestJson = JsonSerializer.Serialize(requests);
            
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(prompt),
                new UserChatMessage($"Here is the data: {requestJson}")
            };

            var options = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
            };

            var response = await _chatClient.CompleteChatAsync(messages, options, cancellationToken);

            var jsonResult = response.Value.Content[0].Text;
            
            var result = JsonSerializer.Deserialize<LicenseInsightResponse>(jsonResult, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            return result ?? new LicenseInsightResponse();
        }
    }
}
