using System.Text;
using Logistics.Api.Common.Database;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace Logistics.Api.Common.BackgroundServices;

public class OutboxPublisherService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OutboxPublisherService> _logger;

    public const string QueueName = "logistics.package.events";

    public OutboxPublisherService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<OutboxPublisherService> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox Publisher Worker is starting...");

        var factory = new ConnectionFactory
        {
            HostName = _configuration["RabbitMQ:HostName"] ?? "localhost",
            Port = int.Parse(_configuration["RabbitMQ:Port"] ?? "5672"),
            UserName = _configuration["RabbitMQ:UserName"] ?? "guest",
            Password = _configuration["RabbitMQ:Password"] ?? "guest"
        };

        IConnection? connection = null;
        IChannel? channel = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (connection == null || !connection.IsOpen)
                {
                    connection = await factory.CreateConnectionAsync(stoppingToken);
                    channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                    await channel.QueueDeclareAsync(
                        queue: QueueName,
                        durable: true,
                        exclusive: false,
                        autoDelete: false,
                        arguments: null,
                        cancellationToken: stoppingToken);
                }

                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var pendingMessages = await db.OutboxMessages
                    .Where(m => m.ProcessedAtUtc == null)
                    .OrderBy(m => m.CreatedAtUtc)
                    .Take(20)
                    .ToListAsync(stoppingToken);

                if (pendingMessages.Any())
                {
                    // Tambahkan pengecekan null ini agar compiler tenang
                    if (channel == null) continue;

                    foreach (var msg in pendingMessages)
                    {
                        var body = Encoding.UTF8.GetBytes(msg.Payload);
                        var props = new BasicProperties
                        {
                            Type = msg.EventType,
                            Persistent = true
                        };

                        // Tambahkan tanda seru (!) setelah channel
                        await channel!.BasicPublishAsync(
                            exchange: string.Empty,
                            routingKey: QueueName,
                            mandatory: false,
                            basicProperties: props,
                            body: body,
                            cancellationToken: stoppingToken);

                        msg.ProcessedAtUtc = DateTime.UtcNow;
                        
                        if (_logger.IsEnabled(LogLevel.Information))
                        {
                            _logger.LogInformation("Outbox event published to RabbitMQ: {EventType} ({Id})", msg.EventType, msg.Id);
                        }
                    }

                    await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox messages or connecting to RabbitMQ.");
            }

            await Task.Delay(3000, stoppingToken); // Polling interval 3 detik
        }
    }
}