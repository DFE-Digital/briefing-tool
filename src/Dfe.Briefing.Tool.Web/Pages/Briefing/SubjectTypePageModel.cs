using Dfe.Briefing.Tool.Web.Constants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dfe.Briefing.Tool.Web.Pages.Briefing
{
    /// <summary>
    /// A page after Briefing/Type: the type the person chose there, school or trust, is passed on in each page's URL so
    /// every later step searches for and labels that type, rather than working it out from what was entered.
    /// </summary>
    public abstract class SubjectTypePageModel : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public string? Type { get; set; }

        public bool HasValidType => SubjectTypes.IsValid(Type);

        public bool IsTrust => Type == SubjectTypes.Trust;

        public string SubjectNoun => IsTrust ? "trust" : "school";

        public string NameLabel => IsTrust ? "Trust name" : "School name";

        public string ReferenceLabel => IsTrust ? "TRN" : "URN";

        protected IActionResult RedirectToStart() => RedirectToPage("Type");
    }
}
