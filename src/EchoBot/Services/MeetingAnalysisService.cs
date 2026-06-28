using System.Net;
using System.Text;
using System.Text.Json;

namespace EchoBot.Services
{
    public sealed class MeetingAnalysisService
    {
        public const string HttpClientName = "AzureOpenAIResponses";

        private const string EndpointEnvironmentVariable = "AZURE_OPENAI_ENDPOINT";
        private const string ApiKeyEnvironmentVariable = "AZURE_OPENAI_API_KEY";
        private const string DeploymentEnvironmentVariable = "AZURE_OPENAI_DEPLOYMENT";
        private const string NotConfiguredDeploymentName = "not_configured";

        private readonly IHttpClientFactory httpClientFactory;
        private readonly ILogger<MeetingAnalysisService> logger;

        public MeetingAnalysisService(
            IHttpClientFactory httpClientFactory,
            ILogger<MeetingAnalysisService> logger)
        {
            this.httpClientFactory = httpClientFactory;
            this.logger = logger;
        }

        public async Task<MeetingAnalysisResult> AnalyzeAsync(string transcriptText, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(transcriptText))
            {
                throw new ArgumentException("Transcript text is required.", nameof(transcriptText));
            }

            var endpoint = Environment.GetEnvironmentVariable(EndpointEnvironmentVariable);
            var apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
            var deployment = Environment.GetEnvironmentVariable(DeploymentEnvironmentVariable);

            if (string.IsNullOrWhiteSpace(endpoint)
                || string.IsNullOrWhiteSpace(apiKey)
                || string.IsNullOrWhiteSpace(deployment))
            {
                const string message = "Azure OpenAI environment variables are not fully configured.";
                logger.LogError(
                    "Azure OpenAI request failed. DeploymentConfigured={DeploymentConfigured}; EndpointConfigured={EndpointConfigured}; ApiKeyConfigured={ApiKeyConfigured}; ExceptionMessage={ExceptionMessage}",
                    !string.IsNullOrWhiteSpace(deployment),
                    !string.IsNullOrWhiteSpace(endpoint),
                    !string.IsNullOrWhiteSpace(apiKey),
                    message);

                return MeetingAnalysisResult.Failure(
                    string.IsNullOrWhiteSpace(deployment) ? NotConfiguredDeploymentName : deployment,
                    null,
                    null,
                    message);
            }

            try
            {
                var requestUri = $"{endpoint.TrimEnd('/')}/openai/v1/responses";
                using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
                request.Headers.Add("api-key", apiKey);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        model = deployment,
                        input = CreateInput(transcriptText),
                        reasoning = new
                        {
                            effort = "low",
                        },
                        text = new
                        {
                            verbosity = "low",
                        },
                        store = false,
                        max_output_tokens = 2000,
                    }),
                    Encoding.UTF8,
                    "application/json");

                var client = httpClientFactory.CreateClient(HttpClientName);
                using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var rawResponseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var statusCode = (int)response.StatusCode;
                var responseStatus = ExtractResponseStatus(rawResponseJson);
                var outputText = ExtractOutputText(rawResponseJson);

                if (response.IsSuccessStatusCode
                    && string.Equals(responseStatus, "completed", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(outputText))
                {
                    logger.LogInformation(
                        "Azure OpenAI request succeeded. DeploymentName={DeploymentName}; ResponseTextLength={ResponseTextLength}",
                        deployment,
                        outputText.Length);

                    return MeetingAnalysisResult.Success(deployment, outputText, rawResponseJson, statusCode);
                }

                var errorMessage = CreateFailureMessage(response, responseStatus, outputText);
                logger.LogError(
                    "Azure OpenAI request failed. HTTPStatusCode={HTTPStatusCode}; ResponseStatus={ResponseStatus}; ResponseBody={ResponseBody}; ExceptionMessage={ExceptionMessage}",
                    statusCode,
                    responseStatus,
                    rawResponseJson,
                    errorMessage);

                return MeetingAnalysisResult.Failure(deployment, rawResponseJson, statusCode, errorMessage);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Azure OpenAI request failed. DeploymentName={DeploymentName}; ExceptionMessage={ExceptionMessage}",
                    deployment,
                    ex.Message);

                return MeetingAnalysisResult.Failure(deployment, null, null, ex.Message);
            }
        }

        private static string CreateInput(string transcriptText)
        {
            return @"あなたは会議分析AIです。
以下の会議文字起こしから、要約、ToDo、未解決論点をJSONで返してください。
日本語の単語の途中に空白を入れないでください。
JSONのみ返してください。
Markdownコードブロックを使わないでください。

出力形式:
{
""summary"": ""..."",
""todos"": [
{
""task"": ""..."",
""owner"": ""..."",
""evidence"": ""...""
}
],
""openIssues"": [
{
""issue"": ""..."",
""evidence"": ""...""
}
]
}

会議文字起こし:
" + transcriptText;
        }

        private static string? ExtractOutputText(string rawResponseJson)
        {
            if (string.IsNullOrWhiteSpace(rawResponseJson))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(rawResponseJson);
                var root = document.RootElement;
                if (root.TryGetProperty("output_text", out var outputTextElement)
                    && outputTextElement.ValueKind == JsonValueKind.String)
                {
                    return outputTextElement.GetString();
                }

                if (root.TryGetProperty("output", out var outputElement)
                    && outputElement.ValueKind == JsonValueKind.Array)
                {
                    var builder = new StringBuilder();
                    foreach (var outputItem in outputElement.EnumerateArray())
                    {
                        if (!outputItem.TryGetProperty("content", out var contentElement)
                            || contentElement.ValueKind != JsonValueKind.Array)
                        {
                            continue;
                        }

                        foreach (var contentItem in contentElement.EnumerateArray())
                        {
                            if (contentItem.TryGetProperty("text", out var textElement)
                                && textElement.ValueKind == JsonValueKind.String)
                            {
                                builder.Append(textElement.GetString());
                            }
                        }
                    }

                    return builder.Length > 0 ? builder.ToString() : null;
                }
            }
            catch (JsonException)
            {
                return null;
            }

            return null;
        }

        private static string? ExtractResponseStatus(string rawResponseJson)
        {
            if (string.IsNullOrWhiteSpace(rawResponseJson))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(rawResponseJson);
                return document.RootElement.TryGetProperty("status", out var statusElement)
                    && statusElement.ValueKind == JsonValueKind.String
                    ? statusElement.GetString()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string CreateFailureMessage(HttpResponseMessage response, string? responseStatus, string? outputText)
        {
            if (!response.IsSuccessStatusCode)
            {
                return response.ReasonPhrase ?? "Azure OpenAI request returned a non-success HTTP status code.";
            }

            if (!string.Equals(responseStatus, "completed", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(responseStatus)
                    ? "Azure OpenAI response status was missing or empty."
                    : $"Azure OpenAI response status was '{responseStatus}'.";
            }

            if (string.IsNullOrWhiteSpace(outputText))
            {
                return "Azure OpenAI response output_text was empty.";
            }

            return "Azure OpenAI request failed.";
        }
    }

    public sealed class MeetingAnalysisResult
    {
        private MeetingAnalysisResult(
            bool succeeded,
            string deploymentName,
            string? outputText,
            string? rawResponseJson,
            int? statusCode,
            string? errorMessage)
        {
            Succeeded = succeeded;
            DeploymentName = deploymentName;
            OutputText = outputText;
            RawResponseJson = rawResponseJson;
            StatusCode = statusCode;
            ErrorMessage = errorMessage;
        }

        public bool Succeeded { get; }

        public string DeploymentName { get; }

        public string? OutputText { get; }

        public string? RawResponseJson { get; }

        public int? StatusCode { get; }

        public string? ErrorMessage { get; }

        public static MeetingAnalysisResult Success(
            string deploymentName,
            string? outputText,
            string? rawResponseJson,
            int statusCode) =>
            new MeetingAnalysisResult(true, deploymentName, outputText, rawResponseJson, statusCode, null);

        public static MeetingAnalysisResult Failure(
            string deploymentName,
            string? rawResponseJson,
            int? statusCode,
            string? errorMessage) =>
            new MeetingAnalysisResult(false, deploymentName, null, rawResponseJson, statusCode, errorMessage);
    }
}
