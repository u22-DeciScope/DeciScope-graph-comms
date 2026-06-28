using EchoBot.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace EchoBot.Controllers
{
    [ApiController]
    public sealed class AiTestController : ControllerBase
    {
        private const string TestTranscript = @"[00:00:01] 山田: 次回までにAPI仕様を確認します。
[00:00:04] 佐藤: 認証まわりの不具合はまだ原因不明です。
[00:00:08] 山田: では、フロント側の認証状態更新タイミングを調査しましょう。";

        private readonly MeetingAnalysisService meetingAnalysisService;
        private readonly AiAnalysisTestRepository repository;
        private readonly ILogger<AiTestController> logger;

        public AiTestController(
            MeetingAnalysisService meetingAnalysisService,
            AiAnalysisTestRepository repository,
            ILogger<AiTestController> logger)
        {
            this.meetingAnalysisService = meetingAnalysisService;
            this.repository = repository;
            this.logger = logger;
        }

        [HttpGet("/api/ai-test")]
        public async Task<IActionResult> Get(CancellationToken cancellationToken)
        {
            var result = await meetingAnalysisService.AnalyzeAsync(TestTranscript, cancellationToken).ConfigureAwait(false);
            var deploymentName = string.IsNullOrWhiteSpace(result.DeploymentName)
                ? Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "not_configured"
                : result.DeploymentName;

            var id = await repository.SaveAsync(deploymentName, TestTranscript, result, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("AI test completed. Id={Id}; Succeeded={Succeeded}; StatusCode={StatusCode}", id, result.Succeeded, result.StatusCode);

            var response = new
            {
                id,
                deploymentName,
                inputText = TestTranscript,
                outputText = result.OutputText,
                rawResponseJson = result.RawResponseJson,
                statusCode = result.StatusCode,
                errorMessage = result.ErrorMessage,
            };

            if (result.Succeeded)
            {
                return Ok(response);
            }

            return StatusCode((int)HttpStatusCode.BadGateway, response);
        }
    }
}
