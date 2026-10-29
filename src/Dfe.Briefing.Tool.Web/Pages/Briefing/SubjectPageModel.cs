using Dfe.Briefing.Tool.Application.Providers.Interfaces;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using Dfe.Briefing.Tool.Web.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dfe.Briefing.Tool.Web.Pages.Briefing
{
    /// <summary>
    /// A page about the school or trust found on Briefing/Find, passed as its URN or TRN in the URL. Each page looks its
    /// record up again rather than keeping it between requests, so any server instance can answer.
    /// </summary>
    public abstract class SubjectPageModel(IBriefingSubjectFinder subjectFinder) : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public string? UrnOrTrn { get; set; }

        /// <summary>The school or trust, once <see cref="LoadSubjectAsync"/> has found it.</summary>
        public BriefingSubject? Subject { get; private set; }

        public string SubjectNoun => Subject is { IsTrust: true } ? "trust" : "school";

        public string NameLabel => Subject is { IsTrust: true } ? "Trust name" : "School name";

        public string ReferenceLabel => Subject is { IsTrust: true } ? "TRN" : "URN";

        /// <returns>Whether the school or trust was found; if not, the page should send the person back to the start.</returns>
        protected async Task<bool> LoadSubjectAsync(CancellationToken cancellationToken)
        {
            var reference = BriefingReference.Normalise(UrnOrTrn);
            Subject = BriefingReference.IsValid(reference) ? await subjectFinder.FindAsync(reference, cancellationToken) : null;
            return Subject is not null;
        }

        protected IActionResult RedirectToStart() => RedirectToPage("Type");
    }

    /// <summary>The model for _SubjectSummaryList: the page it's on, and on the summary page, the summary's date row.</summary>
    public sealed record SubjectSummaryListModel(SubjectPageModel Page, SummaryModel? Summary = null);
}
