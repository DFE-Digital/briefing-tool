using Dfe.Briefing.Tool.Web.Constants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dfe.Briefing.Tool.Web.Pages.Briefing
{
    /// <summary>The first step: whether the summary is of a school or other establishment, or of a trust.</summary>
    public class TypeModel : PageModel
    {
        public const string NoTypeChosen = "Select the type of establishment to summarise";

        [BindProperty(SupportsGet = true)]
        public string? Type { get; set; }

        public void OnGet()
        {
            // Type, if given, is the choice made before: the person came back to change it.
        }

        public IActionResult OnPost()
        {
            if (!SubjectTypes.IsValid(Type))
            {
                ModelState.AddModelError(nameof(Type), NoTypeChosen);
                return Page();
            }

            return RedirectToPage("Find", new { type = Type });
        }
    }
}
