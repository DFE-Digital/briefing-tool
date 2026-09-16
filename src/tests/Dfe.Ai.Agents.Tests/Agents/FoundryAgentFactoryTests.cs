using Azure.AI.Projects.Agents;
using Dfe.Ai.Agents.Factories;
using Dfe.Ai.Agents.Models;
using NSubstitute;
using System.ClientModel;
using System.ClientModel.Primitives;
using Xunit;

namespace Dfe.Ai.Agents.Tests.Agents;

public sealed class FoundryAgentFactoryTests
{
    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly AgentAdministrationClient _admin = Substitute.For<AgentAdministrationClient>();
    private readonly FoundryAgentFactoryOptions _options = new("gpt-5.1");

    private FoundryAgentFactory CreateSut() => new(_admin, _options);

    private void SetUpNoExistingAgent(string name)
    {
        var notFound = NotFoundException();
        _admin.GetAgentAsync(name, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ClientResult<ProjectsAgentRecord>>(notFound));
    }

    private void SetUpExistingAgent(string name, string versionId, string model, string instructions)
    {
        var record = AgentRecordWithLatestVersion(versionId, name, model, instructions);
        _admin.GetAgentAsync(name, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ClientResult.FromValue(record, FakeResponse())));
    }

    private void SetUpCreatedAgentVersion(string name, string versionId)
    {
        var version = ProjectsAgentsModelFactory.ProjectsAgentVersion(id: versionId, name: name, version: "1");
        _admin.CreateAgentVersionAsync(name, Arg.Any<ProjectsAgentVersionCreationOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ClientResult.FromValue(version, FakeResponse())));
    }

    private static PipelineResponse FakeResponse() => Substitute.For<PipelineResponse>();

    private static ClientResultException NotFoundException()
    {
        var response = FakeResponse();
        response.Status.Returns(404);
        return new ClientResultException(response, null!);
    }
     
    private static ProjectsAgentRecord AgentRecordWithLatestVersion(string versionId, string name, string model, string instructions)
    {
        var definition = new DeclarativeAgentDefinition(model) { Instructions = instructions };
        var version = ProjectsAgentsModelFactory.ProjectsAgentVersion(id: versionId, name: name, version: "1", definition: definition);
        var versionJson = ModelReaderWriter.Write(version).ToString();
        var recordJson = "{\"id\":\"rec-1\",\"name\":\"" + name + "\",\"versions\":{\"latest\":" + versionJson + "}}";
        return ModelReaderWriter.Read<ProjectsAgentRecord>(BinaryData.FromString(recordJson))!;
    }

    [Fact]
    public async Task GetOrCreateAsync_CreatesAgent_AndReusesLocalCacheForIdenticalSpec()
    {
        SetUpNoExistingAgent("my-agent");
        SetUpCreatedAgentVersion("my-agent", "id-1");
        var sut = CreateSut();
        var spec = new AgentSpec { Name = "my-agent", Instructions = "Do the thing." };

        var first = await sut.GetOrCreateAsync(spec, cancellationToken);
        var second = await sut.GetOrCreateAsync(spec, cancellationToken);

        Assert.Equal(new AgentReference("id-1", "my-agent"), first);
        Assert.Equal(first, second);
        await _admin.Received(1).CreateAgentVersionAsync("my-agent", Arg.Any<ProjectsAgentVersionCreationOptions>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrCreateAsync_ReusesExistingFoundryAgent_WithoutCreating()
    {
        SetUpExistingAgent("my-agent", "existing-id", _options.DefaultModel, "Do the thing.");

        var sut = CreateSut();
        var spec = new AgentSpec { Name = "my-agent", Instructions = "Do the thing." };

        var result = await sut.GetOrCreateAsync(spec, cancellationToken);

        Assert.Equal(new AgentReference("existing-id", "my-agent"), result);
        await _admin.DidNotReceiveWithAnyArgs().CreateAgentVersionAsync(
            default!, default!, default, cancellationToken);
    }

    [Fact]
    public async Task GetOrCreateAsync_CreatesNewVersion_WhenInstructionsDiffer()
    {
        SetUpExistingAgent("my-agent", "existing-id", _options.DefaultModel, "Old instructions.");
        SetUpCreatedAgentVersion("my-agent", "new-id");

        var sut = CreateSut();
        var spec = new AgentSpec { Name = "my-agent", Instructions = "New instructions." };

        var result = await sut.GetOrCreateAsync(spec, cancellationToken);

        Assert.Equal(new AgentReference("new-id", "my-agent"), result);
    }

    [Fact]
    public async Task GetOrCreateAsync_DoesNotCacheFailure_RetriesOnNextCall()
    {
        SetUpNoExistingAgent("my-agent");
        var version = ProjectsAgentsModelFactory.ProjectsAgentVersion(id: "id-1", name: "my-agent", version: "1");
        _admin.CreateAgentVersionAsync("my-agent", Arg.Any<ProjectsAgentVersionCreationOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<ClientResult<ProjectsAgentVersion>>(new InvalidOperationException("transient failure")),
                Task.FromResult(ClientResult.FromValue(version, FakeResponse())));

        var sut = CreateSut();
        var spec = new AgentSpec { Name = "my-agent", Instructions = "Do the thing." };

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetOrCreateAsync(spec, cancellationToken));

        var result = await sut.GetOrCreateAsync(spec, cancellationToken);

        Assert.Equal(new AgentReference("id-1", "my-agent"), result);
    }

    [Fact]
    public async Task GetOrCreateAsync_ConcurrentCallsForSameSpec_OnlyCreatesOnce()
    {
        SetUpNoExistingAgent("my-agent");
        var gate = new TaskCompletionSource<ClientResult<ProjectsAgentVersion>>();
        var callCount = 0;

        _admin.CreateAgentVersionAsync("my-agent", Arg.Any<ProjectsAgentVersionCreationOptions>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref callCount);
                return gate.Task;
            });

        var sut = CreateSut();
        var spec = new AgentSpec { Name = "my-agent", Instructions = "Do the thing." };

        var calls = Enumerable.Range(0, 10).Select(_ => sut.GetOrCreateAsync(spec, cancellationToken)).ToList();

        await Task.Delay(50, cancellationToken);

        var version = ProjectsAgentsModelFactory.ProjectsAgentVersion(id: "id-1", name: "my-agent", version: "1");
        gate.SetResult(ClientResult.FromValue(version, FakeResponse()));

        var results = await Task.WhenAll(calls);

        Assert.All(results, r => Assert.Equal(new AgentReference("id-1", "my-agent"), r));
        Assert.Equal(1, callCount);
    }
}
