using Dfe.Briefing.Tool.Application.Exceptions;
using Dfe.Briefing.Tool.Application.Orchestrators.Interfaces;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using Dfe.Briefing.Tool.Web.Sanitization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace Dfe.Briefing.Tool.Web.Pages
{
    public class IndexModel(IBriefingOrchestrator briefingOrchestrator, ILogger<IndexModel> logger) : PageModel
    {
        [BindProperty]
        public BriefingRequest Input { get; set; } = new();

        public string? BriefingOutput { get; private set; }

        public string? ErrorMessage { get; private set; }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var parameters = new BriefingParameters(Input.SchoolUrnOrTrustTrn, Input.SchoolOrTrustName);

            try
            {
                var result = await briefingOrchestrator.GetBriefing(parameters, cancellationToken);
                BriefingOutput = BriefingHtmlSanitizer.Sanitize(result.Output);
            }
            catch (NoBriefingDataFoundException ex)
            {
                logger.LogWarning(ex, "No data found to generate a briefing for {SchoolOrTrustName}", Input.SchoolOrTrustName);
                ErrorMessage = "No data was found, so a briefing could not be generated.";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to generate briefing for {SchoolOrTrustName}", Input.SchoolOrTrustName);
                ErrorMessage = "Sorry, something went wrong generating this briefing. Please try again.";
            }

            return Page();
        }
    }

    public sealed class BriefingRequest
    {
        [Required(ErrorMessage = "Enter the school URN or trust TRN")]
        public string SchoolUrnOrTrustTrn { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter the school or trust name")]
        public string SchoolOrTrustName { get; set; } = string.Empty;
    }
}
