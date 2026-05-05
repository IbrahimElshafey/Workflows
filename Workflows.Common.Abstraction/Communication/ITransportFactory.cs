namespace Workflows.Shared.Communication
{
    public interface ITransportFactory
    {
        /// <summary>
        /// Evaluates the message instance against compiled rules to find the correct transport.
        /// Falls back to the default if no rules match.
        /// </summary>
        IMessageTransport GetTransport<T>(T message);

        /// <summary>
        /// Resolves the correct subscriber based strictly on the message type.
        /// </summary>
        IMessageSubscriber GetSubscriber<T>();
    }
}