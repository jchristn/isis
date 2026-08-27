namespace Isis.Core.Models
{
    using System;
    using Isis.Core.Enums;
    using Isis.Core.Helpers;

    /// <summary>
    /// A granular access-control rule for a user within a tenant. A rule either permits or denies a set of
    /// operations on a resource type, optionally narrowed to a single resource. Permissions are user-scoped;
    /// a credential inherits its owning user's permissions.
    /// </summary>
    /// <remarks>
    /// Evaluation follows explicit-deny-wins semantics. When a principal has no permission records they
    /// retain baseline access to their own tenant's resources; once any permission record exists for the
    /// principal, access is decided strictly by the rules (deny beats permit, and an unmatched request is
    /// denied). System administrators and tenant administrators bypass permission evaluation.
    /// Resource types include the wildcard "All" and: "Scope", "Category", "Memory", "Endpoint". Operations
    /// include the wildcard "All", the shorthand "Write" (expands to Create, Update, and Delete), and:
    /// "Create", "Read", "Update", "Delete".
    /// </remarks>
    public class Permission
    {
        #region Public-Members

        /// <summary>
        /// Permission identifier. Defaults to a generated value; may not be set to null or empty.
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Owning tenant identifier. May not be set to null or empty.
        /// </summary>
        public string TenantId
        {
            get
            {
                return _TenantId;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(TenantId));
                _TenantId = value;
            }
        }

        /// <summary>
        /// The user the permission applies to. May not be set to null or empty.
        /// </summary>
        public string UserId
        {
            get
            {
                return _UserId;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(UserId));
                _UserId = value;
            }
        }

        /// <summary>
        /// The resource type this rule covers (for example "Memory", "Scope", "Category", "Endpoint", or "All").
        /// </summary>
        public string ResourceType
        {
            get
            {
                return _ResourceType;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(ResourceType));
                _ResourceType = value;
            }
        }

        /// <summary>
        /// The operation this rule covers ("Create", "Read", "Update", "Delete", "Write", or "All").
        /// </summary>
        public string Operation
        {
            get
            {
                return _Operation;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Operation));
                _Operation = value;
            }
        }

        /// <summary>
        /// Whether this rule permits or denies. Deny overrides Permit.
        /// </summary>
        public PermissionTypeEnum PermissionType { get; set; } = PermissionTypeEnum.Permit;

        /// <summary>
        /// An optional specific resource identifier to which this rule is scoped. Null applies to all
        /// resources of the type within the tenant.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Indicates whether the permission is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// UTC timestamp when the permission was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC timestamp when the permission was last updated.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.Permission();
        private string _TenantId = String.Empty;
        private string _UserId = String.Empty;
        private string _ResourceType = "All";
        private string _Operation = "All";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a permission.
        /// </summary>
        public Permission()
        {
        }

        #endregion
    }
}
