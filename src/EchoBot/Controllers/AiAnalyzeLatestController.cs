using EchoBot.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace EchoBot.Controllers
{
    [ApiController]
    public sealed class AiAnalyzeLatestController : ControllerBase
    {
        private readonly LatestTranscriptAnalysisSourceRepository transcriptSourceRepository;
        private readonly MeetingAnalysisService meetingAnalysisService;
        private readonly AiAnalysisTestRepository analysisRepository;
        private readonly ILogger<AiAnalyzeLatestController> logger;

        public AiAnalyzeLatestController(
            LatestTranscriptAnalysisSourceRepository transcriptSourceRepository,
            MeetingAnalysisService meetingAnalysisService,
            AiAnalysisTestRepository analysisRepository,
            ILogger<AiAnalyzeLatestController> logger)
        {
            this.transcriptSourceRepository = transcriptSourceRepository;
            this.meetingAnalysisService = meetingAnalysisService;
            this.analysisRepository = analysisRepository;
            this.logger = logger;
        }

        [HttpGet("/api/ai-analyze-latest")]
        public async Task<IActionResult> Get(CancellationToken cancellationToken)
        {
            var source = await transcriptSourceRepository.GetLatestAsync(cancellationToken).ConfigureAwait(false);
            if (source == null)
            {
                return NotFound(new
                {
                    error = "transcript_not_found",
                    message = "Local transcript storage is disabled.",
                });
            }

            var result = await meetingAnalysisService.AnalyzeAsync(source.InputText, cancellationToken).ConfigureAwait(false);
            var deploymentName = string.IsNullOrWhiteSpace(result.DeploymentName)
                ? Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "not_configured"
                : result.DeploymentName;

            var id = await analysisRepository.SaveAsync(deploymentName, source.InputText, result, cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Latest transcript AI analysis completed. Id={Id}; MeetingId={MeetingId}; MeetingIdSource={MeetingIdSource}; SegmentCount={SegmentCount}; Succeeded={Succeeded}; StatusCode={StatusCode}",
                id,
                source.MeetingId,
                source.MeetingIdSource,
                source.SegmentCount,
                result.Succeeded,
                result.StatusCode);

            var response = new
            {
                id,
                meetingId = source.MeetingId,
                meetingIdSource = source.MeetingIdSource,
                segmentCount = source.SegmentCount,
                deploymentName,
                inputText = source.InputText,
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
