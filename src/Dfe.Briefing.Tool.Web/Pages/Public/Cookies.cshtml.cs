using Dfe.Briefing.Tool.Web.Models;
using Dfe.Briefing.Tool.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dfe.Briefing.Tool.Web.Pages.Public;

[AllowAnonymous]
public class Cookies(IAnalyticsConsentService analyticsConsentService) : PageModel
{
    public const string IsRubyRequestTempDataKey = "IsRubyRequest";
    public const string PreferencesSetTempDataKey = "PreferencesSet";

    public bool? Consent { get; set; }
    public bool PreferencesSet { get; set; } = false;

    public string ReturnPath { get; set; } = string.Empty;

    public string TransfersCookiesUrl { get; set; } = string.Empty;

    public ActionResult OnGet(bool? consent, string returnUrl)
    {
        ReturnPath = Url.IsLocalUrl(returnUrl) ? returnUrl : "/";

        Consent = analyticsConsentService.ConsentValue();

        if (consent.HasValue)
        {
            PreferencesSet = true;
            if (TempData[IsRubyRequestTempDataKey] == null)
            {
                TempData[PreferencesSetTempDataKey] = true;
            }
            else
            {
                Response.Headers.Append("x-preference-set", "dotnet");
            }

            ApplyCookieConsent(consent.Value);

            if (!string.IsNullOrEmpty(returnUrl))
            {
                return Redirect(ReturnPath);
            }

            return RedirectToPage(Links.Public.CookiePreferences);
        }

        return Page();
    }

    public IActionResult OnPost(bool? consent, string returnUrl, [FromForm(Name = "cookies_form[accept_optional_cookies]")] bool? cookiesConsent)
    {
        ReturnPath = Url.IsLocalUrl(returnUrl) ? returnUrl : "/";

        if (!consent.HasValue)
        {
            consent = cookiesConsent;
        }

        Consent = analyticsConsentService.ConsentValue();

        if (consent.HasValue)
        {
            Consent = consent;
            PreferencesSet = true;

            ApplyCookieConsent(consent.Value);

            if (cookiesConsent != null)
            {
                TempData[IsRubyRequestTempDataKey] = false;
                return Redirect($"/cookies?consent={cookiesConsent}&returnUrl={ReturnPath}");
            }
            else
            {
                if (TempData[PreferencesSetTempDataKey] != null)
                {
                    TempData[PreferencesSetTempDataKey] = null;
                }

                if (TempData[IsRubyRequestTempDataKey] != null)
                {
                    TempData[IsRubyRequestTempDataKey] = null;
                }
            }
        }

        return Page();
    }



    private void ApplyCookieConsent(bool consent)
    {
        if (consent)
        {
            analyticsConsentService.AllowConsent();
        }
        else
        {
            analyticsConsentService.DenyConsent();
        }
    }
}