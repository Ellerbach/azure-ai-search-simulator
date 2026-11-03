namespace AzureAISearchSimulator.Api.Tests.Controllers;

/// <summary>
/// Test classes that start the simulator in-process (WebApplicationFactory) share this collection
/// so they run one after another: two hosts starting at the same time race on process-wide state
/// set up at startup (e.g. LiteDB's global BsonMapper), failing with "The entry point exited
/// without ever building an IHost" or BsonMapper errors.
/// </summary>
[CollectionDefinition(Name)]
public class SimulatorHostCollection
{
    public const string Name = "Simulator host";
}
