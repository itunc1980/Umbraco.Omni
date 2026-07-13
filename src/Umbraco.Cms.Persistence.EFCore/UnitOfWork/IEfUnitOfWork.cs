using System;
using System.Threading;
using System.Threading.Tasks;
using Umbraco.Cms.Persistence.EFCore.Repositories;

namespace Umbraco.Cms.Persistence.EFCore.UnitOfWork;

/// <summary>
/// Defines a Unit of Work interface for managing EF Core transactions and repositories.
/// </summary>
public interface IEfUnitOfWork : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets a repository instance for the specified entity type.
    /// </summary>
    /// <typeparam name="TEntity">The type of the entity.</typeparam>
    /// <returns>An <see cref="IEfRepository{TEntity}"/> instance.</returns>
    IEfRepository<TEntity> Repository<TEntity>() where TEntity : class;

    /// <summary>
    /// Saves all changes made in this unit of work to the database asynchronously.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins a database transaction asynchronously.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the current database transaction asynchronously.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the current database transaction asynchronously.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
