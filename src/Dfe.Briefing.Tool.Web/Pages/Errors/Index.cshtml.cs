using Dfe.Briefing.Tool.Web.Constants;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Dfe.Briefing.Tool.Web.Pages.Errors;

public class IndexModel(ILogger<IndexModel> logger) : PageModel
{
    public string RequestId { get; private set; } = string.Empty;

    public string ErrorMessage { get; private set; } = ErrorMessages.ProblemWithService;
     
    public bool IsPageNotFound { get; private set; }

    public void OnGet(int? statusCode = null)
    {
        ManageErrors(statusCode);
    }

    public void OnPost(int? statusCode = null)
    {
        ManageErrors(statusCode);
    }

    private void ManageErrors(int? statusCode)
    {
        RequestId = HttpContext.TraceIdentifier;
        if (!statusCode.HasValue)
        {
            ManageUnhandledErrors();
        }
        else
        {
            ErrorMessage = statusCode.Value switch
            {
                404 => ErrorMessages.PageNotFound,
                500 => ErrorMessages.ProblemWithService,
                501 => ErrorMessages.NotImplemented,
                _ => $"Error {statusCode}"
            };
            logger.LogError(ErrorMessages.ErrorPageLogTemplate, RequestId, ErrorMessage);
        }

        IsPageNotFound = statusCode == StatusCodes.Status404NotFound || ErrorMessage == ErrorMessages.PageNotFound;
    }

    private void ManageUnhandledErrors()
    {
        var unhandledError = HttpContext.Features.Get<IExceptionHandlerPathFeature>()?.Error;
        logger.LogError(ErrorMessages.ErrorPageUnhandledLogTemplate, RequestId, unhandledError?.Message);
         
        if (unhandledError is InvalidOperationException && unhandledError.Message.Contains("no page named", StringComparison.CurrentCultureIgnoreCase))
        {
            ErrorMessage = ErrorMessages.PageNotFound;
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }
}