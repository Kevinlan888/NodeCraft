using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace NodeCraft.Flow
{
    public interface IPluginContext
    {
        IPluginNodeRegistrar Nodes { get; }

        ILogger Logger { get; }

        Version HostApiVersion { get; }
    }

    public sealed class PluginRegistrationContext : IPluginContext, IPluginNodeRegistrar
    {
        private readonly List<FlowNodeRegistration> _registrations = new List<FlowNodeRegistration>();
        private readonly HashSet<string> _typeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public PluginRegistrationContext(ILogger logger, Version hostApiVersion)
        {
            Logger = logger ?? NullLogger.Instance;
            HostApiVersion = hostApiVersion ?? throw new ArgumentNullException(nameof(hostApiVersion));
            Nodes = this;
        }

        public IPluginNodeRegistrar Nodes { get; }

        public ILogger Logger { get; }

        public Version HostApiVersion { get; }

        public IReadOnlyList<FlowNodeRegistration> Registrations => _registrations;

        public void Register(FlowNodeRegistration registration)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            var typeKey = registration.Definition?.TypeKey ?? string.Empty;
            if (!_typeKeys.Add(typeKey))
            {
                throw new InvalidOperationException(
                    $"Plugin registration already staged node type '{registration.Definition?.TypeKey}'.");
            }

            _registrations.Add(registration);
        }
    }
}
