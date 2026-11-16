namespace Dfe.Briefing.Tool.Domain.ValueObjects.Requests;

public class EstablishmentQueryModel
{
    public int? Urn { get; set; }
    public string? Name { get; set; } 
    public int Limit { get; set; } = 5;
}

