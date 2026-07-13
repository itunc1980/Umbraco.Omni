using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;

namespace Umbraco.Cms.Persistence.EFCore.Services;

/// <summary>
/// Provides high-performance database querying services for Umbraco entities using EF Core.
/// Demarcates performance best practices such as Compiled Queries, Query Splitting, and No-Tracking.
/// </summary>
public class UmbracoHierarchyQueryService
{
    private readonly UmbracoDbContext _context;

    // 1. Pre-compiled query for high-throughput, single-key lookups.
    // Compiles the expression tree once and caches the execution plan, matching NPoco raw execution times.
    private static readonly Func<UmbracoDbContext, int, Task<UserDto?>> GetUserByIdCompiledQuery =
        EF.CompileAsyncQuery((UmbracoDbContext context, int id) =>
            context.Set<UserDto>()
                .AsNoTracking()
                .FirstOrDefault(u => u.Id == id));

    /// <summary>
    /// Initializes a new instance of the <see cref="UmbracoHierarchyQueryService"/> class.
    /// </summary>
    /// <param name="context">The EF Core database context.</param>
    public UmbracoHierarchyQueryService(UmbracoDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Fetches a User by ID using the pre-compiled EF query for maximum performance.
    /// </summary>
    /// <param name="id">The user ID.</param>
    /// <returns>The user DTO, or null if not found.</returns>
    public async Task<UserDto?> GetUserByIdAsync(int id)
    {
        return await GetUserByIdCompiledQuery(_context, id);
    }

    /// <summary>
    /// Fetches a paginated list of users, including their groups and start nodes.
    /// Uses AsSplitQuery to avoid Cartesian product explosion across multiple joined collections.
    /// Uses AsNoTracking to disable EF Core change-tracker allocations.
    /// </summary>
    /// <param name="skip">The number of records to skip.</param>
    /// <param name="take">The number of records to return.</param>
    /// <returns>A list of users with their related groups and start nodes.</returns>
    public async Task<List<UserDto>> GetUsersWithGroupsAndNodesAsync(int skip, int take)
    {
        return await _context.Set<UserDto>()
            .AsNoTracking()             // 1. Turn off EF Core entity tracking to optimize memory footprint
            .AsSplitQuery()             // 2. Split query execution to resolve joined collections in separate SQL calls
            .Include(u => u.UserGroupDtos)
            .Include(u => u.UserStartNodeDtos)
            .OrderBy(u => u.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }
}
