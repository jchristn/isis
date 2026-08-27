namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Isis.Core;
    using Isis.Core.Enums;
    using Isis.Core.Helpers;
    using Isis.Core.Models;
    using Isis.Core.Stores;
    using Isis.McpServer.Settings;
    using Isis.Server.Serialization;
    using Isis.Server.Settings;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite exercising the Isis domain models, identifier generation, JSON serialization,
    /// and settings round trips. Pure in-process assertions with no external dependencies.
    /// </summary>
    public static class ModelSuite
    {
        #region Public-Methods

        /// <summary>
        /// Get the model test suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Suite()
        {
            return new TestSuiteDescriptor(
                "model",
                "Isis Model Suite",
                new List<TestCaseDescriptor>
                {
                    // 1. Validation: null / empty / whitespace setters throw ArgumentNullException.
                    TestCase.Sync("model", "tenant-validation", "Tenant required fields reject blanks", TenantValidation),
                    TestCase.Sync("model", "user-validation", "User required fields reject blanks", UserValidation),
                    TestCase.Sync("model", "credential-validation", "Credential required fields reject blanks", CredentialValidation),
                    TestCase.Sync("model", "authsession-validation", "AuthSession required fields reject blanks", AuthSessionValidation),
                    TestCase.Sync("model", "scope-validation", "Scope required fields reject blanks", ScopeValidation),
                    TestCase.Sync("model", "category-validation", "Category required fields reject blanks", CategoryValidation),
                    TestCase.Sync("model", "memory-validation", "Memory required fields reject blanks", MemoryValidation),
                    TestCase.Sync("model", "memorylink-validation", "MemoryLink required fields reject blanks", MemoryLinkValidation),
                    TestCase.Sync("model", "modelendpoint-validation", "ModelEndpoint required fields reject blanks", ModelEndpointValidation),
                    TestCase.Sync("model", "requesthistory-validation", "RequestHistoryEntry required id rejects blanks", RequestHistoryValidation),

                    // 2. Range setters throw ArgumentOutOfRangeException.
                    TestCase.Sync("model", "modelendpoint-port-range", "ModelEndpoint.Port rejects out-of-range values", ModelEndpointPortRange),
                    TestCase.Sync("model", "scope-dimensionality-range", "Scope.Dimensionality rejects negatives", ScopeDimensionalityRange),
                    TestCase.Sync("model", "modelendpoint-dimensionality-range", "ModelEndpoint.Dimensionality rejects negatives", ModelEndpointDimensionalityRange),

                    // 3. Clamps (no throw).
                    TestCase.Sync("model", "enumerationquery-clamps", "EnumerationQuery clamps MaxResults and Skip", EnumerationQueryClamps),
                    TestCase.Sync("model", "memory-salience-clamp", "Memory.Salience clamps to [0,1]", MemorySalienceClamp),
                    TestCase.Sync("model", "memorysearchquery-clamps", "MemorySearchQuery clamps TopK and TextWeight", MemorySearchQueryClamps),

                    // 4. Defaults and auto-generated identifiers.
                    TestCase.Sync("model", "tenant-defaults", "Tenant defaults and auto id", TenantDefaults),
                    TestCase.Sync("model", "user-defaults", "User defaults and auto id", UserDefaults),
                    TestCase.Sync("model", "credential-defaults", "Credential defaults and auto id", CredentialDefaults),
                    TestCase.Sync("model", "authsession-defaults", "AuthSession defaults, auto id, and token", AuthSessionDefaults),
                    TestCase.Sync("model", "scope-defaults", "Scope defaults and auto id", ScopeDefaults),
                    TestCase.Sync("model", "category-defaults", "Category defaults and auto id", CategoryDefaults),
                    TestCase.Sync("model", "memory-defaults", "Memory defaults, collections, and auto id", MemoryDefaults),
                    TestCase.Sync("model", "modelendpoint-defaults", "ModelEndpoint defaults and auto id", ModelEndpointDefaults),
                    TestCase.Sync("model", "requesthistory-defaults", "RequestHistoryEntry auto id", RequestHistoryDefaults),

                    // 5. IdGenerator.
                    TestCase.Sync("model", "idgenerator-prefixes", "IdGenerator emits the right prefixes", IdGeneratorPrefixes),
                    TestCase.Sync("model", "idgenerator-length", "IdGenerator ids match Constants.IdLength", IdGeneratorLength),
                    TestCase.Sync("model", "idgenerator-uniqueness", "IdGenerator ids are unique", IdGeneratorUniqueness),
                    TestCase.Sync("model", "idgenerator-token", "IdGenerator.Token has token length", IdGeneratorToken),
                    TestCase.Sync("model", "idgenerator-endpoint-kind", "IdGenerator.Endpoint selects prefix by kind", IdGeneratorEndpointKind),

                    // 6. JSON serialization.
                    TestCase.Sync("model", "json-roundtrip", "Json round-trips a populated Memory", JsonRoundTrip),
                    TestCase.Sync("model", "json-enum-string", "Json writes enums as strings", JsonEnumString),
                    TestCase.Sync("model", "json-camelcase", "Json uses camelCase names", JsonCamelCase),
                    TestCase.Sync("model", "json-null-omission", "Json omits null fields", JsonNullOmission),
                    TestCase.Sync("model", "json-case-insensitive", "Json deserialization is case-insensitive", JsonCaseInsensitive),

                    // 7. Settings.
                    TestCase.Sync("model", "isissettings-roundtrip", "IsisSettings round-trips through a file", IsisSettingsRoundTrip),
                    TestCase.Sync("model", "mcpsettings-defaults", "McpServerSettings defaults and RestBaseUrl", McpSettingsDefaults),
                    TestCase.Sync("model", "mcpsettings-fromfile-missing", "McpServerSettings.FromFile returns defaults for a missing file", McpSettingsFromFileMissing)
                });
        }

        #endregion

        #region Private-Methods-Validation

        private static void TenantValidation()
        {
            TestCase.Throws<ArgumentNullException>(() => { new Tenant().Id = null!; }, "Tenant.Id null must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new Tenant().Id = ""; }, "Tenant.Id empty must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new Tenant().Id = "   "; }, "Tenant.Id whitespace must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new Tenant().Name = null!; }, "Tenant.Name null must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new Tenant().Name = ""; }, "Tenant.Name empty must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new Tenant().Name = "   "; }, "Tenant.Name whitespace must throw.");
        }

        private static void UserValidation()
        {
            TestCase.Throws<ArgumentNullException>(() => { new User().Id = null!; }, "User.Id null must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new User().Id = ""; }, "User.Id empty must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new User().TenantId = null!; }, "User.TenantId null must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new User().TenantId = "   "; }, "User.TenantId whitespace must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new User().Email = null!; }, "User.Email null must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new User().Email = ""; }, "User.Email empty must throw.");
        }

        private static void CredentialValidation()
        {
            TestCase.Throws<ArgumentNullException>(() => { new Credential().Id = ""; }, "Credential.Id empty must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new Credential().TenantId = null!; }, "Credential.TenantId null must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new Credential().UserId = "   "; }, "Credential.UserId whitespace must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new Credential().AccessKey = ""; }, "Credential.AccessKey empty must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new Credential().AccessKey = null!; }, "Credential.AccessKey null must throw.");
        }

        private static void AuthSessionValidation()
        {
            TestCase.Throws<ArgumentNullException>(() => { new AuthSession().Id = ""; }, "AuthSession.Id empty must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new AuthSession().TenantId = null!; }, "AuthSession.TenantId null must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new AuthSession().Token = "   "; }, "AuthSession.Token whitespace must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new AuthSession().Token = ""; }, "AuthSession.Token empty must throw.");
        }

        private static void ScopeValidation()
        {
            TestCase.Throws<ArgumentNullException>(() => { new Scope().Id = ""; }, "Scope.Id empty must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new Scope().TenantId = null!; }, "Scope.TenantId null must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new Scope().Name = "   "; }, "Scope.Name whitespace must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new Scope().Name = ""; }, "Scope.Name empty must throw.");
        }

        private static void CategoryValidation()
        {
            TestCase.Throws<ArgumentNullException>(() => { new Category().Id = ""; }, "Category.Id empty must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new Category().TenantId = null!; }, "Category.TenantId null must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new Category().ScopeId = ""; }, "Category.ScopeId empty must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new Category().Name = "   "; }, "Category.Name whitespace must throw.");
        }

        private static void MemoryValidation()
        {
            TestCase.Throws<ArgumentNullException>(() => { new Memory().Id = ""; }, "Memory.Id empty must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new Memory().TenantId = null!; }, "Memory.TenantId null must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new Memory().ScopeId = ""; }, "Memory.ScopeId empty must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new Memory().CategoryId = "   "; }, "Memory.CategoryId whitespace must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new Memory().Slug = ""; }, "Memory.Slug empty must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new Memory().Body = null!; }, "Memory.Body null must throw.");
        }

        private static void MemoryLinkValidation()
        {
            TestCase.Throws<ArgumentNullException>(() => { new MemoryLink().Id = ""; }, "MemoryLink.Id empty must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new MemoryLink().TenantId = null!; }, "MemoryLink.TenantId null must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new MemoryLink().ScopeId = ""; }, "MemoryLink.ScopeId empty must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new MemoryLink().FromMemoryId = "   "; }, "MemoryLink.FromMemoryId whitespace must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new MemoryLink().ToSlug = ""; }, "MemoryLink.ToSlug empty must throw.");
        }

        private static void ModelEndpointValidation()
        {
            TestCase.Throws<ArgumentNullException>(() => { new ModelEndpoint().Id = ""; }, "ModelEndpoint.Id empty must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new ModelEndpoint().TenantId = null!; }, "ModelEndpoint.TenantId null must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new ModelEndpoint().Name = "   "; }, "ModelEndpoint.Name whitespace must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new ModelEndpoint().Name = ""; }, "ModelEndpoint.Name empty must throw.");
        }

        private static void RequestHistoryValidation()
        {
            TestCase.Throws<ArgumentNullException>(() => { new RequestHistoryEntry().Id = null!; }, "RequestHistoryEntry.Id null must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new RequestHistoryEntry().Id = ""; }, "RequestHistoryEntry.Id empty must throw.");
            TestCase.Throws<ArgumentNullException>(() => { new RequestHistoryEntry().Id = "   "; }, "RequestHistoryEntry.Id whitespace must throw.");
        }

        #endregion

        #region Private-Methods-Range

        private static void ModelEndpointPortRange()
        {
            TestCase.Throws<ArgumentOutOfRangeException>(() => { new ModelEndpoint().Port = -1; }, "ModelEndpoint.Port -1 must throw.");
            TestCase.Throws<ArgumentOutOfRangeException>(() => { new ModelEndpoint().Port = 70000; }, "ModelEndpoint.Port 70000 must throw.");

            ModelEndpoint ok = new ModelEndpoint { Port = 8080 };
            TestCase.Require(ok.Port == 8080, "ModelEndpoint.Port should accept an in-range value.");
        }

        private static void ScopeDimensionalityRange()
        {
            TestCase.Throws<ArgumentOutOfRangeException>(() => { new Scope().Dimensionality = -1; }, "Scope.Dimensionality -1 must throw.");

            Scope ok = new Scope { Dimensionality = 768 };
            TestCase.Require(ok.Dimensionality == 768, "Scope.Dimensionality should accept a non-negative value.");
        }

        private static void ModelEndpointDimensionalityRange()
        {
            TestCase.Throws<ArgumentOutOfRangeException>(() => { new ModelEndpoint().Dimensionality = -1; }, "ModelEndpoint.Dimensionality -1 must throw.");

            ModelEndpoint ok = new ModelEndpoint { Dimensionality = 1024 };
            TestCase.Require(ok.Dimensionality == 1024, "ModelEndpoint.Dimensionality should accept a non-negative value.");
        }

        #endregion

        #region Private-Methods-Clamps

        private static void EnumerationQueryClamps()
        {
            EnumerationQuery q = new EnumerationQuery();
            q.MaxResults = 0;
            TestCase.Require(q.MaxResults == 1, "EnumerationQuery.MaxResults should clamp 0 up to 1.");
            q.MaxResults = 5000;
            TestCase.Require(q.MaxResults == 1000, "EnumerationQuery.MaxResults should clamp 5000 down to 1000.");
            q.MaxResults = 250;
            TestCase.Require(q.MaxResults == 250, "EnumerationQuery.MaxResults should keep an in-range value.");
            q.Skip = -1;
            TestCase.Require(q.Skip == 0, "EnumerationQuery.Skip should clamp negatives up to 0.");
            q.Skip = 42;
            TestCase.Require(q.Skip == 42, "EnumerationQuery.Skip should keep a non-negative value.");
        }

        private static void MemorySalienceClamp()
        {
            Memory m = new Memory();
            m.Salience = -1.0;
            TestCase.Require(m.Salience == 0.0, "Memory.Salience should clamp -1 up to 0.");
            m.Salience = 2.0;
            TestCase.Require(m.Salience == 1.0, "Memory.Salience should clamp 2 down to 1.");
            m.Salience = 0.75;
            TestCase.Require(Math.Abs(m.Salience - 0.75) < 1e-9, "Memory.Salience should keep an in-range value.");
        }

        private static void MemorySearchQueryClamps()
        {
            MemorySearchQuery q = new MemorySearchQuery();
            q.TopK = 0;
            TestCase.Require(q.TopK == 1, "MemorySearchQuery.TopK should clamp 0 up to 1.");
            q.TopK = 500;
            TestCase.Require(q.TopK == 100, "MemorySearchQuery.TopK should clamp 500 down to 100.");
            q.TopK = 25;
            TestCase.Require(q.TopK == 25, "MemorySearchQuery.TopK should keep an in-range value.");
            q.TextWeight = -1.0;
            TestCase.Require(q.TextWeight == 0.0, "MemorySearchQuery.TextWeight should clamp -1 up to 0.");
            q.TextWeight = 2.0;
            TestCase.Require(q.TextWeight == 1.0, "MemorySearchQuery.TextWeight should clamp 2 down to 1.");
        }

        #endregion

        #region Private-Methods-Defaults

        private static void TenantDefaults()
        {
            Tenant t = new Tenant();
            TestCase.Require(t.Id.StartsWith("ten_", StringComparison.Ordinal), "Tenant.Id should start with ten_.");
            TestCase.Require(t.Active, "Tenant.Active should default to true.");
            TestCase.Require(!t.Protected, "Tenant.Protected should default to false.");
            TestCase.Require(t.Name.Length == 0, "Tenant.Name should default to empty (a name must be supplied), consistent with the other entities.");
        }

        private static void UserDefaults()
        {
            User u = new User();
            TestCase.Require(u.Id.StartsWith("usr_", StringComparison.Ordinal), "User.Id should start with usr_.");
            TestCase.Require(u.Active, "User.Active should default to true.");
            TestCase.Require(!u.IsAdmin, "User.IsAdmin should default to false.");
        }

        private static void CredentialDefaults()
        {
            Credential c = new Credential();
            TestCase.Require(c.Id.StartsWith("crd_", StringComparison.Ordinal), "Credential.Id should start with crd_.");
            TestCase.Require(c.Active, "Credential.Active should default to true.");
            TestCase.Require(c.AuthMode == CredentialAuthModeEnum.DirectHeader, "Credential.AuthMode should default to DirectHeader.");
        }

        private static void AuthSessionDefaults()
        {
            AuthSession s = new AuthSession();
            TestCase.Require(s.Id.StartsWith("ses_", StringComparison.Ordinal), "AuthSession.Id should start with ses_.");
            TestCase.Require(s.Active, "AuthSession.Active should default to true.");
            TestCase.Require(s.Token.Length == Constants.TokenLength, "AuthSession.Token should default to a token-length value.");
            TestCase.Require(s.PrincipalType == PrincipalTypeEnum.User, "AuthSession.PrincipalType should default to User.");
        }

        private static void ScopeDefaults()
        {
            Scope s = new Scope();
            TestCase.Require(s.Id.StartsWith("scp_", StringComparison.Ordinal), "Scope.Id should start with scp_.");
            TestCase.Require(s.Active, "Scope.Active should default to true.");
            TestCase.Require(s.Dimensionality == 0, "Scope.Dimensionality should default to 0.");
            TestCase.Require(s.StoreProvider == StoreProviderEnum.RecallDb, "Scope.StoreProvider should default to RecallDb.");
        }

        private static void CategoryDefaults()
        {
            Category c = new Category();
            TestCase.Require(c.Id.StartsWith("cat_", StringComparison.Ordinal), "Category.Id should start with cat_.");
            TestCase.Require(c.Active, "Category.Active should default to true.");
        }

        private static void MemoryDefaults()
        {
            Memory m = new Memory();
            TestCase.Require(m.Id.StartsWith("mem_", StringComparison.Ordinal), "Memory.Id should start with mem_.");
            TestCase.Require(Math.Abs(m.Salience - 0.5) < 1e-9, "Memory.Salience should default to 0.5.");
            TestCase.Require(m.Version == 1, "Memory.Version should default to 1.");
            TestCase.Require(m.Type == MemoryTypeEnum.Project, "Memory.Type should default to Project.");
            TestCase.Require(m.Tags != null && m.Tags.Count == 0, "Memory.Tags should be a non-null empty list.");
            TestCase.Require(m.Links != null && m.Links.Count == 0, "Memory.Links should be a non-null empty list.");
            TestCase.Require(m.Metadata != null && m.Metadata.Count == 0, "Memory.Metadata should be a non-null empty dictionary.");
        }

        private static void ModelEndpointDefaults()
        {
            ModelEndpoint e = new ModelEndpoint();
            TestCase.Require(e.Id.StartsWith("eep_", StringComparison.Ordinal), "ModelEndpoint.Id should default to the eep_ prefix.");
            TestCase.Require(e.Active, "ModelEndpoint.Active should default to true.");
            TestCase.Require(e.Kind == EndpointKindEnum.Embedding, "ModelEndpoint.Kind should default to Embedding.");
            TestCase.Require(e.Port == 0, "ModelEndpoint.Port should default to 0.");
        }

        private static void RequestHistoryDefaults()
        {
            RequestHistoryEntry r = new RequestHistoryEntry();
            TestCase.Require(r.Id.StartsWith("req_", StringComparison.Ordinal), "RequestHistoryEntry.Id should start with req_.");
        }

        #endregion

        #region Private-Methods-IdGenerator

        private static void IdGeneratorPrefixes()
        {
            TestCase.Require(IdGenerator.Tenant().StartsWith("ten_", StringComparison.Ordinal), "IdGenerator.Tenant prefix.");
            TestCase.Require(IdGenerator.User().StartsWith("usr_", StringComparison.Ordinal), "IdGenerator.User prefix.");
            TestCase.Require(IdGenerator.Credential().StartsWith("crd_", StringComparison.Ordinal), "IdGenerator.Credential prefix.");
            TestCase.Require(IdGenerator.Session().StartsWith("ses_", StringComparison.Ordinal), "IdGenerator.Session prefix.");
            TestCase.Require(IdGenerator.Scope().StartsWith("scp_", StringComparison.Ordinal), "IdGenerator.Scope prefix.");
            TestCase.Require(IdGenerator.Category().StartsWith("cat_", StringComparison.Ordinal), "IdGenerator.Category prefix.");
            TestCase.Require(IdGenerator.Memory().StartsWith("mem_", StringComparison.Ordinal), "IdGenerator.Memory prefix.");
            TestCase.Require(IdGenerator.Link().StartsWith("lnk_", StringComparison.Ordinal), "IdGenerator.Link prefix.");
            TestCase.Require(IdGenerator.Request().StartsWith("req_", StringComparison.Ordinal), "IdGenerator.Request prefix.");
            TestCase.Require(IdGenerator.EmbeddingEndpoint().StartsWith("eep_", StringComparison.Ordinal), "IdGenerator.EmbeddingEndpoint prefix.");
            TestCase.Require(IdGenerator.InferenceEndpoint().StartsWith("iep_", StringComparison.Ordinal), "IdGenerator.InferenceEndpoint prefix.");
        }

        private static void IdGeneratorLength()
        {
            TestCase.Require(IdGenerator.Tenant().Length == Constants.IdLength, "IdGenerator.Tenant length should equal Constants.IdLength.");
            TestCase.Require(IdGenerator.User().Length == Constants.IdLength, "IdGenerator.User length should equal Constants.IdLength.");
            TestCase.Require(IdGenerator.Memory().Length == Constants.IdLength, "IdGenerator.Memory length should equal Constants.IdLength.");
            TestCase.Require(IdGenerator.Scope().Length == Constants.IdLength, "IdGenerator.Scope length should equal Constants.IdLength.");
        }

        private static void IdGeneratorUniqueness()
        {
            TestCase.Require(IdGenerator.Tenant() != IdGenerator.Tenant(), "IdGenerator.Tenant should be unique per call.");
            TestCase.Require(IdGenerator.Memory() != IdGenerator.Memory(), "IdGenerator.Memory should be unique per call.");
            TestCase.Require(IdGenerator.Token() != IdGenerator.Token(), "IdGenerator.Token should be unique per call.");
        }

        private static void IdGeneratorToken()
        {
            TestCase.Require(IdGenerator.Token().Length == Constants.TokenLength, "IdGenerator.Token length should equal Constants.TokenLength (64).");
        }

        private static void IdGeneratorEndpointKind()
        {
            TestCase.Require(IdGenerator.Endpoint(EndpointKindEnum.Embedding).StartsWith("eep_", StringComparison.Ordinal), "IdGenerator.Endpoint(Embedding) should use eep_.");
            TestCase.Require(IdGenerator.Endpoint(EndpointKindEnum.Inference).StartsWith("iep_", StringComparison.Ordinal), "IdGenerator.Endpoint(Inference) should use iep_.");
        }

        #endregion

        #region Private-Methods-Json

        private static Memory BuildMemory()
        {
            Memory m = new Memory
            {
                TenantId = "ten_1",
                ScopeId = "scp_1",
                CategoryId = "cat_1",
                Slug = "centerline",
                Title = "The Centerline",
                Body = "Control the centerline.",
                Type = MemoryTypeEnum.Reference
            };
            m.Tags.Add("posture");
            m.Tags.Add("framing");
            m.Metadata["confidence"] = "high";
            return m;
        }

        private static void JsonRoundTrip()
        {
            Memory original = BuildMemory();
            string json = Json.Serialize(original);
            Memory? read = Json.Deserialize<Memory>(json);

            TestCase.Require(read != null, "Deserialized memory should not be null.");
            TestCase.Require(read!.Id == original.Id, "Memory.Id should survive round trip.");
            TestCase.Require(read.TenantId == "ten_1", "Memory.TenantId should survive round trip.");
            TestCase.Require(read.Slug == "centerline", "Memory.Slug should survive round trip.");
            TestCase.Require(read.Body == "Control the centerline.", "Memory.Body should survive round trip.");
            TestCase.Require(read.Type == MemoryTypeEnum.Reference, "Memory.Type should survive round trip.");
            TestCase.Require(read.Tags.Count == 2 && read.Tags.Contains("posture") && read.Tags.Contains("framing"), "Memory.Tags should survive round trip.");
            TestCase.Require(read.Metadata.ContainsKey("confidence") && read.Metadata["confidence"] == "high", "Memory.Metadata should survive round trip.");
        }

        private static void JsonEnumString()
        {
            string json = Json.Serialize(BuildMemory());
            TestCase.Require(json.Contains("Reference", StringComparison.Ordinal), "Enum should serialize as the string 'Reference'.");
            TestCase.Require(!json.Contains("\"type\": 3", StringComparison.Ordinal), "Enum should not serialize as a number.");
        }

        private static void JsonCamelCase()
        {
            string json = Json.Serialize(BuildMemory());
            TestCase.Require(json.Contains("tenantId", StringComparison.Ordinal), "Property names should be camelCase (tenantId).");
            TestCase.Require(!json.Contains("TenantId", StringComparison.Ordinal), "Property names should not be PascalCase (TenantId).");
        }

        private static void JsonNullOmission()
        {
            // A fresh memory leaves Title, Summary, Author, and other reference fields null.
            Memory m = new Memory
            {
                TenantId = "ten_1",
                ScopeId = "scp_1",
                CategoryId = "cat_1",
                Slug = "s1",
                Body = "b"
            };
            string json = Json.Serialize(m);
            TestCase.Require(!json.Contains("\"title\"", StringComparison.Ordinal), "Null Title should be omitted from JSON.");
            TestCase.Require(!json.Contains("\"summary\"", StringComparison.Ordinal), "Null Summary should be omitted from JSON.");
        }

        private static void JsonCaseInsensitive()
        {
            // PascalCase property names must still bind because deserialization is case-insensitive.
            string json = "{\"Id\":\"mem_case\",\"TenantId\":\"ten_1\",\"ScopeId\":\"scp_1\",\"CategoryId\":\"cat_1\",\"Slug\":\"case\",\"Body\":\"body\",\"Type\":\"Feedback\"}";
            Memory? read = Json.Deserialize<Memory>(json);
            TestCase.Require(read != null, "Case-insensitive deserialization should not return null.");
            TestCase.Require(read!.Id == "mem_case", "Case-insensitive deserialization should bind Id.");
            TestCase.Require(read.TenantId == "ten_1", "Case-insensitive deserialization should bind TenantId.");
            TestCase.Require(read.Slug == "case", "Case-insensitive deserialization should bind Slug.");
            TestCase.Require(read.Type == MemoryTypeEnum.Feedback, "Case-insensitive deserialization should bind the enum.");
        }

        #endregion

        #region Private-Methods-Settings

        private static void IsisSettingsRoundTrip()
        {
            string path = Path.Combine(Path.GetTempPath(), "isis-settings-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                IsisSettings s = new IsisSettings();
                s.Rest.Port = 9310;
                s.Auth.DefaultSecretKey = "round-trip-key";
                s.Database.Type = DatabaseTypeEnum.Postgresql;
                s.ToFile(path);

                IsisSettings loaded = IsisSettings.FromFile(path);
                TestCase.Require(loaded.Rest.Port == 9310, "IsisSettings.Rest.Port should round trip.");
                TestCase.Require(loaded.Auth.DefaultSecretKey == "round-trip-key", "IsisSettings.Auth.DefaultSecretKey should round trip.");
                TestCase.Require(loaded.Database.Type == DatabaseTypeEnum.Postgresql, "IsisSettings.Database.Type should round trip.");
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        private static void McpSettingsDefaults()
        {
            McpServerSettings s = new McpServerSettings();
            TestCase.Require(s.RestBaseUrl() == "http://127.0.0.1:8700", "McpServerSettings.RestBaseUrl should be http://127.0.0.1:8700 for defaults.");
            TestCase.Require(s.Port == 8720, "McpServerSettings.Port should default to 8720.");
            TestCase.Require(s.McpPath == "/mcp", "McpServerSettings.McpPath should default to /mcp.");
        }

        private static void McpSettingsFromFileMissing()
        {
            string path = Path.Combine(Path.GetTempPath(), "isis-mcp-missing-" + Guid.NewGuid().ToString("N") + ".json");
            TestCase.Require(!File.Exists(path), "Precondition: the settings file should not exist.");
            McpServerSettings s = McpServerSettings.FromFile(path);
            TestCase.Require(s.Port == 8720, "FromFile(nonexistent) should return defaults (Port 8720).");
            TestCase.Require(s.RestBaseUrl() == "http://127.0.0.1:8700", "FromFile(nonexistent) should return default RestBaseUrl.");
        }

        #endregion
    }
}
