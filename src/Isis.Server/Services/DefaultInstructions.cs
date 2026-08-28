namespace Isis.Server.Services
{
    using System.Collections.Generic;
    using Isis.Core.Models;

    /// <summary>
    /// The default instruction set (the agent "tool manual") provisioned for every tenant — the first-run
    /// default tenant and any tenant created through the API. These records are tenant-owned and fully
    /// editable in the dashboard (Memory → Instructions); tenants are expected to customise the category and
    /// memory guidance for their domain. They are surfaced to agents over MCP via <c>isis_instructions</c>,
    /// in ascending position order.
    /// </summary>
    public static class DefaultInstructions
    {
        #region Public-Methods

        /// <summary>
        /// Build the default instruction records (tool manual) for a tenant, in ascending position order.
        /// </summary>
        /// <param name="tenantId">The owning tenant identifier.</param>
        /// <returns>The default instructions.</returns>
        public static List<Instruction> For(string tenantId)
        {
            return new List<Instruction>
            {
                new Instruction
                {
                    TenantId = tenantId,
                    Name = "Start here",
                    Position = 0,
                    Content =
@"This is your operating manual for this tenant's memory. It is maintained by the tenant and may be
edited, so re-read it (isis_instructions) whenever you begin work.

First steps, in order:
1. isis_whoami — confirm your tenantId and principal.
2. isis_instructions — read this manual (you are here).
3. isis_scope_enumerate — find the scope for your project; if none fits, create one with isis_scope_create.
4. isis_guide — for the chosen scope, read its categories and their per-category instructions before writing.

Authentication: every call is authenticated with a tenant credential ACCESS KEY, sent as a bearer token
(Authorization: Bearer <accessKey>; the x-access-key header is also accepted). The access key is public and
transferable; the secret key never leaves your MCP client. Calls without an access key are rejected."
                },
                new Instruction
                {
                    TenantId = tenantId,
                    Name = "Tools",
                    Position = 1,
                    Content =
@"Your MCP client also receives each tool's full input schema from the server; this is a quick reference.
- isis_whoami — resolve tenantId and principal.
- isis_instructions(tenantId) — read this manual.
- isis_scope_enumerate(tenantId) — list scopes. isis_scope_create(tenantId, name, [description, storeProvider,
  embeddingEndpointId, dimensionality, filesystemLayout, targetPath]) — create a project scope if missing. The
  default store (RecallDb) auto-selects the tenant's embedding endpoint and its dimensionality; if the tenant
  has none, create a Filesystem or Verbex (keyword-only) scope instead.
- isis_endpoint_enumerate(tenantId, [kind=Embedding|Inference]) — list model endpoints (id, model, dimensionality)
  to choose an embeddingEndpointId, or to confirm whether semantic (RecallDb) scopes are available at all.
- isis_guide(tenantId, scopeId) — a scope's categories, their instructions, and store capabilities. Call first.
- isis_category_enumerate(tenantId, scopeId) / isis_category_create(tenantId, scopeId, name, [description, instructions]).
- isis_memory_upsert(tenantId, scopeId, categoryId, slug, body, [title, summary, type]) — idempotent on (scope, category, slug). 'type' is optional (User|Feedback|Project|Reference; unknown defaults to Project).
- isis_memory_search(tenantId, scopeId, queryText, [mode=Keyword|Semantic|Hybrid, topK, categoryName]) — note: search filters by category NAME; enumerate filters by category id.
- isis_memory_enumerate / isis_memory_read / isis_memory_delete."
                },
                new Instruction
                {
                    TenantId = tenantId,
                    Name = "Memory model",
                    Position = 2,
                    Content =
@"Memory is organised as scopes → categories → memories.
- Scope: a memory space for one project/book/domain, bound to a store: RecallDB (semantic + keyword — needs an
  embedding endpoint; its dimension is fixed at creation to match the embedding model), Verbex (keyword), or
  Filesystem (keyword, git-trackable files). Prefer RecallDB when an embedding endpoint exists (check with
  isis_endpoint_enumerate); otherwise use Verbex or Filesystem, which need no embeddings.
- Category: a labelled bucket within a scope that carries its OWN usage instructions telling you when and how to
  write into it. Always read the category's instructions (via isis_guide) before writing.
- Memory: one atomic note — a stable slug, an optional title, a one-line summary (recall hook), the body, a
  type (User | Feedback | Project | Reference), and optional tags/links. Re-upserting the same slug updates in place."
                },
                new Instruction
                {
                    TenantId = tenantId,
                    Name = "Creating categories (customise for this domain)",
                    Position = 3,
                    Content =
@"Categories are how this tenant shapes what agents record. Edit this section to define the categories that fit
your domain, and give each a clear ""when to use me"" instruction (the instructions field on the category).

Guidance:
- Prefer a small set of durable categories over many ad-hoc ones. Create a category only when an existing one
  does not fit; check with isis_category_enumerate first.
- Each category's instructions should say what belongs in it, the expected body shape, and what to leave out.

Example categories (replace with your own):
- decisions — architectural/product decisions and their rationale. Body: the decision, the alternatives, why.
- conventions — house rules and patterns to follow. Body: the rule, an example, the exception.
- glossary — domain terms and their meaning. Body: term → definition, with a canonical example."
                },
                new Instruction
                {
                    TenantId = tenantId,
                    Name = "Storing memories (customise for this domain)",
                    Position = 4,
                    Content =
@"Edit this section to specify how agents should write memories for your domain.

Defaults:
- One idea per memory. Do not bundle unrelated facts.
- Slugs are stable and descriptive (e.g. auth-token-rotation), lower-case-hyphenated; re-using a slug updates
  that memory rather than creating a duplicate.
- Write a one-line summary that is a genuine recall hook — what you'd search for to find this later.
- Choose the category whose instructions match; if none fits, propose a new category rather than forcing it.
- Record durable facts, decisions, and conventions — not transient chatter or secrets."
                },
                new Instruction
                {
                    TenantId = tenantId,
                    Name = "Recall",
                    Position = 5,
                    Content =
@"Search before writing to avoid duplicates, and search before answering to ground your response.
- On RecallDB scopes prefer Semantic or Hybrid search for meaning-based recall; use Keyword for exact terms.
- Verbex and Filesystem scopes are keyword-only and match literal terms.
- Narrow with the category filter when you know where the answer lives, and keep topK small."
                }
            };
        }

        #endregion
    }
}
