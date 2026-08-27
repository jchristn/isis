namespace Isis.Core.Database.Mysql.Queries
{
    /// <summary>
    /// MySQL schema setup statements for Isis. Identifiers use bounded VARCHAR (MySQL cannot key TEXT
    /// columns), indexes are declared inline, and booleans/timestamps are stored the same portable way as
    /// the other providers (integer flags and text timestamps).
    /// </summary>
    internal static class MysqlSetupQueries
    {
        #region Internal-Methods

        /// <summary>
        /// Get the CREATE TABLE statements.
        /// </summary>
        /// <returns>The combined DDL string.</returns>
        internal static string CreateTables()
        {
            return @"
CREATE TABLE IF NOT EXISTS schemamigrations (
    id VARCHAR(64) PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    appliedutc VARCHAR(40) NOT NULL,
    success INT NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS tenants (
    id VARCHAR(64) PRIMARY KEY,
    name VARCHAR(255) NOT NULL,
    active INT NOT NULL DEFAULT 1,
    isprotected INT NOT NULL DEFAULT 0,
    createdutc VARCHAR(40) NOT NULL,
    lastupdateutc VARCHAR(40) NOT NULL,
    INDEX idx_tenants_name (name)
);

CREATE TABLE IF NOT EXISTS users (
    id VARCHAR(64) PRIMARY KEY,
    tenantid VARCHAR(64) NOT NULL,
    firstname VARCHAR(255),
    lastname VARCHAR(255),
    email VARCHAR(255) NOT NULL,
    passwordsha256 VARCHAR(128),
    isadmin INT NOT NULL DEFAULT 0,
    istenantadmin INT NOT NULL DEFAULT 0,
    active INT NOT NULL DEFAULT 1,
    isprotected INT NOT NULL DEFAULT 0,
    createdutc VARCHAR(40) NOT NULL,
    lastupdateutc VARCHAR(40) NOT NULL,
    INDEX idx_users_tenantid (tenantid),
    UNIQUE KEY uk_users_tenant_email (tenantid, email)
);

CREATE TABLE IF NOT EXISTS credentials (
    id VARCHAR(64) PRIMARY KEY,
    tenantid VARCHAR(64) NOT NULL,
    userid VARCHAR(64) NOT NULL,
    name VARCHAR(255),
    accesskey VARCHAR(128) NOT NULL,
    secretkey VARCHAR(255),
    authmode VARCHAR(32) NOT NULL DEFAULT 'DirectHeader',
    active INT NOT NULL DEFAULT 1,
    isprotected INT NOT NULL DEFAULT 0,
    createdutc VARCHAR(40) NOT NULL,
    lastupdateutc VARCHAR(40) NOT NULL,
    lastusedutc VARCHAR(40),
    expirationutc VARCHAR(40),
    INDEX idx_credentials_tenantid (tenantid),
    UNIQUE KEY uk_credentials_accesskey (accesskey)
);

CREATE TABLE IF NOT EXISTS authsessions (
    id VARCHAR(64) PRIMARY KEY,
    tenantid VARCHAR(64) NOT NULL,
    userid VARCHAR(64),
    credentialid VARCHAR(64),
    principaltype VARCHAR(32) NOT NULL DEFAULT 'User',
    authscheme VARCHAR(32) NOT NULL DEFAULT 'BearerToken',
    token VARCHAR(128) NOT NULL,
    sourceip VARCHAR(64),
    useragent VARCHAR(512),
    issuedutc VARCHAR(40) NOT NULL,
    expirationutc VARCHAR(40) NOT NULL,
    lastusedutc VARCHAR(40),
    revokedutc VARCHAR(40),
    revocationreason VARCHAR(512),
    active INT NOT NULL DEFAULT 1,
    createdutc VARCHAR(40) NOT NULL,
    lastupdateutc VARCHAR(40) NOT NULL,
    INDEX idx_sessions_tenantid (tenantid),
    UNIQUE KEY uk_sessions_token (token)
);

CREATE TABLE IF NOT EXISTS scopes (
    id VARCHAR(64) PRIMARY KEY,
    tenantid VARCHAR(64) NOT NULL,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    storeprovider VARCHAR(32) NOT NULL DEFAULT 'RecallDb',
    recallcollectionid VARCHAR(128),
    dimensionality INT NOT NULL DEFAULT 0,
    embeddingendpointid VARCHAR(64),
    filesystemlayout VARCHAR(32) NOT NULL DEFAULT 'Hierarchy',
    targetpath VARCHAR(1024),
    active INT NOT NULL DEFAULT 1,
    createdutc VARCHAR(40) NOT NULL,
    lastupdateutc VARCHAR(40) NOT NULL,
    UNIQUE KEY uk_scopes_tenant_name (tenantid, name)
);

CREATE TABLE IF NOT EXISTS categories (
    id VARCHAR(64) PRIMARY KEY,
    tenantid VARCHAR(64) NOT NULL,
    scopeid VARCHAR(64) NOT NULL,
    name VARCHAR(255) NOT NULL,
    description TEXT,
    instructions TEXT,
    active INT NOT NULL DEFAULT 1,
    createdutc VARCHAR(40) NOT NULL,
    lastupdateutc VARCHAR(40) NOT NULL,
    INDEX idx_categories_scopeid (scopeid),
    UNIQUE KEY uk_categories_tenant_scope_name (tenantid, scopeid, name)
);

CREATE TABLE IF NOT EXISTS memories (
    id VARCHAR(64) PRIMARY KEY,
    tenantid VARCHAR(64) NOT NULL,
    scopeid VARCHAR(64) NOT NULL,
    categoryid VARCHAR(64) NOT NULL,
    slug VARCHAR(255) NOT NULL,
    storekey VARCHAR(512),
    title VARCHAR(512),
    type VARCHAR(32) NOT NULL DEFAULT 'Project',
    summary TEXT,
    body LONGTEXT NOT NULL,
    tags LONGTEXT NOT NULL,
    links LONGTEXT NOT NULL,
    metadata LONGTEXT NOT NULL,
    salience DOUBLE NOT NULL DEFAULT 0.5,
    author VARCHAR(64),
    sessionid VARCHAR(64),
    model VARCHAR(128),
    version INT NOT NULL DEFAULT 1,
    createdutc VARCHAR(40) NOT NULL,
    lastupdateutc VARCHAR(40) NOT NULL,
    lastaccessedutc VARCHAR(40),
    INDEX idx_memories_scope_category (scopeid, categoryid),
    UNIQUE KEY uk_memories_scope_category_slug (scopeid, categoryid, slug)
);

CREATE TABLE IF NOT EXISTS model_endpoints (
    id VARCHAR(64) PRIMARY KEY,
    tenantid VARCHAR(64) NOT NULL,
    name VARCHAR(255) NOT NULL,
    kind VARCHAR(32) NOT NULL DEFAULT 'Embedding',
    apiformat VARCHAR(32) NOT NULL DEFAULT 'OpenAI',
    hostname VARCHAR(255) NOT NULL,
    port INT NOT NULL DEFAULT 0,
    usessl INT NOT NULL DEFAULT 0,
    apikey VARCHAR(512),
    model VARCHAR(255),
    dimensionality INT NOT NULL DEFAULT 0,
    timeoutms INT NOT NULL DEFAULT 60000,
    active INT NOT NULL DEFAULT 1,
    healthcheckurl VARCHAR(512) NOT NULL DEFAULT '/',
    healthcheckmethod VARCHAR(16) NOT NULL DEFAULT 'GET',
    healthcheckintervalms INT NOT NULL DEFAULT 5000,
    healthchecktimeoutms INT NOT NULL DEFAULT 5000,
    healthcheckexpectedstatuscode INT NOT NULL DEFAULT 200,
    healthythreshold INT NOT NULL DEFAULT 2,
    unhealthythreshold INT NOT NULL DEFAULT 2,
    healthcheckuseauth INT NOT NULL DEFAULT 0,
    createdutc VARCHAR(40) NOT NULL,
    lastupdateutc VARCHAR(40) NOT NULL,
    INDEX idx_endpoints_tenant_kind (tenantid, kind)
);

CREATE TABLE IF NOT EXISTS request_history (
    id VARCHAR(64) PRIMARY KEY,
    tenantid VARCHAR(64),
    method VARCHAR(16) NOT NULL,
    path VARCHAR(2048) NOT NULL,
    statuscode INT NOT NULL DEFAULT 0,
    sourceip VARCHAR(64),
    principalname VARCHAR(255),
    durationms DOUBLE NOT NULL DEFAULT 0,
    createdutc VARCHAR(40) NOT NULL,
    INDEX idx_reqhistory_tenant_created (tenantid, createdutc)
);

CREATE TABLE IF NOT EXISTS permissions (
    id VARCHAR(64) PRIMARY KEY,
    tenantid VARCHAR(64) NOT NULL,
    userid VARCHAR(64) NOT NULL,
    resourcetype VARCHAR(64) NOT NULL DEFAULT 'All',
    operation VARCHAR(32) NOT NULL DEFAULT 'All',
    permissiontype VARCHAR(16) NOT NULL DEFAULT 'Permit',
    resourceid VARCHAR(64),
    active INT NOT NULL DEFAULT 1,
    createdutc VARCHAR(40) NOT NULL,
    lastupdateutc VARCHAR(40) NOT NULL,
    INDEX idx_permissions_tenant_user (tenantid, userid)
);
";
        }

        #endregion
    }
}
