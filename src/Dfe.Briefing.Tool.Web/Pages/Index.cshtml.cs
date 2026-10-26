using Dfe.Briefing.Tool.Application.Helpers;
using Dfe.Briefing.Tool.Application.Orchestrators.Interfaces;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using Dfe.Briefing.Tool.Web.Sanitization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Threading.Channels;

namespace Dfe.Briefing.Tool.Web.Pages
{
    public class IndexModel(IBriefingOrchestrator briefingOrchestrator, ILogger<IndexModel> logger) : PageModel
    {
        [BindProperty]
        public BriefingRequest Input { get; set; } = new();

        public string? BriefingOutput { get; private set; }

        public string? ErrorMessage { get; private set; }

        public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var parameters = new BriefingParameters(Input.SchoolUrnOrTrustTrn, Input.SchoolOrTrustName);

            try
            {
                var result = await briefingOrchestrator.GetBriefing(parameters, cancellationToken: cancellationToken);
                BriefingOutput = BriefingHtmlSanitizer.Sanitize(result.Output);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed to generate briefing for {SchoolOrTrustName}", Input.SchoolOrTrustName);
                ErrorMessage = "Sorry, something went wrong generating this briefing. Please try again.";
            }

            return Page();
        }

        /// <summary>
        /// Streams the briefing generation progress and final output to the client using Server-Sent Events (SSE).
        /// </summary>
        public async Task OnGetStreamAsync(string? schoolUrnOrTrustTrn, string? schoolOrTrustName, CancellationToken cancellationToken)
        {
            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";

            if (string.IsNullOrWhiteSpace(schoolUrnOrTrustTrn) || string.IsNullOrWhiteSpace(schoolOrTrustName))
            {
                await WriteEventAsync("error-message", "Enter the school URN or trust TRN and its name.", cancellationToken);
                return;
            }

            var parameters = new BriefingParameters(schoolUrnOrTrustTrn, schoolOrTrustName);
            var channel = Channel.CreateUnbounded<string>();
            var briefingTask = RunBriefingAsync(parameters, channel.Writer, cancellationToken);

            await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken))
            {
                await WriteEventAsync("progress", message, cancellationToken);
            }

            try
            {
                var result = await briefingTask; 
                if (result.Output is { } briefing && briefingOrchestrator.CanAddWebSearchFindings(briefing))
                {
                    var offer = new WebSearchRequest(schoolUrnOrTrustTrn, schoolOrTrustName, briefing, result.City);
                    await WriteEventAsync("web-search-offer", JsonSerializer.Serialize(offer, JsonSerializerHelper.JsonOptions), cancellationToken);
                }

                await WriteEventAsync("complete", BriefingHtmlSanitizer.Sanitize(result.Output) ?? string.Empty, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed to generate briefing for {SchoolOrTrustName}", schoolOrTrustName);
                await WriteEventAsync("error-message", "Sorry, something went wrong generating this briefing. Please try again.", cancellationToken);
            }
        }

        /// <summary>
        /// Runs the web search the person asked for after reading the briefing, and returns the briefing with a More
        /// background information section of its findings added at the end. Only the web search agent runs - the browser sends back the
        /// briefing it was given, which is reused as it is, so this also works whichever server instance answers.
        /// </summary>
        public async Task<IActionResult> OnPostWebSearchAsync([FromBody] WebSearchRequest? request, CancellationToken cancellationToken)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.SchoolUrnOrTrustTrn) || string.IsNullOrWhiteSpace(request.SchoolOrTrustName)
                || string.IsNullOrWhiteSpace(request.Briefing) || request.Briefing.Length > MaxBriefingCharacters
                || !briefingOrchestrator.CanAddWebSearchFindings(request.Briefing))
            {
                return BadRequest(new { error = "This briefing can't be searched for more information. Generate it again and try once more." });
            }

            try
            {
                var parameters = new BriefingParameters(request.SchoolUrnOrTrustTrn, request.SchoolOrTrustName);
                var result = await briefingOrchestrator.AddWebSearchFindings(parameters, request.Briefing, request.City, cancellationToken);
                return new JsonResult(new { html = BriefingHtmlSanitizer.Sanitize(result.Output) ?? string.Empty });
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed to search the web for {SchoolOrTrustName}", request.SchoolOrTrustName);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { error = "Sorry, something went wrong searching the web. Your briefing is unchanged - please try again." });
            }
        }

        // A briefing is well under this; the limit stops an oversized request being sent to the web search agent.
        private const int MaxBriefingCharacters = 300_000; 

        private async Task<AIResult> RunBriefingAsync(BriefingParameters parameters, ChannelWriter<string> writer,
            CancellationToken cancellationToken)
        {
            try
            {
                return await briefingOrchestrator.GetBriefing(parameters, new ChannelProgress(writer), cancellationToken);
            }
            finally
            {
                writer.TryComplete();
            }
        }

        private async Task WriteEventAsync(string eventName, string data, CancellationToken cancellationToken)
        {
            var payload = string.Join('\n', data.ReplaceLineEndings("\n").Split('\n').Select(line => $"data: {line}"));
            await Response.WriteAsync($"event: {eventName}\n{payload}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }

        /// <summary>
        /// Wraps a ChannelWriter to implement IProgress, allowing progress messages to be sent to the client via Server-Sent Events (SSE).
        /// </summary>
        /// <param name="writer">The channel writer to wrap.</param>
        private sealed class ChannelProgress(ChannelWriter<string> writer) : IProgress<string>
        {
            public void Report(string value) => writer.TryWrite(value);
        }
    }

    public sealed class BriefingRequest
    {
        [Required(ErrorMessage = "Enter the school URN or trust TRN")]
        public string SchoolUrnOrTrustTrn { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter the school or trust name")]
        public string SchoolOrTrustName { get; set; } = string.Empty;
    }

    /// <summary>A briefing the person has asked to add web search findings to.</summary>
    public sealed record WebSearchRequest(string SchoolUrnOrTrustTrn, string SchoolOrTrustName, string Briefing, string? City);
}
