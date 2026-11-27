using Dfe.Briefing.Tool.Application.Providers.Interfaces;
using Dfe.Briefing.Tool.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace Dfe.Briefing.Tool.Web.Pages.Briefing;

/// <summary>
/// The third step: the person checks the record found is the school or trust they meant, then confirms it or
/// chooses a different one.
/// </summary>
public class ConfirmModel(IBriefingSubjectFinder subjectFinder) : SubjectPageModel(subjectFinder)
{
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
        => await LoadSubjectAsync(cancellationToken) ? Page() : RedirectToStart();
}
