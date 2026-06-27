// ***********************************************************************
// Assembly         : EchoBot.Controllers
// Author           : JasonTheDeveloper
// Created          : 09-07-2020
//
// Last Modified By : bcage29
// Last Modified On : 02-28-2022
// ***********************************************************************
// <copyright file="PlatformCallController.cs" company="Microsoft">
//     Copyright ©  2023
// </copyright>
// <summary></summary>
// ***********************************************************************>
using EchoBot.Bot;
using EchoBot.Constants;
using EchoBot.Util;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Communications.Client;
using System.Net;

namespace EchoBot.Controllers
{
    /// <summary>
    /// Entry point for handling call-related web hook requests from Skype Platform.
    /// </summary>
    [ApiController]
    [Route(HttpRouteConstants.CallSignalingRoutePrefix)]
    public class PlatformCallController : ControllerBase
    {
        private readonly ILogger<PlatformCallController> _logger;
        private readonly AppSettings _settings;
        private readonly IBotService _botService;

        public PlatformCallController(ILogger<PlatformCallController> logger,
            IOptions<AppSettings> settings,
            IBotService botService)
        {
            _logger = logger;
            _settings = settings.Value;
            _botService = botService;
        }

        /// <summary>
        /// Handle a callback for an incoming call.
        /// </summary>
        /// <returns>The <see cref="HttpResponseMessage" />.</returns>
        [HttpPost]
        [Route(HttpRouteConstants.OnIncomingRequestRoute)]
        public async Task<HttpResponseMessage> OnIncomingRequestAsync()
        {
            return await ProcessNotificationAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Handle a callback for an existing call
        /// </summary>
        /// <returns>The <see cref="HttpResponseMessage" />.</returns>
        [HttpPost]
        [Route(HttpRouteConstants.OnNotificationRequestRoute)]
        public async Task<HttpResponseMessage> OnNotificationRequestAsync()
        {
            return await ProcessNotificationAsync().ConfigureAwait(false);
        }

        private async Task<HttpResponseMessage> ProcessNotificationAsync()
        {
            HttpRequestMessage httpRequestMessage;
            try
            {
                httpRequestMessage = HttpHelpers.ToHttpRequestMessage(this.Request);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Unable to convert incoming call notification request to an absolute URI. Scheme: {Scheme}, Host: {Host}, Path: {Path}",
                    Request.Scheme,
                    Request.Host.Value,
                    Request.Path.Value);

                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("The incoming call notification request URI is invalid."),
                };
            }

            // Pass the incoming notification to the sdk. The sdk takes care of what to do with it.
            return await _botService.Client.ProcessNotificationAsync(httpRequestMessage).ConfigureAwait(false);
        }
    }
}
