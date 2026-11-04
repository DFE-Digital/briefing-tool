namespace Dfe.Briefing.Tool.Web.Services
{
    public interface IAnalyticsConsentService
    {
        void AllowConsent();
        bool? ConsentValue();
        void DenyConsent();
        bool HasConsent();
    }

    public class AnalyticsConsentService : IAnalyticsConsentService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        // Named for this app: another DfE service on the same host (e.g. localhost) can set ACCEPT_OPTIONAL_COOKIES,
        // which would hide this app's banner before the person has chosen.
        public const string ConsentCookieName = "BriefingCookieConsent";
        private bool? Consent { get; set; }
        private readonly string _analyticsDomain = ".education.gov.uk";

        private readonly ILogger<AnalyticsConsentService> _logger;

        public AnalyticsConsentService(IHttpContextAccessor httpContextAccessor, IConfiguration configuration, ILogger<AnalyticsConsentService> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
            var domain = configuration["GoogleAnalytics:Domain"];
            if (!string.IsNullOrEmpty(domain))
            {
                _analyticsDomain = domain;
            }
        }

        public bool? ConsentValue()
        {
            if (Consent.HasValue)
            {
                return Consent;
            }

            if (_httpContextAccessor.HttpContext != null && _httpContextAccessor.HttpContext.Request.Cookies.ContainsKey(ConsentCookieName))
            {
                return bool.Parse(_httpContextAccessor.HttpContext.Request.Cookies[ConsentCookieName] ?? string.Empty);
            }

            return false;
        }

        public bool HasConsent()
        {
            return ConsentValue() ?? false;
        }

        public void AllowConsent()
        {
            SetConsent(true);
        }

        public void DenyConsent()
        {
            SetConsent(false);
        }

        private void SetConsent(bool consent)
        {
            Consent = consent;
            var cookieOptions = new CookieOptions { Expires = DateTime.Today.AddYears(1), Secure = true, HttpOnly = true };
            _httpContextAccessor.HttpContext!.Response.Cookies.Append(ConsentCookieName, consent.ToString().ToLower(), cookieOptions);
            var request = _httpContextAccessor.HttpContext.Request;

            if (!consent)
            {
                foreach (var cookie in request.Cookies.Keys)
                {
                    if (cookie.StartsWith("_ga") || cookie.Equals("_gid"))
                    {
                        _logger.LogInformation("Expiring Google analytics cookie: {Cookie}", cookie);
                        //Delete if domain is the same
                        _httpContextAccessor.HttpContext.Response.Cookies.Delete(cookie);
                        //Delete if domain matches - need both as we wont be sent the cookie if the domain doesnt match
                        _httpContextAccessor.HttpContext.Response.Cookies.Delete(cookie, new CookieOptions() { Domain = _analyticsDomain, Secure = true, HttpOnly = true });
                    }

                    // App Insights
                    if (cookie.StartsWith("ai_"))
                    {
                        _logger.LogInformation("Expiring App insights cookie: {Cookie}", cookie);
                        _httpContextAccessor.HttpContext.Response.Cookies.Append(cookie, string.Empty, new CookieOptions { Expires = DateTime.Now.AddYears(-1), Secure = true, SameSite = SameSiteMode.Lax, HttpOnly = true });
                    }
                }
            }
        }
    }

}