using Dfe.Briefing.Tool.Application.Providers.Interfaces;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using Dfe.Briefing.Tool.Web.Pages.Briefing;
using Dfe.Briefing.Tool.Web.Validation;
using Microsoft.AspNetCore.Mvc;

namespace Dfe.Briefing.Tool.Web.Models;

/// <summary>
/// A page about the school or trust found on Briefing/Find, passed as its type and URN or TRN in the URL. Each page
/// looks its record up again rather than keeping it between requests, so any server instance can answer.
/// </summary>
public abstract class SubjectPageModel(IBriefingSubjectFinder subjectFinder) : SubjectTypePageModel
{
    [BindProperty(SupportsGet = true)]
    public string? UrnOrTrn { get; set; }

    /// <summary>The school or trust, once <see cref="LoadSubjectAsync"/> has found it.</summary>
    public BriefingSubject? Subject { get; private set; }

    /// <returns>Whether the school or trust was found; if not, the page should send the person back to the start.</returns>
    protected async Task<bool> LoadSubjectAsync(CancellationToken cancellationToken)
    {
        // A school has a URN and a trust a TRN, so a reference of the other kind can't be the one chosen.
        var reference = BriefingReference.Normalise(UrnOrTrn);
        var isReferenceOfType = HasValidType && (IsTrust ? BriefingReference.IsTrn(reference) : BriefingReference.IsUrn(reference));

        Subject = isReferenceOfType ? await subjectFinder.FindAsync(reference, IsTrust, cancellationToken) : null;
        return Subject is not null;
    }
}

/// <summary>The model for _SubjectSummaryList: the page it's on, and on the summary page, the summary's date row.</summary>
public sealed record SubjectSummaryListModel(SubjectPageModel Page, SummaryModel? Summary = null);
