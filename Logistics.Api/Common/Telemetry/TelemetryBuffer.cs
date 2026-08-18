using System.Threading.Channels;
using Logistics.Api.Common.Entities;

namespace Logistics.Api.Common.Telemetry;

public class TelemetryBuffer
{
    private readonly Channel<VehicleTelemetryLog> _channel;

    public TelemetryBuffer()
    {
        // Unbounded channel untuk throughput tinggi
        _channel = Channel.CreateUnbounded<VehicleTelemetryLog>(new UnboundedChannelOptions
        {
            SingleReader = true // Dibaca oleh 1 background worker
        });
    }

    public void Enqueue(VehicleTelemetryLog log) => _channel.Writer.TryWrite(log);

    public ChannelReader<VehicleTelemetryLog> Reader => _channel.Reader;
}