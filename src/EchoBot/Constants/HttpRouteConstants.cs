// ***********************************************************************
// Assembly         : EchoBot.Constants
// Author           : JasonTheDeveloper
// Created          : 09-07-2020
//
// Last Modified By : bcage29
// Last Modified On : 10-27-2023
// ***********************************************************************
// <copyright file="HttpRouteConstants.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
// </copyright>
// <summary>HTTP route constants for routing requests to CallController methods.</summary>
// ***********************************************************************-
namespace EchoBot.Constants
{
    public static class HttpRouteConstants
    {
        /// <summary>
        /// Route prefix for all incoming requests.
        /// </summary>
        public const string CallSignalingRoutePrefix = "api/calling";

        /// <summary>
        /// Route for incoming call requests.
        /// </summary>
        public const string OnIncomingRequestRoute = "";

        /// <summary>
        /// Route for incoming notification requests.
        /// </summary>
        public const string OnNotificationRequestRoute = "notification";

    }
}

