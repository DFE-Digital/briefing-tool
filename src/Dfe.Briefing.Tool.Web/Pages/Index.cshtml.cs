using Dfe.Briefing.Tool.Application.Orchestrators.Interfaces;
using Dfe.Briefing.Tool.Domain.ValueObjects;
using Dfe.Briefing.Tool.Web.Sanitization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Threading.Channels;

namespace Dfe.Briefing.Tool.Web.Pages
{
    public class IndexModel(IBriefingOrchestrator briefingOrchestrator, ILogger<IndexModel> logger) : PageModel
    {
        [BindProperty]
        public BriefingRequest Input { get; set; } = new();

        public string? BriefingOutput { get; private set; }

        public string? ErrorMessage { get; private set; }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var parameters = new BriefingParameters(Input.SchoolUrnOrTrustTrn, Input.SchoolOrTrustName, Input.AllowWebSearch);

            try
            {
                var result = await briefingOrchestrator.GetBriefing(parameters, cancellationToken: cancellationToken);
                BriefingOutput = BriefingHtmlSanitizer.Sanitize(result.Output);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to generate briefing for {SchoolOrTrustName}", Input.SchoolOrTrustName);
                ErrorMessage = "Sorry, something went wrong generating this briefing. Please try again.";
            }

            return Page();
        }

        /// <summary>
        /// Streams the briefing generation progress and final output to the client using Server-Sent Events (SSE).
        /// </summary>
        public async Task OnGetStreamAsync(string? schoolUrnOrTrustTrn, string? schoolOrTrustName, bool allowWebSearch,
            CancellationToken cancellationToken)
        {
            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";

            if (string.IsNullOrWhiteSpace(schoolUrnOrTrustTrn) || string.IsNullOrWhiteSpace(schoolOrTrustName))
            {
                await WriteEventAsync("error-message", "Enter the school URN or trust TRN and its name.", cancellationToken);
                return;
            }

            var parameters = new BriefingParameters(schoolUrnOrTrustTrn, schoolOrTrustName, allowWebSearch);
            var channel = Channel.CreateUnbounded<string>();
            var briefingTask = RunBriefingAsync(parameters, channel.Writer, cancellationToken);

            await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken))
            {
                await WriteEventAsync("progress", message, cancellationToken);
            }

            try
            {
                var result = await briefingTask;
                await WriteEventAsync("complete", BriefingHtmlSanitizer.Sanitize(result.Output) ?? string.Empty, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to generate briefing for {SchoolOrTrustName}", schoolOrTrustName);
                await WriteEventAsync("error-message", "Sorry, something went wrong generating this briefing. Please try again.", cancellationToken);
            }
        }

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

        public bool AllowWebSearch { get; set; }
    }
}
