namespace Isis.Core.Database.SqlServer.Queries
{
    /// <summary>
    /// SQL Server schema setup statements for Isis. Each table is created only when absent (SQL Server has
    /// no CREATE TABLE IF NOT EXISTS), indexes are declared inline, and booleans/timestamps are stored the
    /// same portable way as the other providers (integer flags and text timestamps).
    /// </summary>
    internal static class SqlServerSetupQueries
    {
        #region Internal-Methods

        /// <summary>
        /// Get the CREATE TABLE statements.
        /// </summary>
        /// <returns>The combined DDL string.</returns>
        internal static string CreateTables()
        {
            return @"
IF OBJECT_ID(N'dbo.schemamigrations', N'U') IS NULL CREATE TABLE schemamigrations (
    id NVARCHAR(64) NOT NULL PRIMARY KEY,
    name NVARCHAR(255) NOT NULL,
    appliedutc NVARCHAR(40) NOT NULL,
    success INT NOT NULL DEFAULT 1
);

IF OBJECT_ID(N'dbo.tenants', N'U') IS NULL CREATE TABLE tenants (
    id NVARCHAR(64) NOT NULL PRIMARY KEY,
    name NVARCHAR(255) NOT NULL,
    active INT NOT NULL DEFAULT 1,
    isprotected INT NOT NULL DEFAULT 0,
    createdutc NVARCHAR(40) NOT NULL,
    lastupdateutc NVARCHAR(40) NOT NULL,
    INDEX idx_tenants_name (name)
);

IF OBJECT_ID(N'dbo.users', N'U') IS NULL CREATE TABLE users (
    id NVARCHAR(64) NOT NULL PRIMARY KEY,
    tenantid NVARCHAR(64) NOT NULL,
    firstname NVARCHAR(255),
    lastname NVARCHAR(255),
    email NVARCHAR(255) NOT NULL,
    passwordsha256 NVARCHAR(128),
    isadmin INT NOT NULL DEFAULT 0,
    istenantadmin INT NOT NULL DEFAULT 0,
    active INT NOT NULL DEFAULT 1,
    isprotected INT NOT NULL DEFAULT 0,
    createdutc NVARCHAR(40) NOT NULL,
    lastupdateutc NVARCHAR(40) NOT NULL,
    INDEX idx_users_tenantid (tenantid),
    INDEX uk_users_tenant_email UNIQUE (tenantid, email)
);

IF OBJECT_ID(N'dbo.credentials', N'U') IS NULL CREATE TABLE credentials (
    id NVARCHAR(64) NOT NULL PRIMARY KEY,
    tenantid NVARCHAR(64) NOT NULL,
    userid NVARCHAR(64) NOT NULL,
    name NVARCHAR(255),
    accesskey NVARCHAR(128) NOT NULL,
    secretkey NVARCHAR(255),
    authmode NVARCHAR(32) NOT NULL DEFAULT 'DirectHeader',
    active INT NOT NULL DEFAULT 1,
    isprotected INT NOT NULL DEFAULT 0,
    createdutc NVARCHAR(40) NOT NULL,
    lastupdateutc NVARCHAR(40) NOT NULL,
    lastusedutc NVARCHAR(40),
    expirationutc NVARCHAR(40),
    INDEX idx_credentials_tenantid (tenantid),
    INDEX uk_credentials_accesskey UNIQUE (accesskey)
);

IF OBJECT_ID(N'dbo.authsessions', N'U') IS NULL CREATE TABLE authsessions (
    id NVARCHAR(64) NOT NULL PRIMARY KEY,
    tenantid NVARCHAR(64) NOT NULL,
    userid NVARCHAR(64),
    credentialid NVARCHAR(64),
    principaltype NVARCHAR(32) NOT NULL DEFAULT 'User',
    authscheme NVARCHAR(32) NOT NULL DEFAULT 'BearerToken',
    token NVARCHAR(128) NOT NULL,
    sourceip NVARCHAR(64),
    useragent NVARCHAR(512),
    issuedutc NVARCHAR(40) NOT NULL,
    expirationutc NVARCHAR(40) NOT NULL,
    lastusedutc NVARCHAR(40),
    revokedutc NVARCHAR(40),
    revocationreason NVARCHAR(512),
    active INT NOT NULL DEFAULT 1,
    createdutc NVARCHAR(40) NOT NULL,
    lastupdateutc NVARCHAR(40) NOT NULL,
    INDEX idx_sessions_tenantid (tenantid),
    INDEX uk_sessions_token UNIQUE (token)
);

IF OBJECT_ID(N'dbo.scopes', N'U') IS NULL CREATE TABLE scopes (
    id NVARCHAR(64) NOT NULL PRIMARY KEY,
    tenantid NVARCHAR(64) NOT NULL,
    name NVARCHAR(255) NOT NULL,
    description NVARCHAR(MAX),
    storeprovider NVARCHAR(32) NOT NULL DEFAULT 'RecallDb',
    recallcollectionid NVARCHAR(128),
    dimensionality INT NOT NULL DEFAULT 0,
    embeddingendpointid NVARCHAR(64),
    filesystemlayout NVARCHAR(32) NOT NULL DEFAULT 'Hierarchy',
    targetpath NVARCHAR(1024),
    active INT NOT NULL DEFAULT 1,
    createdutc NVARCHAR(40) NOT NULL,
    lastupdateutc NVARCHAR(40) NOT NULL,
    INDEX uk_scopes_tenant_name UNIQUE (tenantid, name)
);

IF OBJECT_ID(N'dbo.categories', N'U') IS NULL CREATE TABLE categories (
    id NVARCHAR(64) NOT NULL PRIMARY KEY,
    tenantid NVARCHAR(64) NOT NULL,
    scopeid NVARCHAR(64) NOT NULL,
    name NVARCHAR(255) NOT NULL,
    description NVARCHAR(MAX),
    instructions NVARCHAR(MAX),
    active INT NOT NULL DEFAULT 1,
    createdutc NVARCHAR(40) NOT NULL,
    lastupdateutc NVARCHAR(40) NOT NULL,
    INDEX idx_categories_scopeid (scopeid),
    INDEX uk_categories_tenant_scope_name UNIQUE (tenantid, scopeid, name)
);

IF OBJECT_ID(N'dbo.memories', N'U') IS NULL CREATE TABLE memories (
    id NVARCHAR(64) NOT NULL PRIMARY KEY,
    tenantid NVARCHAR(64) NOT NULL,
    scopeid NVARCHAR(64) NOT NULL,
    categoryid NVARCHAR(64) NOT NULL,
    slug NVARCHAR(255) NOT NULL,
    storekey NVARCHAR(512),
    title NVARCHAR(512),
    type NVARCHAR(32) NOT NULL DEFAULT 'Project',
    summary NVARCHAR(MAX),
    body NVARCHAR(MAX) NOT NULL,
    tags NVARCHAR(MAX) NOT NULL,
    links NVARCHAR(MAX) NOT NULL,
    metadata NVARCHAR(MAX) NOT NULL,
    salience FLOAT NOT NULL DEFAULT 0.5,
    author NVARCHAR(64),
    sessionid NVARCHAR(64),
    model NVARCHAR(128),
    version INT NOT NULL DEFAULT 1,
    createdutc NVARCHAR(40) NOT NULL,
    lastupdateutc NVARCHAR(40) NOT NULL,
    lastaccessedutc NVARCHAR(40),
    INDEX idx_memories_scope_category (scopeid, categoryid),
    INDEX uk_memories_scope_category_slug UNIQUE (scopeid, categoryid, slug)
);

IF OBJECT_ID(N'dbo.model_endpoints', N'U') IS NULL CREATE TABLE model_endpoints (
    id NVARCHAR(64) NOT NULL PRIMARY KEY,
    tenantid NVARCHAR(64) NOT NULL,
    name NVARCHAR(255) NOT NULL,
    kind NVARCHAR(32) NOT NULL DEFAULT 'Embedding',
    apiformat NVARCHAR(32) NOT NULL DEFAULT 'OpenAI',
    hostname NVARCHAR(255) NOT NULL,
    port INT NOT NULL DEFAULT 0,
    usessl INT NOT NULL DEFAULT 0,
    apikey NVARCHAR(512),
    model NVARCHAR(255),
    dimensionality INT NOT NULL DEFAULT 0,
    timeoutms INT NOT NULL DEFAULT 60000,
    active INT NOT NULL DEFAULT 1,
    healthcheckurl NVARCHAR(512) NOT NULL DEFAULT '/',
    healthcheckmethod NVARCHAR(16) NOT NULL DEFAULT 'GET',
    healthcheckintervalms INT NOT NULL DEFAULT 5000,
    healthchecktimeoutms INT NOT NULL DEFAULT 5000,
    healthcheckexpectedstatuscode INT NOT NULL DEFAULT 200,
    healthythreshold INT NOT NULL DEFAULT 2,
    unhealthythreshold INT NOT NULL DEFAULT 2,
    healthcheckuseauth INT NOT NULL DEFAULT 0,
    createdutc NVARCHAR(40) NOT NULL,
    lastupdateutc NVARCHAR(40) NOT NULL,
    INDEX idx_endpoints_tenant_kind (tenantid, kind)
);

IF OBJECT_ID(N'dbo.request_history', N'U') IS NULL CREATE TABLE request_history (
    id NVARCHAR(64) NOT NULL PRIMARY KEY,
    tenantid NVARCHAR(64),
    method NVARCHAR(16) NOT NULL,
    path NVARCHAR(2048) NOT NULL,
    statuscode INT NOT NULL DEFAULT 0,
    sourceip NVARCHAR(64),
    principalname NVARCHAR(255),
    requestheaders NVARCHAR(MAX),
    requestbody NVARCHAR(MAX),
    responseheaders NVARCHAR(MAX),
    responsebody NVARCHAR(MAX),
    durationms FLOAT NOT NULL DEFAULT 0,
    createdutc NVARCHAR(40) NOT NULL,
    INDEX idx_reqhistory_tenant_created (tenantid, createdutc)
);

IF OBJECT_ID(N'dbo.permissions', N'U') IS NULL CREATE TABLE permissions (
    id NVARCHAR(64) NOT NULL PRIMARY KEY,
    tenantid NVARCHAR(64) NOT NULL,
    userid NVARCHAR(64) NOT NULL,
    resourcetype NVARCHAR(64) NOT NULL DEFAULT 'All',
    operation NVARCHAR(32) NOT NULL DEFAULT 'All',
    permissiontype NVARCHAR(16) NOT NULL DEFAULT 'Permit',
    resourceid NVARCHAR(64),
    active INT NOT NULL DEFAULT 1,
    createdutc NVARCHAR(40) NOT NULL,
    lastupdateutc NVARCHAR(40) NOT NULL,
    INDEX idx_permissions_tenant_user (tenantid, userid)
);

IF OBJECT_ID(N'dbo.instructions', N'U') IS NULL CREATE TABLE instructions (
    id NVARCHAR(64) NOT NULL PRIMARY KEY,
    tenantid NVARCHAR(64) NOT NULL,
    name NVARCHAR(255) NOT NULL,
    content NVARCHAR(MAX) NOT NULL,
    position INT NOT NULL DEFAULT 0,
    active INT NOT NULL DEFAULT 1,
    isprotected INT NOT NULL DEFAULT 0,
    createdutc NVARCHAR(40) NOT NULL,
    lastupdateutc NVARCHAR(40) NOT NULL,
    INDEX idx_instructions_tenantid (tenantid, position)
);
";
        }

        #endregion
    }
}
