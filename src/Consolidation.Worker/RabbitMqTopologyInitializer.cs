using Messaging;
using RabbitMQ.Client;

namespace Consolidation.Worker;

// The queue's owning boundary declares the topology; processing/acknowledgements come in #7.
internal sealed class RabbitMqTopologyInitializer(IConnection connection) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        RabbitMqTopology.DeclareAsync(connection, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
