namespace Isis.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Isis.Core.Database;
    using Isis.Core.Models;
    using Isis.Core.Security;
    using Isis.Server.Models;
    using Isis.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Tenant-scoped agent instruction routes. Instructions are surfaced to agents over MCP (isis_instructions).
    /// Reads are permitted to any principal in the tenant; writes require tenant administration.
    /// </summary>
    public class InstructionRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AuthorizationService _Authorization;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">The database driver.</param>
        /// <param name="authorization">The authorization service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public InstructionRoutes(DatabaseDriverBase database, AuthorizationService authorization)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="server">The webserver.</param>
        public void Register(Webserver server)
        {
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/instructions", ListAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("List instructions", "Instructions"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/instructions", CreateAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Create an instruction", "Instructions"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/tenants/{tenantId}/instructions/{instructionId}", ReadAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Read an instruction", "Instructions"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/api/tenants/{tenantId}/instructions/{instructionId}", UpdateAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Update an instruction", "Instructions"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/api/tenants/{tenantId}/instructions/{instructionId}", DeleteAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Delete an instruction", "Instructions"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/instructions/batch-get", BatchGetAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Batch-get instructions", "Instructions"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/instructions/batch", BatchCreateAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Batch-create instructions", "Instructions"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/api/tenants/{tenantId}/instructions/batch-delete", BatchDeleteAsync, null, openApiMetadata: OpenApiRouteMetadata.Create("Batch-delete instructions", "Instructions"));
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            string tenantId = RouteHelpers.Param(context, "tenantId") ?? string.Empty;
            if (!_Authorization.CanAccessTenant(ctx, tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            EnumerationResult<Instruction> result = await _Database.Instructions.EnumerateAsync(tenantId, RouteHelpers.Enumeration(context), context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, result).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            string tenantId = RouteHelpers.Param(context, "tenantId") ?? string.Empty;
            if (!_Authorization.CanAdministerTenant(ctx, tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted to manage instructions for this tenant.").ConfigureAwait(false);
                return;
            }

            Instruction? instruction = RouteHelpers.Body<Instruction>(context);
            if (instruction == null || string.IsNullOrWhiteSpace(instruction.Name))
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "An instruction name is required.").ConfigureAwait(false);
                return;
            }

            instruction.TenantId = tenantId;
            Instruction created = await _Database.Instructions.CreateAsync(instruction, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 201, created).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            string tenantId = RouteHelpers.Param(context, "tenantId") ?? string.Empty;
            if (!_Authorization.CanAccessTenant(ctx, tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            string instructionId = RouteHelpers.Param(context, "instructionId") ?? string.Empty;
            Instruction? instruction = await _Database.Instructions.ReadAsync(tenantId, instructionId, context.Token).ConfigureAwait(false);
            if (instruction == null)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Instruction not found.").ConfigureAwait(false);
                return;
            }

            await RouteHelpers.JsonAsync(context, 200, instruction).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            string tenantId = RouteHelpers.Param(context, "tenantId") ?? string.Empty;
            if (!_Authorization.CanAdministerTenant(ctx, tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted to manage instructions for this tenant.").ConfigureAwait(false);
                return;
            }

            string instructionId = RouteHelpers.Param(context, "instructionId") ?? string.Empty;
            Instruction? existing = await _Database.Instructions.ReadAsync(tenantId, instructionId, context.Token).ConfigureAwait(false);
            if (existing == null)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Instruction not found.").ConfigureAwait(false);
                return;
            }

            Instruction? update = RouteHelpers.Body<Instruction>(context);
            if (update == null || string.IsNullOrWhiteSpace(update.Name))
            {
                await RouteHelpers.ErrorAsync(context, 400, "BadRequest", "An instruction name is required.").ConfigureAwait(false);
                return;
            }

            update.Id = instructionId;
            update.TenantId = tenantId;
            update.CreatedUtc = existing.CreatedUtc;
            Instruction saved = await _Database.Instructions.UpdateAsync(update, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, saved).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            string tenantId = RouteHelpers.Param(context, "tenantId") ?? string.Empty;
            if (!_Authorization.CanAdministerTenant(ctx, tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted to manage instructions for this tenant.").ConfigureAwait(false);
                return;
            }

            string instructionId = RouteHelpers.Param(context, "instructionId") ?? string.Empty;
            bool deleted = await _Database.Instructions.DeleteAsync(tenantId, instructionId, context.Token).ConfigureAwait(false);
            if (!deleted)
            {
                await RouteHelpers.ErrorAsync(context, 404, "NotFound", "Instruction not found.").ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = 204;
            await context.Response.Send().ConfigureAwait(false);
        }

        private async Task BatchGetAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            string tenantId = RouteHelpers.Param(context, "tenantId") ?? string.Empty;
            if (!_Authorization.CanAccessTenant(ctx, tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted for this tenant.").ConfigureAwait(false);
                return;
            }

            BatchIdsRequest? request = RouteHelpers.Body<BatchIdsRequest>(context);
            List<Instruction> objects = new List<Instruction>();
            if (request != null && request.Ids != null && request.Ids.Count > 0)
            {
                objects = await _Database.Instructions.ReadManyAsync(tenantId, request.Ids, context.Token).ConfigureAwait(false);
            }

            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["objects"] = objects;
            await RouteHelpers.JsonAsync(context, 200, body).ConfigureAwait(false);
        }

        private async Task BatchCreateAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            string tenantId = RouteHelpers.Param(context, "tenantId") ?? string.Empty;
            if (!_Authorization.CanAdministerTenant(ctx, tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted to manage instructions for this tenant.").ConfigureAwait(false);
                return;
            }

            BatchInstructionRequest? request = RouteHelpers.Body<BatchInstructionRequest>(context);
            List<Instruction> objects = new List<Instruction>();
            if (request != null && request.Items != null && request.Items.Count > 0)
            {
                foreach (Instruction item in request.Items) item.TenantId = tenantId;
                objects = await _Database.Instructions.CreateManyAsync(request.Items, context.Token).ConfigureAwait(false);
            }

            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["objects"] = objects;
            await RouteHelpers.JsonAsync(context, 201, body).ConfigureAwait(false);
        }

        private async Task BatchDeleteAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            string tenantId = RouteHelpers.Param(context, "tenantId") ?? string.Empty;
            if (!_Authorization.CanAdministerTenant(ctx, tenantId))
            {
                await RouteHelpers.ErrorAsync(context, 403, "Forbidden", "Not permitted to manage instructions for this tenant.").ConfigureAwait(false);
                return;
            }

            BatchIdsRequest? request = RouteHelpers.Body<BatchIdsRequest>(context);
            int deleted = 0;
            if (request != null && request.Ids != null && request.Ids.Count > 0)
            {
                deleted = await _Database.Instructions.DeleteManyAsync(tenantId, request.Ids, context.Token).ConfigureAwait(false);
            }

            Dictionary<string, object?> body = new Dictionary<string, object?>();
            body["deleted"] = deleted;
            await RouteHelpers.JsonAsync(context, 200, body).ConfigureAwait(false);
        }

        #endregion
    }
}
