namespace EchoBot.Services
{
    public sealed class AiAnalysisTestRepository
    {
        private readonly ILogger<AiAnalysisTestRepository> logger;
        private int lastId;

        public AiAnalysisTestRepository(ILogger<AiAnalysisTestRepository> logger)
        {
            this.logger = logger;
        }

        public Task<int> SaveAsync(
            string deploymentName,
            string inputText,
            MeetingAnalysisResult result,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(deploymentName))
            {
                throw new ArgumentException("Deployment name is required.", nameof(deploymentName));
            }

            if (string.IsNullOrWhiteSpace(inputText))
            {
                throw new ArgumentException("Input text is required.", nameof(inputText));
            }
            ArgumentNullException.ThrowIfNull(result);

            var id = Interlocked.Increment(ref lastId);
            logger.LogInformation(
                "AI analysis test result was not persisted. Id={Id}; DeploymentName={DeploymentName}; Succeeded={Succeeded}; StatusCode={StatusCode}",
                id,
                deploymentName,
                result.Succeeded,
                result.StatusCode);
            return Task.FromResult(id);
        }
    }
}
