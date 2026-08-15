using System;
using System.Collections.Generic;

namespace NodeCraft.Flow
{
    internal sealed class SessionValueStore
    {
        private readonly Dictionary<Tuple<string, int>, object> _values
            = new Dictionary<Tuple<string, int>, object>();
        private bool _sealed;

        internal IReadOnlySessionValueStore CreateReadOnlyView()
        {
            return new ReadOnlySessionValueStore(this);
        }

        internal void SetPortValue(string nodeId, int outputSlot, object value)
        {
            if (_sealed)
            {
                throw new InvalidOperationException("Session value store is sealed.");
            }

            var key = Tuple.Create(nodeId, outputSlot);
            if (_values.ContainsKey(key))
            {
                throw new InvalidOperationException(
                    $"Session output '{nodeId}' slot {outputSlot} was already initialized.");
            }

            _values.Add(key, value);
        }

        internal void Seal()
        {
            _sealed = true;
        }

        internal void Clear()
        {
            _values.Clear();
            _sealed = true;
        }

        private bool TryGetPortValue(string nodeId, int outputSlot, out object value)
        {
            return _values.TryGetValue(Tuple.Create(nodeId, outputSlot), out value);
        }

        private sealed class ReadOnlySessionValueStore : IReadOnlySessionValueStore
        {
            private readonly SessionValueStore _owner;

            internal ReadOnlySessionValueStore(SessionValueStore owner)
            {
                _owner = owner;
            }

            public bool TryGetPortValue(string nodeId, int outputSlot, out object value)
            {
                return _owner.TryGetPortValue(nodeId, outputSlot, out value);
            }
        }
    }
}
