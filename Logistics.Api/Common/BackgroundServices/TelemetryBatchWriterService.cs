using Logistics.Api.Common.Database;
using Logistics.Api.Common.Entities;
using Logistics.Api.Common.Telemetry;

namespace Logistics.Api.Common.BackgroundServices;

public class TelemetryBatchWriterService : BackgroundService
{
    private readonly TelemetryBuffer _buffer;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TelemetryBatchWriterService> _logger;

    public TelemetryBatchWriterService(
        TelemetryBuffer buffer,
        IServiceProvider serviceProvider,
        ILogger<TelemetryBatchWriterService> logger)
    {
        _buffer = buffer;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<VehicleTelemetryLog>(500);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Kumpulkan hingga 500 log atau timeout 3 detik
                var timeoutTask = Task.Delay(3000, stoppingToken);

                while (batch.Count < 500)
                {
                    if (_buffer.Reader.TryRead(out var log))
                    {
                        batch.Add(log);
                    }
                    else
                    {
                        var readTask = _buffer.Reader.WaitToReadAsync(stoppingToken).AsTask();
                        var completed = await Task.WhenAny(readTask, timeoutTask);
                        if (completed == timeoutTask || !readTask.Result) break;
                    }
                }

                if (batch.Count > 0)
                {
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    // Batch Bulk Insert dalam 1 kali kueri ke PostgreSQL
                    await db.VehicleTelemetryLogs.AddRangeAsync(batch, stoppingToken);
                    await db.SaveChangesAsync(stoppingToken);

                    _logger.LogInformation("Flushed {Count} telemetry records to PostgreSQL in batch.", batch.Count);
                    batch.Clear();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error occurred while flushing telemetry batch to database.");
            }
        }
    }
}