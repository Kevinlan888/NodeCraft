using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NodeCraft.Flow;
using NodeCraft.PluginSample.PrivateDependency;

public sealed class ValidFixturePlugin : IFlowPlugin
{
    public PluginMetadata Metadata { get; } = new PluginMetadata
    {
        Id = "test.valid.plugin",
        DisplayName = "Valid Test Plugin",
        Version = new Version(2, 0, 0),
    };

    public void Register(IPluginContext context)
    {
        context.Nodes.Register(FixtureRegistrationFactory.CreateRegistration("test.valid.node"));
        context.Logger.LogInformation(PrivateValueFormatter.Format("fixture loaded"));
    }
}

public sealed class ThrowingFixturePlugin : IFlowPlugin
{
    public PluginMetadata Metadata { get; } = new PluginMetadata
    {
        Id = "test.throwing.plugin",
        DisplayName = "Throwing Test Plugin",
        Version = new Version(1, 0, 0),
    };

    public void Register(IPluginContext context)
    {
        context.Nodes.Register(FixtureRegistrationFactory.CreateRegistration("test.throwing.node"));
        throw new InvalidOperationException("fixture registration failed");
    }
}

public sealed class MissingDependencyFixturePlugin : IFlowPlugin
{
    public PluginMetadata Metadata { get; } = new PluginMetadata
    {
        Id = "test.failed-duplicate.plugin",
        DisplayName = "Missing Dependency Test Plugin",
        Version = new Version(1, 0, 0),
    };

    public void Register(IPluginContext context)
    {
        context.Logger.LogInformation(PrivateValueFormatter.Format("fixture loaded"));
        context.Nodes.Register(FixtureRegistrationFactory.CreateRegistration("test.missing-dependency.node"));
    }
}

public sealed class DuplicateFailedIdFixturePlugin : IFlowPlugin
{
    public PluginMetadata Metadata { get; } = new PluginMetadata
    {
        Id = "test.failed-duplicate.plugin",
        DisplayName = "Duplicate Failed Id Test Plugin",
        Version = new Version(1, 0, 0),
    };

    public void Register(IPluginContext context)
    {
        context.Nodes.Register(FixtureRegistrationFactory.CreateRegistration("test.failed-duplicate.late.node"));
    }
}

public sealed class DuplicateFixturePlugin : IFlowPlugin
{
    public PluginMetadata Metadata { get; } = new PluginMetadata
    {
        Id = "test.duplicate.plugin",
        DisplayName = "Duplicate Test Plugin",
        Version = new Version(1, 0, 0),
    };

    public void Register(IPluginContext context)
    {
        context.Nodes.Register(FixtureRegistrationFactory.CreateRegistration("test.valid.node"));
    }
}

internal sealed class FixtureNodeModel : NodeModel
{
    public FixtureNodeModel(string executorType)
    {
        ExecutorType = executorType;
        Name = "Fixture";
    }
}

internal sealed class FixtureExecutor : IFlowNodeExecutor
{
    public Task<IReadOnlyDictionary<string, object>> ExecuteAsync(
        FlowExecutionContext context,
        WorkflowNode node,
        FlowNodeDefinition definition,
        IReadOnlyDictionary<string, object> inputs,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyDictionary<string, object>>(
            new Dictionary<string, object>());
    }
}

internal static class FixtureRegistrationFactory
{
    public static FlowNodeRegistration CreateRegistration(string typeKey)
    {
        return new FlowNodeRegistration(
            new FlowNodeDefinition
            {
                TypeKey = typeKey,
                DisplayName = "Fixture",
                Category = "Tests",
            },
            () => new FixtureExecutor())
        {
            NodeModelType = typeof(FixtureNodeModel),
            NodeFactory = () => new FixtureNodeModel(typeKey),
            PaletteDisplayName = "Fixture",
            PaletteDescription = "Test fixture node",
        };
    }
}
