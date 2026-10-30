namespace CashFlowEntryService.Infrastructure.Messaging;

/// <summary>
/// Owns a single long-lived connection/channel to publish `CashFlowEntryRecorded.v1` events with
/// publisher confirmations. Exchange declaration is idempotent, so startup order relative to
/// the ConsolidationService consumer never matters (ADR-002).
/// </summary>
public sealed class RabbitMqEventPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqEventPublisher> logger)
    : IEventPublisher, IAsyncDisposable
{
    // Bounds the TCP/socket-level connection attempt itself (RabbitMQ.Client's
    // ConnectionFactory.RequestedConnectionTimeout — "timeout setting for connection attempts").
    // Without this, an unreachable host (network partition, not "connection refused") falls back
    // to the OS socket-connect default, which can be minutes, not seconds — exactly the failure
    // mode OutboxPublisherService's per-message CancellationToken (Outbox:PublishTimeoutMs) also
    // guards against from the caller's side; the two are layered on purpose.
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(5);

    private readonly RabbitMqOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync(Guid eventId, string routingKey, string jsonPayload, string? correlationId, CancellationToken cancellationToken)
    {
        var channel = await GetOrCreateChannelAsync(cancellationToken);

        var headers = new Dictionary<string, object?>
        {
            [CashFlowTopology.RetryCountHeader] = 0
        };

        if (correlationId is not null)
        {
            headers[CashFlowTopology.CorrelationIdHeader] = correlationId;
        }

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = eventId.ToString(),
            Headers = headers
        };

        var body = Encoding.UTF8.GetBytes(jsonPayload);

        // mandatory: true + publisher confirmation tracking (enabled on the channel below) makes
        // RabbitMQ.Client 7 await this call until the broker confirms the message, and throw
        // RabbitMQ.Client.Exceptions.PublishException (or its subtype PublishReturnException for a
        // basic.return, e.g. no queue bound to this routing key) instead of silently acking an
        // unroutable publish (ADR-003). The caller (OutboxPublisherService) classifies and retries.
        await channel.BasicPublishAsync(
            exchange: CashFlowTopology.EventsExchange,
            routingKey: routingKey,
            mandatory: true,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }

    private async Task<IChannel> GetOrCreateChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_channel is { IsOpen: true })
            {
                return _channel;
            }

            // A partial reconnect (e.g. the broker dropped the connection between calls): the
            // channel above is no longer open, but `_connection`/`_channel` can still hold the
            // previous, now-dead objects. Close and dispose them before replacing the fields —
            // this instance is a long-lived singleton, so overwriting them without closing first
            // would leak the old IConnection (and its AutomaticRecoveryEnabled background
            // threads/sockets) on every single reconnect, not just once.
            await CloseStaleConnectionAsync();

            logger.LogInformation("Abrindo conexão com o RabbitMQ em {HostName}:{Port}", _options.HostName, _options.Port);

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                AutomaticRecoveryEnabled = true,
                RequestedConnectionTimeout = ConnectionTimeout
            };

            _connection = await factory.CreateConnectionAsync("cashflowentryservice-outbox-publisher", cancellationToken);
            _channel = await _connection.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                cancellationToken);

            await _channel.ExchangeDeclareAsync(
                exchange: CashFlowTopology.EventsExchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken);

            return _channel;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Best-effort close of whatever this instance still holds from a previous connection
    /// attempt. The old connection/channel are already unusable by the time this runs (that is
    /// why <see cref="GetOrCreateChannelAsync"/> is reconnecting), so a failure here is logged and
    /// swallowed — the fields are cleared either way, and the caller immediately replaces them.
    /// </summary>
    private async Task CloseStaleConnectionAsync()
    {
        if (_channel is not null)
        {
            try
            {
                await _channel.CloseAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao fechar o channel RabbitMQ de uma reconexão anterior; descartando mesmo assim.");
            }
            finally
            {
                _channel.Dispose();
                _channel = null;
            }
        }

        if (_connection is not null)
        {
            try
            {
                await _connection.CloseAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao fechar a conexão RabbitMQ de uma reconexão anterior; descartando mesmo assim.");
            }
            finally
            {
                _connection.Dispose();
                _connection = null;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync();
            _channel.Dispose();
        }

        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }

        _gate.Dispose();
    }
}
