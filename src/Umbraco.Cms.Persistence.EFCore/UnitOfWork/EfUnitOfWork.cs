using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;
using Umbraco.Cms.Persistence.EFCore.Repositories;

namespace Umbraco.Cms.Persistence.EFCore.UnitOfWork;

/// <summary>
/// Provides a concrete implementation of <see cref="IEfUnitOfWork"/> using <see cref="UmbracoDbContext"/>.
/// </summary>
public class EfUnitOfWork : IEfUnitOfWork
{
    private readonly UmbracoDbContext _context;
    private readonly ConcurrentDictionary<Type, object> _repositories;
    private IDbContextTransaction? _transaction;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="EfUnitOfWork"/> class.
    /// </summary>
    /// <param name="context">The DbContext instance.</param>
    public EfUnitOfWork(UmbracoDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _repositories = new ConcurrentDictionary<Type, object>();
    }

    /// <inheritdoc />
    public IEfRepository<TEntity> Repository<TEntity>() where TEntity : class
    {
        return (IEfRepository<TEntity>)_repositories.GetOrAdd(
            typeof(TEntity),
            _ => new EfRepository<TEntity>(_context));
    }

    /// <inheritdoc />
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            return;
        }

        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await SaveChangesAsync(cancellationToken);
            if (_transaction != null)
            {
                await _transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
        finally
        {
            await DisposeTransactionAsync();
        }
    }

    /// <inheritdoc />
    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync(cancellationToken);
            await DisposeTransactionAsync();
        }
    }

    private async Task DisposeTransactionAsync()
    {
        if (_transaction != null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisposeAsync(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    /// <param name="disposing">If true, releases both managed and unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _transaction?.Dispose();
                _context.Dispose();
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources asynchronously.
    /// </summary>
    /// <param name="disposing">If true, releases both managed and unmanaged resources.</param>
    /// <returns>A task representing the asynchronous dispose operation.</returns>
    protected virtual async ValueTask DisposeAsync(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                if (_transaction != null)
                {
                    await _transaction.DisposeAsync();
                }

                await _context.DisposeAsync();
            }

            _disposed = true;
        }
    }
}
