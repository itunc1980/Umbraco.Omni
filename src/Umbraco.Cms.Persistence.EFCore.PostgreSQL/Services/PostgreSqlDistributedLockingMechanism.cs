using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.DistributedLocking;
using Umbraco.Cms.Core.DistributedLocking.Exceptions;
using Umbraco.Cms.Core.Exceptions;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Extensions;

namespace Umbraco.Cms.Persistence.EFCore.PostgreSQL.Services;

public class PostgreSqlDistributedLockingMechanism : IDistributedLockingMechanism
{
    private ConnectionStrings _connectionStrings;
    private GlobalSettings _globalSettings;
    private readonly ILogger<PostgreSqlDistributedLockingMechanism> _logger;
    private readonly Lazy<IScopeAccessor> _scopeAccessor;

    public PostgreSqlDistributedLockingMechanism(
        ILogger<PostgreSqlDistributedLockingMechanism> logger,
        Lazy<IScopeAccessor> scopeAccessor,
        IOptionsMonitor<GlobalSettings> globalSettings,
        IOptionsMonitor<ConnectionStrings> connectionStrings)
    {
        _logger = logger;
        _scopeAccessor = scopeAccessor;
        _connectionStrings = connectionStrings.CurrentValue;
        _globalSettings = globalSettings.CurrentValue;
        globalSettings.OnChange(x => _globalSettings = x);
        connectionStrings.OnChange(x => _connectionStrings = x);
    }

    /// <inheritdoc/>
    public bool Enabled => _connectionStrings.IsConnectionStringConfigured() &&
                           string.Equals(_connectionStrings.ProviderName, "PostgreSQL", StringComparison.InvariantCultureIgnoreCase);

    /// <inheritdoc/>
    public IDistributedLock ReadLock(int lockId, TimeSpan? obtainLockTimeout = null)
    {
        obtainLockTimeout ??= _globalSettings.DistributedLockingReadLockDefaultTimeout;
        return new PostgreSqlDistributedLock(this, lockId, DistributedLockType.ReadLock, obtainLockTimeout.Value);
    }

    /// <inheritdoc/>
    public IDistributedLock WriteLock(int lockId, TimeSpan? obtainLockTimeout = null)
    {
        obtainLockTimeout ??= _globalSettings.DistributedLockingWriteLockDefaultTimeout;
        return new PostgreSqlDistributedLock(this, lockId, DistributedLockType.WriteLock, obtainLockTimeout.Value);
    }

    private sealed class PostgreSqlDistributedLock : IDistributedLock
    {
        private readonly PostgreSqlDistributedLockingMechanism _parent;
        private readonly TimeSpan _timeout;

        public PostgreSqlDistributedLock(
            PostgreSqlDistributedLockingMechanism parent,
            int lockId,
            DistributedLockType lockType,
            TimeSpan timeout)
        {
            _parent = parent;
            _timeout = timeout;
            LockId = lockId;
            LockType = lockType;

            try
            {
                switch (lockType)
                {
                    case DistributedLockType.ReadLock:
                        ObtainReadLock();
                        break;
                    case DistributedLockType.WriteLock:
                        ObtainWriteLock();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(lockType), lockType, @"Unsupported lockType");
                }
            }
            catch (Exception ex) when (IsTimeoutException(ex))
            {
                if (LockType == DistributedLockType.ReadLock)
                {
                    throw new DistributedReadLockTimeoutException(LockId);
                }
                throw new DistributedWriteLockTimeoutException(LockId);
            }
        }

        public int LockId { get; }
        public DistributedLockType LockType { get; }

        public void Dispose()
        {
            // Mostly no op, cleaned up by completing transaction in scope.
        }

        public override string ToString() => $"PostgreSqlDistributedLock({LockId})";

        private bool IsTimeoutException(Exception ex)
        {
            // Simplified timeout check for Npgsql. In a real scenario, check for specific PostgreSQL error codes.
            return ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
        }

        private void ObtainReadLock()
        {
            IUmbracoDatabase db = _parent._scopeAccessor.Value.AmbientScope?.Database
                ?? throw new PanicException("no database was found");

            if (!db.InTransaction)
            {
                throw new InvalidOperationException("PostgreSqlDistributedLockingMechanism requires a transaction to function.");
            }
        }

        private void ObtainWriteLock()
        {
            IUmbracoDatabase db = _parent._scopeAccessor.Value.AmbientScope?.Database
                ?? throw new PanicException("no database was found");

            if (!db.InTransaction)
            {
                throw new InvalidOperationException("PostgreSqlDistributedLockingMechanism requires a transaction to function.");
            }

            var query = @$"UPDATE ""umbracoLock"" SET value = (CASE WHEN (value=1) THEN -1 ELSE 1 END) WHERE id = {LockId.ToString(CultureInfo.InvariantCulture)}";
            DbCommand command = db.CreateCommand(db.Connection, CommandType.Text, query);
            command.CommandTimeout = (int)Math.Ceiling(_timeout.TotalSeconds);

            try
            {
                var i = db.ExecuteNonQuery(command);
                if (i == 0)
                {
                    throw new ArgumentException($"LockObject with id={LockId} does not exist.");
                }
            }
            catch (Exception ex) when (IsTimeoutException(ex))
            {
                throw new DistributedWriteLockTimeoutException(LockId);
            }
        }
    }
}
