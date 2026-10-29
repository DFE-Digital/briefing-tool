using Dfe.Briefing.Tool.Application.Providers.Interfaces;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using Dfe.Briefing.Tool.Web.Constants;
using Dfe.Briefing.Tool.Web.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dfe.Briefing.Tool.Web.Pages.Briefing
{
    /// <summary>
    /// The second step: search for the school or trust. With JavaScript, matches are suggested as the person types (see
    /// <see cref="OnGetSearchAsync"/>); without it, they enter its name, URN or TRN.
    /// </summary>
    public class FindModel(IBriefingSubjectFinder subjectFinder, ILogger<FindModel> logger) : PageModel
    {
        // Fewer letters match too many names to be useful.
        public const int MinimumSearchLength = 3;

        [BindProperty(SupportsGet = true)]
        public string? Type { get; set; }

        /// <summary>What the person entered: a name, a URN or TRN, or a suggestion they chose, e.g. "Name (URN 123456)".</summary>
        [BindProperty]
        public string? UrnOrTrn { get; set; }

        public bool IsTrust => Type == SubjectTypes.Trust;

        public string SubjectNoun => IsTrust ? "trust" : "school";

        public string ReferenceLabel => IsTrust ? "TRN" : "URN";

        public IActionResult OnGet(string? urnOrTrn)
        {
            if (!SubjectTypes.IsValid(Type))
                return RedirectToPage("Type");

            // Coming back from the confirmation page to change it.
            UrnOrTrn = urnOrTrn;
            return Page();
        }

        /// <summary>The schools or trusts matching what the person has typed so far, for the autocomplete.</summary>
        public async Task<IActionResult> OnGetSearchAsync(string? query, CancellationToken cancellationToken)
        {
            if (!SubjectTypes.IsValid(Type) || query is null || query.Trim().Length < MinimumSearchLength)
                return new JsonResult(Array.Empty<SubjectSuggestion>());

            try
            {
                var subjects = await subjectFinder.SearchAsync(query, IsTrust, cancellationToken);
                return new JsonResult(subjects.Select(subject => new SubjectSuggestion(Suggestion(subject), subject.Name, subject.UrnOrTrn, subject.Region)));
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed to search for a {SubjectType}", Type);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
        {
            if (!SubjectTypes.IsValid(Type))
                return RedirectToPage("Type");

            var entered = UrnOrTrn?.Trim() ?? string.Empty;
            if (ValidationError(entered) is { } error)
            {
                ModelState.AddModelError(nameof(UrnOrTrn), error);
                return Page();
            }

            try
            {
                var matches = await subjectFinder.SearchAsync(BriefingReference.FromSuggestion(entered) ?? entered, IsTrust, cancellationToken);
                switch (matches.Count)
                {
                    case 1:
                        return RedirectToPage("Confirm", new { urnOrTrn = matches[0].UrnOrTrn });
                    case 0:
                        ModelState.AddModelError(nameof(UrnOrTrn), $"No {SubjectNoun} was found for {entered}. Check it and try again");
                        return Page();
                    default:
                        ModelState.AddModelError(nameof(UrnOrTrn), $"More than one {SubjectNoun} matches {entered}. Choose one from the list, or enter its whole {ReferenceLabel}");
                        return Page();
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed to find the {SubjectType} {UrnOrTrn}", Type, entered);
                ModelState.AddModelError(nameof(UrnOrTrn), "Sorry, something went wrong finding this. Please try again");
                return Page();
            }
        }

        // How a match is shown, and sent back when chosen: the reference in it is what's looked up. A URN or TRN search
        // can find a school or a trust whichever was chosen, so each match is labelled with its own kind of reference.
        private static string Suggestion(BriefingSubject subject) => $"{subject.Name} ({(subject.IsTrust ? "TRN" : "URN")} {subject.UrnOrTrn})";

        private string? ValidationError(string entered)
        {
            if (entered.Length == 0)
                return $"Enter the {SubjectNoun}'s name, URN or TRN";

            return BriefingReference.FromSuggestion(entered) is null && entered.Length < MinimumSearchLength
                ? $"Enter at least {MinimumSearchLength} characters of the {SubjectNoun}'s name, URN or TRN"
                : null;
        }
    }

    /// <summary>One autocomplete suggestion: its text, and the school or trust it is.</summary>
    public sealed record SubjectSuggestion(string Text, string Name, string UrnOrTrn, string? Region);
}
