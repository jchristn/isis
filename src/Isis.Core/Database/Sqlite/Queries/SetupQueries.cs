namespace Isis.Core.Database.Sqlite.Queries
{
    /// <summary>
    /// SQLite schema setup statements for Isis.
    /// </summary>
    internal static class SetupQueries
    {
        #region Internal-Members

        /// <summary>
        /// The timestamp format used to persist UTC timestamps as text.
        /// </summary>
        internal static readonly string TimestampFormat = "yyyy-MM-dd HH:mm:ss.ffffffZ";

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Get the CREATE TABLE statements.
        /// </summary>
        /// <returns>The combined DDL string.</returns>
        internal static string CreateTables()
        {
            return @"
CREATE TABLE IF NOT EXISTS schemamigrations (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    appliedutc TEXT NOT NULL,
    success INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS tenants (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    active INTEGER NOT NULL DEFAULT 1,
    isprotected INTEGER NOT NULL DEFAULT 0,
    createdutc TEXT NOT NULL,
    lastupdateutc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS users (
    id TEXT PRIMARY KEY,
    tenantid TEXT NOT NULL,
    firstname TEXT,
    lastname TEXT,
    email TEXT NOT NULL,
    passwordsha256 TEXT,
    isadmin INTEGER NOT NULL DEFAULT 0,
    istenantadmin INTEGER NOT NULL DEFAULT 0,
    active INTEGER NOT NULL DEFAULT 1,
    isprotected INTEGER NOT NULL DEFAULT 0,
    createdutc TEXT NOT NULL,
    lastupdateutc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS credentials (
    id TEXT PRIMARY KEY,
    tenantid TEXT NOT NULL,
    userid TEXT NOT NULL,
    name TEXT,
    accesskey TEXT NOT NULL,
    secretkey TEXT,
    authmode TEXT NOT NULL DEFAULT 'DirectHeader',
    active INTEGER NOT NULL DEFAULT 1,
    isprotected INTEGER NOT NULL DEFAULT 0,
    createdutc TEXT NOT NULL,
    lastupdateutc TEXT NOT NULL,
    lastusedutc TEXT,
    expirationutc TEXT
);

CREATE TABLE IF NOT EXISTS authsessions (
    id TEXT PRIMARY KEY,
    tenantid TEXT NOT NULL,
    userid TEXT,
    credentialid TEXT,
    principaltype TEXT NOT NULL DEFAULT 'User',
    authscheme TEXT NOT NULL DEFAULT 'BearerToken',
    token TEXT NOT NULL,
    sourceip TEXT,
    useragent TEXT,
    issuedutc TEXT NOT NULL,
    expirationutc TEXT NOT NULL,
    lastusedutc TEXT,
    revokedutc TEXT,
    revocationreason TEXT,
    active INTEGER NOT NULL DEFAULT 1,
    createdutc TEXT NOT NULL,
    lastupdateutc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS scopes (
    id TEXT PRIMARY KEY,
    tenantid TEXT NOT NULL,
    name TEXT NOT NULL,
    description TEXT,
    storeprovider TEXT NOT NULL DEFAULT 'RecallDb',
    recallcollectionid TEXT,
    dimensionality INTEGER NOT NULL DEFAULT 0,
    embeddingendpointid TEXT,
    filesystemlayout TEXT NOT NULL DEFAULT 'Hierarchy',
    targetpath TEXT,
    chunkingmode TEXT NOT NULL DEFAULT 'OnOverflow',
    chunkstrategy TEXT NOT NULL DEFAULT 'FixedTokenCount',
    chunkmaxtokens INTEGER NOT NULL DEFAULT 0,
    chunkoverlaptokens INTEGER NOT NULL DEFAULT 64,
    active INTEGER NOT NULL DEFAULT 1,
    createdutc TEXT NOT NULL,
    lastupdateutc TEXT NOT NULL,
    rerankendpointid TEXT,
    rerankcandidates INTEGER NOT NULL DEFAULT 10,
    rerankminscore REAL
);

CREATE TABLE IF NOT EXISTS categories (
    id TEXT PRIMARY KEY,
    tenantid TEXT NOT NULL,
    scopeid TEXT NOT NULL,
    name TEXT NOT NULL,
    description TEXT,
    instructions TEXT,
    active INTEGER NOT NULL DEFAULT 1,
    createdutc TEXT NOT NULL,
    lastupdateutc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS memories (
    id TEXT PRIMARY KEY,
    tenantid TEXT NOT NULL,
    scopeid TEXT NOT NULL,
    categoryid TEXT NOT NULL,
    slug TEXT NOT NULL,
    storekey TEXT,
    title TEXT,
    type TEXT NOT NULL DEFAULT 'Project',
    summary TEXT,
    resource TEXT,
    body TEXT NOT NULL DEFAULT '',
    tags TEXT NOT NULL DEFAULT '[]',
    links TEXT NOT NULL DEFAULT '[]',
    metadata TEXT NOT NULL DEFAULT '{}',
    salience REAL NOT NULL DEFAULT 0.5,
    author TEXT,
    sessionid TEXT,
    model TEXT,
    version INTEGER NOT NULL DEFAULT 1,
    createdutc TEXT NOT NULL,
    lastupdateutc TEXT NOT NULL,
    lastaccessedutc TEXT,
    supersedes TEXT,
    supersededby TEXT
);

CREATE TABLE IF NOT EXISTS model_endpoints (
    id TEXT PRIMARY KEY,
    tenantid TEXT NOT NULL,
    name TEXT NOT NULL,
    kind TEXT NOT NULL DEFAULT 'Embedding',
    apiformat TEXT NOT NULL DEFAULT 'OpenAI',
    maxinputtokens INTEGER NOT NULL DEFAULT 0,
    baseurl TEXT NOT NULL DEFAULT '',
    authtype TEXT NOT NULL DEFAULT 'None',
    authheadername TEXT,
    authsecretheadername TEXT,
    authqueryparam TEXT,
    authkeyid TEXT,
    authsecret TEXT,
    model TEXT,
    dimensionality INTEGER NOT NULL DEFAULT 0,
    timeoutms INTEGER NOT NULL DEFAULT 60000,
    active INTEGER NOT NULL DEFAULT 1,
    healthcheckurl TEXT NOT NULL DEFAULT '/',
    healthcheckmethod TEXT NOT NULL DEFAULT 'GET',
    healthcheckintervalms INTEGER NOT NULL DEFAULT 5000,
    healthchecktimeoutms INTEGER NOT NULL DEFAULT 5000,
    healthcheckexpectedstatuscode INTEGER NOT NULL DEFAULT 200,
    healthythreshold INTEGER NOT NULL DEFAULT 2,
    unhealthythreshold INTEGER NOT NULL DEFAULT 2,
    healthcheckuseauth INTEGER NOT NULL DEFAULT 0,
    createdutc TEXT NOT NULL,
    lastupdateutc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS request_history (
    id TEXT PRIMARY KEY,
    tenantid TEXT,
    method TEXT NOT NULL,
    path TEXT NOT NULL,
    statuscode INTEGER NOT NULL DEFAULT 0,
    sourceip TEXT,
    principalname TEXT,
    requestheaders TEXT,
    requestbody TEXT,
    responseheaders TEXT,
    responsebody TEXT,
    durationms REAL NOT NULL DEFAULT 0,
    createdutc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS operation_events (
    id TEXT PRIMARY KEY,
    tenantid TEXT,
    resourcetype TEXT NOT NULL,
    operation TEXT NOT NULL,
    resourceid TEXT,
    scopeid TEXT,
    method TEXT NOT NULL,
    path TEXT NOT NULL,
    statuscode INTEGER NOT NULL DEFAULT 0,
    principalname TEXT,
    sourceip TEXT,
    durationms REAL NOT NULL DEFAULT 0,
    createdutc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS permissions (
    id TEXT PRIMARY KEY,
    tenantid TEXT NOT NULL,
    userid TEXT NOT NULL,
    resourcetype TEXT NOT NULL DEFAULT 'All',
    operation TEXT NOT NULL DEFAULT 'All',
    permissiontype TEXT NOT NULL DEFAULT 'Permit',
    resourceid TEXT,
    active INTEGER NOT NULL DEFAULT 1,
    createdutc TEXT NOT NULL,
    lastupdateutc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS instructions (
    id TEXT PRIMARY KEY,
    tenantid TEXT NOT NULL,
    scopeid TEXT,
    name TEXT NOT NULL,
    content TEXT NOT NULL DEFAULT '',
    mergemode TEXT NOT NULL DEFAULT 'Append',
    position INTEGER NOT NULL DEFAULT 0,
    active INTEGER NOT NULL DEFAULT 1,
    isprotected INTEGER NOT NULL DEFAULT 0,
    createdutc TEXT NOT NULL,
    lastupdateutc TEXT NOT NULL
);
";
        }

        /// <summary>
        /// Get the CREATE INDEX statements.
        /// </summary>
        /// <returns>The combined DDL string.</returns>
        internal static string CreateIndices()
        {
            return @"
CREATE INDEX IF NOT EXISTS idx_tenants_name ON tenants(name);
CREATE INDEX IF NOT EXISTS idx_users_tenantid ON users(tenantid);
CREATE UNIQUE INDEX IF NOT EXISTS idx_users_tenantid_email ON users(tenantid, email);
CREATE INDEX IF NOT EXISTS idx_credentials_tenantid ON credentials(tenantid);
CREATE UNIQUE INDEX IF NOT EXISTS idx_credentials_accesskey ON credentials(accesskey);
CREATE UNIQUE INDEX IF NOT EXISTS idx_sessions_token ON authsessions(token);
CREATE INDEX IF NOT EXISTS idx_sessions_tenantid ON authsessions(tenantid);
CREATE UNIQUE INDEX IF NOT EXISTS idx_scopes_tenantid_name ON scopes(tenantid, name);
CREATE INDEX IF NOT EXISTS idx_categories_scopeid ON categories(scopeid);
CREATE UNIQUE INDEX IF NOT EXISTS idx_categories_tenant_scope_name ON categories(tenantid, scopeid, name);
CREATE INDEX IF NOT EXISTS idx_memories_scope_category ON memories(scopeid, categoryid);
CREATE UNIQUE INDEX IF NOT EXISTS idx_memories_scope_category_slug ON memories(scopeid, categoryid, slug);
CREATE INDEX IF NOT EXISTS idx_endpoints_tenant_kind ON model_endpoints(tenantid, kind);
CREATE INDEX IF NOT EXISTS idx_reqhistory_tenant_created ON request_history(tenantid, createdutc);
CREATE INDEX IF NOT EXISTS idx_opevents_tenant_created ON operation_events(tenantid, createdutc);
CREATE INDEX IF NOT EXISTS idx_opevents_resource_created ON operation_events(resourcetype, createdutc);
CREATE INDEX IF NOT EXISTS idx_permissions_tenant_user ON permissions(tenantid, userid);
CREATE INDEX IF NOT EXISTS idx_instructions_tenantid ON instructions(tenantid, scopeid, position);
";
        }

        #endregion
    }
}
