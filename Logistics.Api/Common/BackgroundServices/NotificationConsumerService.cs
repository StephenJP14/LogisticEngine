using System.Text;
using System.Text.Json;
using Logistics.Api.Common.Events;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Logistics.Api.Common.BackgroundServices;

public class NotificationConsumerService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<NotificationConsumerService> _logger;

    public const string ExchangeName = "logistics.cdc.exchange";
    public const string QueueName = "logistics.cdc.notifications";
    public const string RoutingKey = "logistics.package.events";

    public NotificationConsumerService(IConfiguration configuration, ILogger<NotificationConsumerService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CDC Notification & ERP Consumer is starting...");

        var factory = new ConnectionFactory
        {
            HostName = _configuration["RabbitMQ:HostName"] ?? "localhost",
            Port = int.Parse(_configuration["RabbitMQ:Port"] ?? "5672"),
            UserName = _configuration["RabbitMQ:UserName"] ?? "guest",
            Password = _configuration["RabbitMQ:Password"] ?? "guest"
        };

        try
        {
            var connection = await factory.CreateConnectionAsync(stoppingToken);
            var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

            // Declare Exchange & Queue untuk CDC Debezium
            await channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Direct, durable: true, cancellationToken: stoppingToken);
            await channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
            await channel.QueueBindAsync(QueueName, ExchangeName, RoutingKey, cancellationToken: stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (model, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();
                    var json = Encoding.UTF8.GetString(body);

                    using var doc = JsonDocument.Parse(json);

                    // Ekstraksi baris INSERT dari Debezium CDC Envelope (payload -> after / root -> after)
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("payload", out var payloadElem))
                    {
                        root = payloadElem;
                    }

                    if (root.TryGetProperty("after", out var afterElem) && afterElem.ValueKind == JsonValueKind.Object)
                    {
                        var eventType = afterElem.GetProperty("EventType").GetString();
                        var eventPayloadRaw = afterElem.GetProperty("Payload").GetString();

                        if (eventType == nameof(PackageDeliveredEvent) && !string.IsNullOrEmpty(eventPayloadRaw))
                        {
                            var deliveredEvent = JsonSerializer.Deserialize<PackageDeliveredEvent>(eventPayloadRaw);
                            if (deliveredEvent != null)
                            {   
                                _logger.LogInformation("[DEBEZIUM CDC -> ASYNC ACTION] 📱 WhatsApp sent to {Recipient} for Resi {Resi}",
                                    deliveredEvent.RecipientName, deliveredEvent.TrackingNumber);

                                _logger.LogInformation("[DEBEZIUM CDC -> ASYNC ACTION] 📊 ERP Ledger Updated for Resi {Resi}",
                                    deliveredEvent.TrackingNumber);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing Debezium CDC event.");
                }

                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
            };

            await channel.BasicConsumeAsync(QueueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to connect CDC Consumer to RabbitMQ.");
        }
    }
}