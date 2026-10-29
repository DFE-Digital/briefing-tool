using Dfe.Briefing.Tool.Infrastructure;
using Dfe.Briefing.Tool.Infrastructure.Constants;
using Dfe.Briefing.Tool.Infrastructure.Extensions;
using Dfe.Briefing.Tool.ReleaseGate;
using Dfe.Briefing.Tool.ReleaseGate.Commands;
using Dfe.Briefing.Tool.ReleaseGate.Constants;
using Dfe.Briefing.Tool.ReleaseGate.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (args.Length == 0 && !Console.IsInputRedirected)
    args = ChooseCommand();

var command = args.FirstOrDefault() ?? CommandNames.Gate;
var options = args.Skip(1).ToArray();

if (command == CommandNames.ExportCopilotStudio)
    return await ExportCopilotStudioCommand.RunAsync(options.FirstOrDefault() ?? CommandNames.DefaultExportFolder, CancellationToken.None);

if (command != CommandNames.Gate)
{
    await Console.Error.WriteLineAsync($"""
        Usage:
          {CommandNames.Gate}                           Check the guardrail and run every test case (exit 1 on failure)
          {CommandNames.ExportCopilotStudio} [folder]   Write the evaluation and guardrail test cases as Copilot Studio test sets
        """);
    return 2;
}

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    ContentRootPath = AppContext.BaseDirectory,
    DisableDefaults = true
});

// The settings shared with the web app first, then the gate's own; secrets come from user secrets locally, or
// environment variables in a pipeline (e.g. AiAgents__Authentication__ClientSecret).
builder.Configuration
    .AddSharedSettings()
    .AddJsonFile(Settings.AppSettingsFile, optional: false)
    .AddUserSecrets(typeof(GateCommand).Assembly, optional: true)
    .AddEnvironmentVariables();

// The gate runs the cases prepared from TestCases, each with its prompt built by the app (see TestCasePreparer).
var preparedCases = Path.Combine(Path.GetTempPath(), Settings.PreparedCasesFolder, Guid.NewGuid().ToString(Telemetry.RunIdFormat));
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    [Settings.TestCasesDirectoryKey] = preparedCases
});

builder.Services.AddInfrastructure(builder.Configuration, agents => agents
    .AddQualityEvaluation()
    .AddReleaseGate());

// Same cloud_RoleName as the web app; its own cloud_RoleInstance tells the gate's telemetry apart.
var telemetryOn = builder.AddApplicationInsights(Telemetry.RoleInstance);

using var host = builder.Build();
var services = host.Services;
if (telemetryOn)
    services.StartTelemetry();
else
    Console.WriteLine($"{TelemetrySettings.ConnectionStringKey} isn't set, so results aren't sent to Application Insights.");

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var cancellationToken = cancellation.Token;

// Anything that stops the run is logged as an error, so Application Insights shows why a release has no results.
var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(GateCommand).FullName ?? nameof(GateCommand));

try
{
    await TestCasePreparer.PrepareAsync(services, preparedCases, cancellationToken);
    return await GateCommand.RunAsync(services, cancellationToken);
}
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    await Console.Error.WriteLineAsync("Cancelled.");
    return 2;
}
catch (InvalidDataException ex)
{
    await Console.Error.WriteLineAsync(ReleaseLog.TestCaseFileWrong + ex.Message);
    logger.LogError(ex, ReleaseLog.StoppedTemplate, ReleaseLog.TestCaseFileWrong + ex.Message);
    return 2;
}
catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
{
    await Console.Error.WriteLineAsync($"The release gate stopped: {ex.Message}");
    logger.LogError(ex, ReleaseLog.StoppedTemplate, ex.Message);
    return 2;
}
finally
{
    if (telemetryOn)
        services.FlushTelemetry();

    if (Directory.Exists(preparedCases))
        Directory.Delete(preparedCases, recursive: true);
}

static string[] ChooseCommand()
{
    Console.WriteLine("""
        What do you want to do?
          1. Run the release gate (guardrail and evaluation)
          2. Export the test cases as Copilot Studio test sets
        """);

    while (true)
    {
        Console.Write("Choose 1 or 2: ");
        switch (Console.ReadLine()?.Trim())
        { 
            case "1": return [CommandNames.Gate];
            case "2": return [CommandNames.ExportCopilotStudio, CommandNames.DefaultExportFolder];
            case null: return [CommandNames.Gate];
        }
    }
}
