using Dfe.Briefing.Tool.Application.Helpers;
using Dfe.Briefing.Tool.Application.Services.Interfaces;
using Dfe.Briefing.Tool.Data;
using Dfe.Briefing.Tool.Domain.ValueObjects.Requests;
using Dfe.Briefing.Tool.Domain.ValueObjects.Responses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dfe.Briefing.Tool.Application.Services;

internal class DatabricksQueryService(Logger<DatabricksQueryService> logger, IDbContextFactory<DatabricksDbContext> dbContextFactory) : IDatabricksQueryService
{
    public async Task<ResponseModel> RunQueryAsync(EstablishmentQueryModel parameters)
    {
        using var context = dbContextFactory.CreateDbContext();
        try
        {
            var query = context.Establishments.AsNoTracking().AsQueryable();

            if (parameters.Urn.HasValue)
                query = query.Where(e => e.Urn == parameters.Urn);

            if (!string.IsNullOrWhiteSpace(parameters.Name))
                query = query.Where(e => e.SchoolName == parameters.Name);
             
 
            var establishments = await query
                .OrderBy(e => e.SchoolName)
                .Take(parameters.Limit)
                .ToListAsync();

            return new ResponseModel(JsonSerializerHelper.Serialize(establishments), establishments.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while running query with parameters: {Parameters}", JsonSerializerHelper.Serialize(parameters));
            return new ResponseModel(Error: ex.Message);
        }
    }
}