using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Umbraco.Cms.Persistence.EFCore.Repositories;

/// <summary>
/// Defines a generic repository interface for Entity Framework Core operations.
/// </summary>
/// <typeparam name="TEntity">The type of the entity.</typeparam>
public interface IEfRepository<TEntity> where TEntity : class
{
    /// <summary>
    /// Returns a queryable root for the entity. By default, it disables tracking for read-only query performance.
    /// </summary>
    /// <param name="asNoTracking">If true, configures the query to be non-tracking.</param>
    /// <returns>An <see cref="IQueryable{TEntity}"/> instance.</returns>
    IQueryable<TEntity> Query(bool asNoTracking = true);

    /// <summary>
    /// Retrieves an entity by its identifier asynchronously.
    /// </summary>
    /// <param name="id">The identifier of the entity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching entity, or null if not found.</returns>
    Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all entities asynchronously.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A collection of all entities.</returns>
    Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new entity asynchronously.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a collection of entities asynchronously.
    /// </summary>
    /// <param name="entities">The entities to add.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an entity as modified in the context.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    void Update(TEntity entity);

    /// <summary>
    /// Marks an entity for deletion in the context.
    /// </summary>
    /// <param name="entity">The entity to delete.</param>
    void Delete(TEntity entity);

    /// <summary>
    /// Marks a collection of entities for deletion in the context.
    /// </summary>
    /// <param name="entities">The entities to delete.</param>
    void DeleteRange(IEnumerable<TEntity> entities);

    /// <summary>
    /// Determines whether any elements of a sequence satisfy a condition.
    /// </summary>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if any elements satisfy the condition; otherwise, false.</returns>
    Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the number of elements in a sequence that satisfy a condition.
    /// </summary>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of elements that satisfy the condition.</returns>
    Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
}
