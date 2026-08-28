namespace Isis.Server.Routes
{
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Enums;
    using Isis.Core.Models;
    using Isis.Core.Security;
    using Isis.Server.Models;
    using Isis.Server.Serialization;
    using Isis.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Chat-with-memory routes. Answers natural-language questions about a scope's memory, grounded in
    /// retrieved memories and synthesized by the configured inference endpoint.
    /// </summary>
    public class ChatRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AuthorizationService _Authorization;
        private readonly MemoryChatService _ChatService;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">The database driver.</param>
        /// <param name="authorization">The authorization service.</param>
        /// <param name="chatService">The chat service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public ChatRoutes(DatabaseDriverBase database, AuthorizationService authorization, MemoryChatService chatService)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
            _ChatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="server">The webserver.</param>
        public void Register(Webserver server)
        {
            server.Routes.PostAuthentication.Parameter.Add(
                HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}/chat", ChatAsync, null,
                openApiMetadata: OpenApiRouteMetadata.Create("Chat with a scope's memory", "Chat"));

            server.Routes.PostAuthentication.Parameter.Add(
                HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/scopes/{scopeId}/chat/stream", ChatStreamAsync, null,
                openApiMetadata: OpenApiRouteMetadata.Create("Stream a chat answer over a scope's memory (SSE)", "Chat"));
        }

        #endregion

        #region Private-Methods

        private async Task ChatAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            string tenantId = RouteHelpers.Param(context, "tenantId") ?? string.Empty;
            string scopeId = RouteHelpers.Param(context, "scopeId") ?? string.Empty;

            if (!_Authorization.CanAccessTenant(ctx, tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            ChatRequest? request = RouteHelpers.Body<ChatRequest>(context);
            if (request == null || string.IsNullOrWhiteSpace(request.Question))
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "A question is required.").ConfigureAwait(false);
                return;
            }

            Scope? scope = await _Database.Scopes.ReadAsync(tenantId, scopeId, context.Token).ConfigureAwait(false);
            if (scope == null)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Scope not found.").ConfigureAwait(false);
                return;
            }

            ModelEndpoint? endpoint = await ResolveInferenceEndpointAsync(tenantId, request.InferenceEndpointId, context.Token).ConfigureAwait(false);
            if (endpoint == null)
            {
                await RouteHelpers.ErrorAsync(context, 400, "NoInferenceEndpoint", "No active inference endpoint is configured for this tenant. Add one under /v1.0/api/tenants/{tenantId}/endpoints.").ConfigureAwait(false);
                return;
            }

            try
            {
                ChatAnswer answer = await _ChatService.AskAsync(scope, endpoint, request.Question, request.TopK, context.Token).ConfigureAwait(false);
                await RouteHelpers.JsonAsync(context, 200, answer).ConfigureAwait(false);
            }
            catch (NotSupportedException ex)
            {
                await RouteHelpers.ErrorAsync(context, 501, "NotImplemented", ex.Message).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                await RouteHelpers.ErrorAsync(context, 502, "InferenceError", ex.Message).ConfigureAwait(false);
            }
        }

        private async Task ChatStreamAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            string tenantId = RouteHelpers.Param(context, "tenantId") ?? string.Empty;
            string scopeId = RouteHelpers.Param(context, "scopeId") ?? string.Empty;

            if (!_Authorization.CanAccessTenant(ctx, tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            ChatRequest? request = RouteHelpers.Body<ChatRequest>(context);
            if (request == null || string.IsNullOrWhiteSpace(request.Question))
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "A question is required.").ConfigureAwait(false);
                return;
            }

            Scope? scope = await _Database.Scopes.ReadAsync(tenantId, scopeId, context.Token).ConfigureAwait(false);
            if (scope == null)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Scope not found.").ConfigureAwait(false);
                return;
            }

            ModelEndpoint? endpoint = await ResolveInferenceEndpointAsync(tenantId, request.InferenceEndpointId, context.Token).ConfigureAwait(false);
            if (endpoint == null)
            {
                await RouteHelpers.ErrorAsync(context, 400, "NoInferenceEndpoint", "No active inference endpoint is configured for this tenant. Add one under /v1.0/api/tenants/{tenantId}/endpoints.").ConfigureAwait(false);
                return;
            }

            // Switch to a chunked SSE-style response. Once the first chunk is sent we can no longer change the
            // status code, so all validation above sends normal JSON errors; failures after this point are
            // delivered as an "error" event on the stream.
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/event-stream";
            context.Response.ChunkedTransfer = true;
            // Disable proxy buffering (nginx) so events reach the client incrementally rather than all at once.
            context.Response.Headers.Add("X-Accel-Buffering", "no");
            context.Response.Headers.Add("Cache-Control", "no-cache");

            Func<object, CancellationToken, Task> emit = async (payload, emitToken) =>
            {
                byte[] frame = Encoding.UTF8.GetBytes("data: " + Json.SerializeCompact(payload) + "\n\n");
                await context.Response.SendChunk(frame, false, emitToken).ConfigureAwait(false);
            };

            try
            {
                await _ChatService.AskStreamingAsync(scope, endpoint, request.Question, request.TopK, emit, context.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                try
                {
                    byte[] frame = Encoding.UTF8.GetBytes("data: " + Json.SerializeCompact(new { type = "error", message = ex.Message }) + "\n\n");
                    await context.Response.SendChunk(frame, false, context.Token).ConfigureAwait(false);
                }
                catch
                {
                    // The client may have disconnected; nothing more we can do.
                }
            }
            finally
            {
                try { await context.Response.SendChunk(Array.Empty<byte>(), true, context.Token).ConfigureAwait(false); }
                catch { }
            }
        }

        private async Task<ModelEndpoint?> ResolveInferenceEndpointAsync(string tenantId, string? endpointId, System.Threading.CancellationToken token)
        {
            if (!string.IsNullOrEmpty(endpointId))
            {
                ModelEndpoint? explicitEndpoint = await _Database.ModelEndpoints.ReadAsync(tenantId, endpointId, token).ConfigureAwait(false);
                if (explicitEndpoint != null && explicitEndpoint.Kind == EndpointKindEnum.Inference && explicitEndpoint.Active) return explicitEndpoint;
                return null;
            }

            EnumerationResult<ModelEndpoint> endpoints = await _Database.ModelEndpoints.EnumerateAsync(tenantId, EndpointKindEnum.Inference, new EnumerationQuery { MaxResults = 1000 }, token).ConfigureAwait(false);
            foreach (ModelEndpoint candidate in endpoints.Objects)
            {
                if (candidate.Active) return candidate;
            }

            return null;
        }

        #endregion
    }
}
