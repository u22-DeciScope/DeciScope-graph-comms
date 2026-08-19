// ***********************************************************************
// Assembly         : EchoBot
// Author           : bcage29
// Created          : 10-27-2023
//
// Last Modified By : bcage29
// Last Modified On : 10-27-2023
// ***********************************************************************
// <copyright file="BotHost.cs" company="Microsoft">
//     Copyright ©  2023
// </copyright>
// <summary></summary>
// ***********************************************************************
using DotNetEnv.Configuration;
using EchoBot.Bot;
using EchoBot.Meetings;
using EchoBot.Services;
using EchoBot.Util;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Graph.Communications.Common.Telemetry;

namespace EchoBot
{
    /// <summary>
    /// Bot Web Application
    /// </summary>
    public class BotHost : IBotHost
    {
        private readonly ILogger<BotHost> _logger;
        private WebApplication? _app;

        /// <summary>
        /// Bot Host constructor
        /// </summary>
        /// <param name="logger"></param>
        public BotHost(ILogger<BotHost> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Starting the Bot and Web App
        /// </summary>
        /// <returns></returns>
        public async Task StartAsync()
        {
            _logger.LogInformation("Starting the Echo Bot");
            // Set up the bot web application
            var builder = WebApplication.CreateBuilder();

            if (builder.Environment.IsDevelopment())
            {
                // load the .env file environment variables
                builder.Configuration.AddDotNetEnv();
            }

            // Add Environment Variables
            builder.Configuration.AddEnvironmentVariables(prefix: "AppSettings__");

            // Add services to the container.
            builder.Services.AddControllers();

            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var section = builder.Configuration.GetSection("AppSettings");
            var appSettings = section.Get<AppSettings>();

            builder.Services
                .AddOptions<AppSettings>()
                .BindConfiguration(nameof(AppSettings))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            builder.Services.AddSingleton<IGraphLogger, GraphLogger>(_ => new GraphLogger("EchoBotWorker", redirectToTrace: true));
            builder.Services.AddSingleton<AudioSocketReceiveStallDetector>();
            builder.Services.AddSingleton<IBotMediaLogger, BotMediaLogger>();
            builder.Services.AddSingleton<IRecordingStatusUpdater, RecordingStatusUpdater>();
            builder.Services.AddSingleton<ITranscriptSequenceProvider, InMemoryTranscriptSequenceProvider>();
            builder.Services.AddSingleton(serviceProvider =>
            {
                var options = MeetingJoinOptions.FromEnvironment();
                var logger = serviceProvider.GetRequiredService<ILogger<MeetingJoinOptions>>();
                logger.LogInformation(
                    "Meeting join configuration. DefaultTenantIdConfigured={DefaultTenantIdConfigured}",
                    options.DefaultTenantIdConfigured);
                return options;
            });
            builder.Services.AddSingleton(serviceProvider =>
            {
                var options = BotControlOptions.FromEnvironment();
                var logger = serviceProvider.GetRequiredService<ILogger<BotControlOptions>>();
                logger.LogInformation(
                    "Bot control configuration. JoinMode={JoinMode}; ControlApiEnabled={ControlApiEnabled}; ControlTokenConfigured={ControlTokenConfigured}; BindUrl={BindUrl}; Reason={Reason}",
                    options.JoinMode,
                    options.ControlApiEnabled,
                    !string.IsNullOrWhiteSpace(options.ControlToken),
                    options.BindUrl,
                    options.Reason);
                return options;
            });
            builder.Services.AddSingleton(serviceProvider =>
            {
                var options = TranscriptForwardingOptions.FromEnvironment();
                var logger = serviceProvider.GetRequiredService<ILogger<TranscriptForwardingOptions>>();
                if (options.Enabled)
                {
                    logger.LogInformation(
                        "Transcript forwarding configuration. Enabled={Enabled}; ApiUrl={ApiUrl}; ApiKeyConfigured={ApiKeyConfigured}; TimeoutSeconds={TimeoutSeconds}; MaxRetryAttempts={MaxRetryAttempts}; QueueCapacity={QueueCapacity}",
                        options.Enabled,
                        options.ApiUrl,
                        options.ApiKeyConfigured,
                        options.TimeoutSeconds,
                        options.MaxRetryAttempts,
                        options.QueueCapacity);
                }
                else
                {
                    if (options.RequestedEnabled)
                    {
                        logger.LogError(
                            "Transcript forwarding configuration. Enabled={Enabled}; Reason={Reason}; ApiKeyConfigured={ApiKeyConfigured}; TimeoutSeconds={TimeoutSeconds}; MaxRetryAttempts={MaxRetryAttempts}; QueueCapacity={QueueCapacity}",
                            options.Enabled,
                            options.Reason,
                            options.ApiKeyConfigured,
                            options.TimeoutSeconds,
                            options.MaxRetryAttempts,
                            options.QueueCapacity);
                    }
                    else
                    {
                        logger.LogInformation(
                            "Transcript forwarding configuration. Enabled={Enabled}; Reason={Reason}; ApiKeyConfigured={ApiKeyConfigured}; TimeoutSeconds={TimeoutSeconds}; MaxRetryAttempts={MaxRetryAttempts}; QueueCapacity={QueueCapacity}",
                            options.Enabled,
                            options.Reason,
                            options.ApiKeyConfigured,
                            options.TimeoutSeconds,
                            options.MaxRetryAttempts,
                            options.QueueCapacity);
                    }
                }

                return options;
            });
            builder.Services.AddHttpClient(TranscriptForwarder.HttpClientName, client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
            });
            builder.Services.AddHttpClient(BotMeetingStatusReporter.HttpClientName, client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
            });
            builder.Services.AddHttpClient(TeamsMeetingTitleResolver.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            });
            builder.Services.AddSingleton<TranscriptForwarder>();
            builder.Services.AddSingleton<QueuedTranscriptForwarder>();
            builder.Services.AddSingleton<ITranscriptForwarder>(serviceProvider => serviceProvider.GetRequiredService<QueuedTranscriptForwarder>());
            builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<QueuedTranscriptForwarder>());
            builder.Services.AddSingleton<BotMeetingSessionRegistry>();
            builder.Services.AddSingleton<IBotMeetingStatusReporter, BotMeetingStatusReporter>();
            builder.Services.AddSingleton<BotJoinCommandService>();
            builder.Services.AddSingleton<IBotJoinCommandService>(serviceProvider => serviceProvider.GetRequiredService<BotJoinCommandService>());
            builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<BotJoinCommandService>());
            builder.Services.AddSingleton<IMeetingTenantContext, MeetingTenantContext>();
            builder.Services.AddSingleton<ITeamsMeetingTitleResolver, TeamsMeetingTitleResolver>();
            builder.Services.AddSingleton<ITeamsMeetingJoinInfoProvider>(serviceProvider =>
                new TeamsMeetingJoinInfoProvider(
                    new TeamsMeetingUrlResolver(),
                    serviceProvider.GetRequiredService<MeetingJoinOptions>(),
                    serviceProvider.GetRequiredService<ILogger<TeamsMeetingJoinInfoProvider>>()));
            builder.Logging.AddApplicationInsights();
            builder.Logging.SetMinimumLevel(LogLevel.Information);

            builder.Logging.AddEventLog(config => config.SourceName = "Echo Bot Service");

            builder.Services.AddSingleton<IBotService, BotService>();

            // Bot Settings Setup
            var botInternalHostingProtocol = "https";
            if (appSettings.UseLocalDevSettings)
            {
                // if running locally with ngrok
                // the call signalling and notification will use the same internal and external ports
                // because you cannot receive requests on the same tunnel with different ports

                // calls come in over 443 (external) and route to the internally hosted port: BotCallingInternalPort
                botInternalHostingProtocol = "http";

                builder.Services.PostConfigure<AppSettings>(options =>
                {
                    options.BotInstanceExternalPort = 443;
                    options.BotInternalPort = appSettings.BotCallingInternalPort;

                });
            }
            else
            {
                //appSettings.MediaDnsName = appSettings.ServiceDnsName;
                builder.Services.PostConfigure<AppSettings>(options =>
                {
                    options.MediaDnsName = appSettings.ServiceDnsName;
                });
            }

            // localhost
            var baseDomain = "+";

            // http for local development
            // https for running on VM
            var callListeningUris = new HashSet<string>
            {
                $"{botInternalHostingProtocol}://{baseDomain}:{appSettings.BotCallingInternalPort}/",
                $"{botInternalHostingProtocol}://{baseDomain}:{appSettings.BotInternalPort}/"
            };

            var botControlOptions = BotControlOptions.FromEnvironment();
            if (!string.IsNullOrWhiteSpace(botControlOptions.BindUrl))
            {
                callListeningUris.Add(botControlOptions.BindUrl);
            }

            builder.WebHost.UseUrls(callListeningUris.ToArray());

            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                serverOptions.ConfigureHttpsDefaults(listenOptions =>
                {
                    listenOptions.ServerCertificate = Utilities.GetCertificateFromStore(appSettings.CertificateThumbprint);
                });
            });

            _app = builder.Build();

            using (var scope = _app.Services.CreateScope())
            {
                var bot = scope.ServiceProvider.GetRequiredService<IBotService>();
                bot.Initialize();
            }

            // Configure the HTTP request pipeline.
            if (_app.Environment.IsDevelopment())
            {
                // https://localhost:<port>/swagger
                _app.UseSwagger();
                _app.UseSwaggerUI();
            }

            _app.UseAuthorization();

            _app.MapControllers();

            await _app.RunAsync();
        }

        /// <summary>
        /// Stop the bot web application
        /// </summary>
        /// <returns></returns>
        public async Task StopAsync()
        {
            if (_app != null) 
            {
                using (var scope = _app.Services.CreateScope())
                {
                    var bot = scope.ServiceProvider.GetRequiredService<IBotService>();
                    // terminate all calls and dispose of the call client
                    await bot.Shutdown();
                }

                // stop the bot web application
                await _app.StopAsync();
            }
        }
    }
}
