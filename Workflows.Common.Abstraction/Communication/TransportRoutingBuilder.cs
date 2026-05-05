using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;




namespace Workflows.Common.Abstraction.Communication
{
    public class TransportRoutingBuilder
    {
        private Type _defaultTransport;
        private Type _defaultSubscriber;
        internal List<TransportRule> Rules { get; } = new();

        // 1. Configure the Fallback
        public Communication.TransportRoutingBuilder UseDefault<TTransport, TSubscriber>()
            where TTransport : Communication.IMessageTransport
            where TSubscriber : IMessageSubscriber
        {
            _defaultTransport = typeof(TTransport);
            _defaultSubscriber = typeof(TSubscriber);
            return this;
        }

        // 2. Start a Rule for a specific Type
        public Communication.TransportRoutingBuilder.RuleBuilder<TMessage> ForMessage<TMessage>()
        {
            return new Communication.TransportRoutingBuilder.RuleBuilder<TMessage>(this);
        }

        internal Type GetDefaultTransport() => _defaultTransport
            ?? throw new InvalidOperationException("No default transport configured.");

        internal Type GetDefaultSubscriber() => _defaultSubscriber
            ?? throw new InvalidOperationException("No default subscriber configured.");

        // Helper class for the Fluent API
        public class RuleBuilder<TMessage>
        {
            private readonly Communication.TransportRoutingBuilder _parent;
            private Func<object, bool> _compiledCondition = _ => true; // Default: match all values of this type

            internal RuleBuilder(Communication.TransportRoutingBuilder parent) => _parent = parent;

            // The Expression Tree: Compiles at startup!
            public Communication.TransportRoutingBuilder.RuleBuilder<TMessage> When(Expression<Func<TMessage, bool>> conditionExpr)
            {
                var compiledFunc = conditionExpr.Compile();
                // Wrap it so it takes an object (for fast runtime evaluation)
                _compiledCondition = obj => obj is TMessage msg && compiledFunc(msg);
                return this;
            }

            public Communication.TransportRoutingBuilder Use<TTransport, TSubscriber>(string address)
                where TTransport : Communication.IMessageTransport
                where TSubscriber : IMessageSubscriber
            {
                _parent.Rules.Add(new TransportRule(
                    typeof(TMessage),
                    _compiledCondition,
                    typeof(TTransport),
                    typeof(TSubscriber),
                    address)); // <--- Save it to the rule

                return _parent;
            }
        }
    }
}