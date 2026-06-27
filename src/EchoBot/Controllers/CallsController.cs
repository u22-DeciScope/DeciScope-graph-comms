// ***********************************************************************
// Assembly         : EchoBot.Controllers
// Author           : JasonTheDeveloper
// Created          : 09-07-2020
//
// Last Modified By : bcage29
// Last Modified On : 02-28-2022
// ***********************************************************************
// <copyright file="JoinCallController.cs" company="Microsoft">
//     Copyright ©  2023
// </copyright>
// <summary></summary>
// ***********************************************************************
using EchoBot.Bot;
using EchoBot.Constants;
using EchoBot.Meetings;
using EchoBot.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Net;

namespace EchoBot.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class CallsController : ControllerBase
    {
        private readonly ILogger<CallsController> _logger;
        private readonly AppSettings _settings;
        private readonly IBotService _botService;

        public CallsController(ILogger<CallsController> logger,
            IOptions<AppSettings> settings,
            IBotService botService)
        {
            _logger = logger;
            _settings = settings.Value;
            _botService = botService;
        }

        /// <summary>
        /// The join call async.
        /// </summary>
        /// <param name="joinCallBody">The join call body.</param>
        /// <returns>The <see cref="HttpResponseMessage" />.</returns>
        [HttpPost]
        [HttpPost("/joinCall")]
        public async Task<IActionResult> JoinCallAsync([FromBody] JoinCallBody joinCallBody)
        {
            try
            {
                _logger.LogInformation("Received Teams meeting join request.");
                var call = await _botService.JoinCallAsync(joinCallBody, HttpContext.RequestAborted).ConfigureAwait(false);

                var threadId = call.Resource?.ChatInfo?.ThreadId;

                var callKey = !string.IsNullOrWhiteSpace(threadId)
                    ? threadId
                    : call.Id;

                var values = new
                {
                    CallId = call.Id,
                    ScenarioId = call.ScenarioId,
                    ThreadId = threadId,
                    CallKey = callKey,
                    Port = _settings.BotInstanceExternalPort.ToString(),
                };

                return Ok(values);
            }
            catch (TeamsMeetingJoinException e)
            {
                _logger.LogWarning(
                    e,
                    "Teams meeting join request validation failed. Code={Code}; Method={Method}; Path={Path}",
                    e.Code,
                    this.Request.Method,
                    this.Request.Path);

                return BadRequest(new
                {
                    Error = e.Code,
                    Message = e.Message,
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Teams meeting join request failed. Method={Method}; Path={Path}", this.Request.Method, this.Request.Path);
                return Problem(statusCode: (int)HttpStatusCode.InternalServerError, title: "Failed to join the Teams meeting.");
            }
        }

        /// <summary>
        /// End the call.
        /// </summary>
        /// <param name="threadId">Thread Id of the call to end.</param>
        /// <returns>The <see cref="HttpResponseMessage" />.</returns>
        [HttpDelete]
        public async Task<IActionResult> OnEndCallAsync(string threadId)
        {
            _logger.LogInformation($"Ending call {threadId}");

            try
            {
                await _botService.EndCallByThreadIdAsync(threadId).ConfigureAwait(false);
                return NoContent();
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Received HTTP {this.Request.Method}, {this.Request.Path}");
                return Problem(detail: e.StackTrace, statusCode: (int)HttpStatusCode.InternalServerError, title: e.Message);
            }
        }
    }
}
