// ***********************************************************************
// Assembly         : EchoBot.Bot
// Author           : JasonTheDeveloper
// Created          : 09-07-2020
//
// Last Modified By : bcage29
// Last Modified On : 10-17-2023
// ***********************************************************************
// <copyright file="IBotService.cs" company="Microsoft">
//     Copyright ©  2023
// </copyrigh>t
// <summary></summary>
// ***********************************************************************
using Microsoft.Graph.Communications.Calls;
using Microsoft.Graph.Communications.Client;
using System.Collections.Concurrent;

namespace EchoBot.Bot
{
    /// <summary>
    /// Interface IBotService
    /// </summary>
    public interface IBotService
    {
        /// <summary>
        /// Gets the collection of call handlers.
        /// </summary>
        /// <value>The call handlers.</value>
        ConcurrentDictionary<string, CallHandler> CallHandlers { get; }

        /// <summary>
        /// Gets the entry point for stateful bot.
        /// </summary>
        /// <value>The client.</value>
        ICommunicationsClient Client { get; }

        /// <summary>
        /// End a DeciScope command meeting session.
        /// </summary>
        /// <param name="sessionId">The DeciScope meeting session id.</param>
        /// <param name="reason">The reason for ending the session.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>True when an active call handler was found and leave was requested.</returns>
        Task<bool> EndMeetingSessionAsync(string sessionId, string? reason = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Joins a Teams meeting for a DeciScope command session.
        /// </summary>
        /// <param name="sessionId">The DeciScope meeting session id.</param>
        /// <param name="joinUrl">The Teams meeting join URL.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The <see cref="ICall" /> that was requested to join.</returns>
        Task<ICall> JoinMeetingAsync(
            string sessionId,
            string joinUrl,
            string? tenantId = null,
            IReadOnlyCollection<string>? candidateUserIds = null,
            string? joinMeetingId = null,
            string? canonicalJoinWebUrl = null,
            IReadOnlyCollection<string>? candidateUserPrincipalNames = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Initialize the bot instance
        /// </summary>
        void Initialize();

        /// <summary>
        /// Shutdown the bot instance
        /// </summary>
        /// <returns></returns>
        Task Shutdown();
    }
}

