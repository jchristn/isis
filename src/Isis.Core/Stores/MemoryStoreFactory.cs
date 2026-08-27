namespace Isis.Core.Stores
{
    using System;
    using Isis.Core.Enums;
    using Isis.Core.Models;
    using Isis.Core.Stores.Filesystem;
    using Isis.Core.Stores.RecallDb;
    using Isis.Core.Stores.Verbex;

    /// <summary>
    /// Creates the appropriate <see cref="IMemoryStore"/> for a scope based on its configured provider.
    /// </summary>
    public static class MemoryStoreFactory
    {
        #region Public-Methods

        /// <summary>
        /// Create a memory store for the given provider.
        /// </summary>
        /// <param name="provider">The store provider.</param>
        /// <returns>A memory store.</returns>
        public static IMemoryStore Create(StoreProviderEnum provider)
        {
            switch (provider)
            {
                case StoreProviderEnum.RecallDb:
                    return new RecallDbMemoryStore();
                case StoreProviderEnum.Verbex:
                    return new VerbexMemoryStore();
                case StoreProviderEnum.Filesystem:
                    return new FilesystemMemoryStore();
                default:
                    throw new NotSupportedException("Unknown store provider: " + provider + ".");
            }
        }

        /// <summary>
        /// Create a memory store for the given scope using default (unconfigured) external clients.
        /// </summary>
        /// <param name="scope">The scope.</param>
        /// <returns>A memory store.</returns>
        /// <exception cref="ArgumentNullException">Thrown when scope is null.</exception>
        public static IMemoryStore Create(Scope scope)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            return Create(scope.StoreProvider);
        }

        /// <summary>
        /// Create a memory store for the given scope, configuring external clients from the supplied options.
        /// </summary>
        /// <param name="scope">The scope.</param>
        /// <param name="options">Store connection options.</param>
        /// <returns>A memory store.</returns>
        /// <exception cref="ArgumentNullException">Thrown when scope is null.</exception>
        public static IMemoryStore Create(Scope scope, StoreOptions? options)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));

            switch (scope.StoreProvider)
            {
                case StoreProviderEnum.RecallDb:
                    if (options != null && !string.IsNullOrEmpty(options.RecallDbEndpoint) && !string.IsNullOrEmpty(options.RecallDbAdminKey))
                    {
                        return new RecallDbMemoryStore(options.RecallDbEndpoint!, options.RecallDbAdminKey!);
                    }

                    return new RecallDbMemoryStore();
                case StoreProviderEnum.Verbex:
                    return new VerbexMemoryStore();
                case StoreProviderEnum.Filesystem:
                    return new FilesystemMemoryStore();
                default:
                    throw new NotSupportedException("Unknown store provider: " + scope.StoreProvider + ".");
            }
        }

        #endregion
    }
}
