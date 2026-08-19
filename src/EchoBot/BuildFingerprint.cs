using System.Reflection;

namespace EchoBot
{
    /// <summary>
    /// Non-secret release identity embedded in the assembly and emitted at startup.
    /// </summary>
    public sealed class BuildFingerprint
    {
        private BuildFingerprint(
            string repositoryName,
            string assemblyVersion,
            string informationalVersion,
            string buildVersion,
            string gitCommitSha,
            string buildTimestamp,
            string dirtyBuild,
            string runtimeEnvironment)
        {
            RepositoryName = repositoryName;
            AssemblyVersion = assemblyVersion;
            InformationalVersion = informationalVersion;
            BuildVersion = buildVersion;
            GitCommitSha = gitCommitSha;
            BuildTimestamp = buildTimestamp;
            DirtyBuild = dirtyBuild;
            RuntimeEnvironment = runtimeEnvironment;
        }

        public static BuildFingerprint Current { get; } = FromAssembly(typeof(BuildFingerprint).Assembly);

        public string RepositoryName { get; }
        public string AssemblyVersion { get; }
        public string InformationalVersion { get; }
        public string BuildVersion { get; }
        public string GitCommitSha { get; }
        public string BuildTimestamp { get; }
        public string DirtyBuild { get; }
        public string RuntimeEnvironment { get; }

        internal static BuildFingerprint FromAssembly(Assembly assembly)
        {
            var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .GroupBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last().Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            string Metadata(string key, string fallback) =>
                metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                    ? value.Trim()
                    : fallback;

            return new BuildFingerprint(
                Metadata("RepositoryName", "DeciScope-graph-comms"),
                assembly.GetName().Version?.ToString() ?? "unknown",
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
                Metadata("BuildVersion", "unknown"),
                Metadata("GitCommitSha", "unknown"),
                Metadata("BuildTimestamp", "unknown"),
                Metadata("DirtyBuild", "unknown"),
                Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                    ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                    ?? "Production");
        }
    }
}
