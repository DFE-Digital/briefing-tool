using Dfe.Briefing.Tool.Application.Providers.Interfaces;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using Dfe.Briefing.Tool.Web.Validation;
using Microsoft.AspNetCore.Mvc;

namespace Dfe.Briefing.Tool.Web.Pages.Briefing
{
    /// <summary>
    /// The second step: search for the school or trust, whichever the person chose on Briefing/Type. With JavaScript,
    /// matches are suggested as the person types (see <see cref="OnGetSearchAsync"/>); without it, they enter its name and
    /// URN or TRN.
    /// </summary>
    public class FindModel(IBriefingSubjectFinder subjectFinder, ILogger<FindModel> logger) : SubjectTypePageModel
    {
        // Fewer letters match too many names to be useful.
        public const int MinimumSearchLength = 4;

        /// <summary>What the person entered: a name, a school's URN or trust's TRN, or a suggestion they chose, e.g. "Name (URN 123456)".</summary>
        [BindProperty]
        public string? UrnOrTrn { get; set; }

        public IActionResult OnGet(string? urnOrTrn)
        {
            if (!HasValidType)
                return RedirectToStart();

            // Coming back from the confirmation page to change it.
            UrnOrTrn = urnOrTrn;
            return Page();
        }

        /// <summary>The schools or trusts matching what the person has typed so far, for the autocomplete.</summary>
        public async Task<IActionResult> OnGetSearchAsync(string? query, CancellationToken cancellationToken)
        {
            if (!HasValidType || query is null || query.Trim().Length < MinimumSearchLength)
                return new JsonResult(Array.Empty<SubjectSuggestion>());

            try
            {
                var subjects = await subjectFinder.SearchAsync(query, IsTrust, cancellationToken);
                return new JsonResult(subjects.Select(subject => new SubjectSuggestion(SuggestionOf(subject), subject.Name, subject.UrnOrTrn, subject.Region)));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The person typed again, so the autocomplete dropped this search for a newer one: nobody is waiting for
                // its answer, and it isn't an error.
                return new EmptyResult();
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed to search for a {SubjectType}", Type);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
        {
            if (!HasValidType)
                return RedirectToStart();

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
                        return RedirectToPage("Confirm", new { type = Type, urnOrTrn = matches[0].UrnOrTrn });
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

        // How a match is shown, and sent back when chosen: the reference in it is what's looked up.
        private string SuggestionOf(BriefingSubject subject) => $"{subject.Name} ({ReferenceLabel} {subject.UrnOrTrn})";

        private string? ValidationError(string entered)
        {
            if (entered.Length == 0)
                return $"Enter the {SubjectNoun}'s name or {ReferenceLabel}";

            return BriefingReference.FromSuggestion(entered) is null && entered.Length < MinimumSearchLength
                ? $"Enter at least {MinimumSearchLength} characters of the {SubjectNoun}'s name or {ReferenceLabel}"
                : null;
        }
    }

    /// <summary>One autocomplete suggestion: its text, and the school or trust it is.</summary>
    public sealed record SubjectSuggestion(string Text, string Name, string UrnOrTrn, string? Region);
}
