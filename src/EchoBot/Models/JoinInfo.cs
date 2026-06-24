// ***********************************************************************
// Assembly         : EchoBot.Models
// Author           : JasonTheDeveloper
// Created          : 09-07-2020
//
// Last Modified By : bcage29
// Last Modified On : 10-27-2023
// ***********************************************************************
// <copyright file="JoinInfo.cs" company="Microsoft">
//     Copyright ©  2023
// </copyright>
// <summary></summary>
// ***********************************************************************
using Microsoft.Graph;
using Microsoft.Graph.Contracts;
using Microsoft.Graph.Models;
using System.Net;
using System.Text.Json;

namespace EchoBot.Models
{
    /// <summary>
    /// Gets the join information.
    /// </summary>
    public class JoinInfo
    {
        /// <summary>
        /// Parse Join URL into its components.
        /// </summary>
        /// <param name="joinURL">Join URL from Team's meeting body.</param>
        /// <param name="tenantId">Tenant id supplied separately when the URL does not contain it.</param>
        /// <returns>Parsed data.</returns>
        /// <exception cref="ArgumentException">Join URL cannot be parsed.</exception>
        public static (ChatInfo, MeetingInfo) ParseJoinURL(string joinURL, string? tenantId = null)
        {
            if (string.IsNullOrWhiteSpace(joinURL))
            {
                throw new ArgumentException("Join URL cannot be null or empty", nameof(joinURL));
            }

            if (!Uri.TryCreate(joinURL.Trim(), UriKind.Absolute, out var uri))
            {
                throw new ArgumentException("Join URL is not a valid absolute URL", nameof(joinURL));
            }

            if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Join URL must use HTTPS", nameof(joinURL));
            }

            var pathSegments = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => WebUtility.UrlDecode(segment))
                .ToArray();

            if (TryParseMeetupJoin(uri, pathSegments, out var chatInfo, out var meetingInfo))
            {
                return (chatInfo, meetingInfo);
            }

            if (TryParseShortMeetingUrl(uri, pathSegments, out meetingInfo))
            {
                return (new ChatInfo(), meetingInfo);
            }

            throw new ArgumentException("Join URL cannot be parsed as a supported Teams meeting URL", nameof(joinURL));
        }

        private static bool TryParseMeetupJoin(
            Uri uri,
            IReadOnlyList<string> pathSegments,
            out ChatInfo chatInfo,
            out MeetingInfo meetingInfo)
        {
            chatInfo = new ChatInfo();
            meetingInfo = new OrganizerMeetingInfo();

            var meetupIndex = IndexOfSegment(pathSegments, "meetup-join");
            if (meetupIndex < 1 || meetupIndex + 2 >= pathSegments.Count)
            {
                return false;
            }

            var threadId = pathSegments[meetupIndex + 1];
            var messageId = pathSegments[meetupIndex + 2];
            var query = ParseQuery(uri.Query);

            if (!query.TryGetValue("context", out var context) || string.IsNullOrWhiteSpace(context))
            {
                throw new ArgumentException("Join URL is invalid: missing context", nameof(uri));
            }

            var meeting = ParseContext(context);
            if (string.IsNullOrWhiteSpace(meeting.Tid))
            {
                throw new ArgumentException("Join URL is invalid: missing tenant id", nameof(uri));
            }

            if (string.IsNullOrWhiteSpace(meeting.Oid))
            {
                throw new ArgumentException("Join URL is invalid: missing organizer id", nameof(uri));
            }

            chatInfo = new ChatInfo
            {
                ThreadId = threadId,
                MessageId = messageId,
                ReplyChainMessageId = string.IsNullOrWhiteSpace(meeting.MessageId) ? messageId : meeting.MessageId,
            };

            var organizerMeetingInfo = new OrganizerMeetingInfo
            {
                Organizer = new IdentitySet
                {
                    User = new Identity { Id = meeting.Oid },
                },
            };
            organizerMeetingInfo.Organizer.User.SetTenantId(meeting.Tid);

            meetingInfo = organizerMeetingInfo;
            return true;
        }

        private static bool TryParseShortMeetingUrl(
            Uri uri,
            IReadOnlyList<string> pathSegments,
            out MeetingInfo meetingInfo)
        {
            meetingInfo = new JoinMeetingIdMeetingInfo();

            var meetIndex = IndexOfSegment(pathSegments, "meet");
            if (meetIndex < 0 || meetIndex + 1 >= pathSegments.Count)
            {
                return false;
            }

            var meetingId = pathSegments[meetIndex + 1].TrimEnd('/');
            var query = ParseQuery(uri.Query);

            if (string.IsNullOrWhiteSpace(meetingId))
            {
                throw new ArgumentException("Join URL is invalid: missing meeting id", nameof(uri));
            }

            if (!query.TryGetValue("p", out var passcode) || string.IsNullOrWhiteSpace(passcode))
            {
                throw new ArgumentException("Join URL is invalid: missing meeting passcode", nameof(uri));
            }

            meetingInfo = new JoinMeetingIdMeetingInfo
            {
                JoinMeetingId = meetingId,
                Passcode = passcode,
            };

            return true;
        }

        private static Meeting ParseContext(string context)
        {
            var decodedContext = WebUtility.UrlDecode(context);

            using var document = JsonDocument.Parse(decodedContext);
            var root = document.RootElement;

            return new Meeting
            {
                Tid = GetJsonString(root, "Tid", "tid") ?? string.Empty,
                Oid = GetJsonString(root, "Oid", "oid") ?? string.Empty,
                MessageId = GetJsonString(root, "MessageId", "messageId") ?? string.Empty,
            };
        }

        private static string? GetJsonString(JsonElement root, params string[] names)
        {
            foreach (var name in names)
            {
                if (root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
                {
                    return property.GetString();
                }
            }

            return null;
        }

        private static int IndexOfSegment(IReadOnlyList<string> segments, string expectedSegment)
        {
            for (var i = 0; i < segments.Count; i++)
            {
                if (segments[i].Equals(expectedSegment, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(query))
            {
                return result;
            }

            foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                var key = WebUtility.UrlDecode(parts[0]);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                result[key] = parts.Length == 2 ? WebUtility.UrlDecode(parts[1]) : string.Empty;
            }

            return result;
        }
    }
}
