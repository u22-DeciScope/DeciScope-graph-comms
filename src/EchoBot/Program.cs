using EchoBot;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;

IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "Echo Bot Service";
    })
    .ConfigureServices(services =>
    {
        LoggerProviderOptions.RegisterProviderOptions<
            EventLogSettings, EventLogLoggerProvider>(services);

        services.AddSingleton<IBotHost, BotHost>();

        services.AddHostedService<EchoBotWorker>();
    })
    .Build();

var fingerprint = BuildFingerprint.Current;
host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("BuildFingerprint")
    .LogInformation(
        "Build fingerprint. RepositoryName={RepositoryName}; BuildVersion={BuildVersion}; AssemblyVersion={AssemblyVersion}; InformationalVersion={InformationalVersion}; GitCommitSha={GitCommitSha}; BuildTimestamp={BuildTimestamp}; DirtyBuild={DirtyBuild}; RuntimeEnvironment={RuntimeEnvironment}",
        fingerprint.RepositoryName,
        fingerprint.BuildVersion,
        fingerprint.AssemblyVersion,
        fingerprint.InformationalVersion,
        fingerprint.GitCommitSha,
        fingerprint.BuildTimestamp,
        fingerprint.DirtyBuild,
        fingerprint.RuntimeEnvironment);

await host.RunAsync();
