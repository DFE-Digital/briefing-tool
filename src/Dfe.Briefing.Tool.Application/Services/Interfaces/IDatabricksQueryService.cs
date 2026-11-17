using Dfe.Briefing.Tool.Domain.ValueObjects.Requests;
using Dfe.Briefing.Tool.Domain.ValueObjects.Responses;

namespace Dfe.Briefing.Tool.Application.Services.Interfaces;

internal interface IDatabricksQueryService
{
    Task<ResponseModel> RunQueryAsync(EstablishmentQueryModel parameters);
}
