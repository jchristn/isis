namespace Isis.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Isis.Core.Models;
    using Isis.Core.Responses;
    using Isis.Core.Security;
    using Isis.Server.Serialization;
    using WatsonWebserver.Core;

    /// <summary>
    /// Shared helpers for route handlers: reading the authenticated context, writing JSON responses, and
    /// binding typed request bodies.
    /// </summary>
    public static class RouteHelpers
    {
        #region Public-Methods

        /// <summary>
        /// Read the authenticated request context from the Watson context metadata.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <returns>The request context, or an unauthenticated context if none is present.</returns>
        public static RequestContext Context(HttpContextBase context)
        {
            if (context.Metadata is RequestContext requestContext) return requestContext;
            return RequestContext.Unauthenticated();
        }

        /// <summary>
        /// Write a JSON response with the given status code.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="statusCode">HTTP status code.</param>
        /// <param name="body">Body object.</param>
        /// <returns>Awaitable task.</returns>
        public static async Task JsonAsync(HttpContextBase context, int statusCode, object? body)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            await context.Response.Send(Json.Serialize(body)).ConfigureAwait(false);
        }

        /// <summary>
        /// Write a JSON error response.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="statusCode">HTTP status code.</param>
        /// <param name="error">Error code.</param>
        /// <param name="message">Message.</param>
        /// <returns>Awaitable task.</returns>
        public static async Task ErrorAsync(HttpContextBase context, int statusCode, string error, string message)
        {
            await JsonAsync(context, statusCode, new ErrorResponse(error, message)).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a route parameter value.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="key">Parameter name.</param>
        /// <returns>The value, or null.</returns>
        public static string? Param(HttpContextBase context, string key)
        {
            return context.Request.Url.Parameters[key];
        }

        /// <summary>
        /// Read a query string value.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="key">Query key.</param>
        /// <returns>The value, or null.</returns>
        public static string? Query(HttpContextBase context, string key)
        {
            if (context.Request.Query == null || context.Request.Query.Elements == null) return null;
            return context.Request.Query.Elements[key];
        }

        /// <summary>
        /// Read an integer query string value.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="key">Query key.</param>
        /// <returns>The value, or null.</returns>
        public static int? QueryInt(HttpContextBase context, string key)
        {
            string? value = Query(context, key);
            if (!string.IsNullOrEmpty(value) && int.TryParse(value, out int parsed)) return parsed;
            return null;
        }

        /// <summary>
        /// Build a new enumeration query from the query string (maxResults, skip, q).
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <returns>A populated enumeration query.</returns>
        public static EnumerationQuery Enumeration(HttpContextBase context)
        {
            EnumerationQuery query = new EnumerationQuery();
            int? maxResults = QueryInt(context, "maxResults");
            if (maxResults.HasValue) query.MaxResults = maxResults.Value;
            int? skip = QueryInt(context, "skip");
            if (skip.HasValue) query.Skip = skip.Value;
            string? search = Query(context, "q");
            if (!string.IsNullOrEmpty(search)) query.SearchTerm = search;
            return query;
        }

        /// <summary>
        /// Bind the request body to a typed value, or null when absent or invalid.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="context">HTTP context.</param>
        /// <returns>The deserialized body, or null.</returns>
        public static T? Body<T>(HttpContextBase context) where T : class
        {
            string body = context.Request.DataAsString;
            if (string.IsNullOrEmpty(body)) return null;
            try
            {
                return Json.Deserialize<T>(body);
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }

        #endregion
    }
}
