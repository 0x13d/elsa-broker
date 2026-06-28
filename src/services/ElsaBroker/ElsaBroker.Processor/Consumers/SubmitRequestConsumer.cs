using MassTransit;
using ElsaBroker.Abstractions;
using ElsaBroker.Contracts;
using ElsaBroker.Data;
using ElsaBroker.Data.Logging;
using ElsaBroker.Processor.Handlers;

namespace ElsaBroker.Processor.Consumers;

/// <summary>
/// Single consumer for all ISubmitRequest messages.
/// Resolves the correct IRequestHandler by RequestType, then updates the audit record.
/// </summary>
public class SubmitRequestConsumer(
    IEnumerable<IRequestHandler>     handlers,
    BrokerDbContext                  db,
    ILogShipper                      shipper,
    ILogger<SubmitRequestConsumer>   logger) : IConsumer<ISubmitRequest>
{
    public async Task Consume(ConsumeContext<ISubmitRequest> context)
    {
        var msg = context.Message;
        logger.LogInformation("Consuming {RequestType} [{CorrelationId}]", msg.RequestType, msg.CorrelationId);

        var record = await db.RequestRecords.FindAsync(msg.CorrelationId);
        if (record is null)
        {
            logger.LogWarning("No RequestRecord found for {CorrelationId} — skipping.", msg.CorrelationId);
            return;
        }

        record.Status    = RequestStatus.Processing;
        record.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(context.CancellationToken);

        await shipper.ShipAsync(new BrokerLogEvent
        {
            Timestamp     = DateTimeOffset.UtcNow,
            EventType     = "RequestProcessing",
            Level         = "Information",
            Source        = "Processor",
            Message       = $"Processing {msg.RequestType} [{msg.CorrelationId}]",
            CorrelationId = msg.CorrelationId.ToString(),
            ClientId      = msg.ClientId,
            RequestType   = msg.RequestType,
        }, context.CancellationToken);

        var handler = handlers.FirstOrDefault(h =>
            string.Equals(h.RequestType, msg.RequestType, StringComparison.OrdinalIgnoreCase));

        if (handler is null)
        {
            record.Status      = RequestStatus.Faulted;
            record.ErrorDetail = $"No handler registered for request type '{msg.RequestType}'";
            record.UpdatedAt   = DateTime.UtcNow;
            await db.SaveChangesAsync(context.CancellationToken);
            logger.LogError("No handler for RequestType '{RequestType}'", msg.RequestType);

            await shipper.ShipAsync(new BrokerLogEvent
            {
                Timestamp     = DateTimeOffset.UtcNow,
                EventType     = "RequestFaulted",
                Level         = "Error",
                Source        = "Processor",
                Message       = $"No handler registered for request type '{msg.RequestType}'",
                CorrelationId = msg.CorrelationId.ToString(),
                ClientId      = msg.ClientId,
                RequestType   = msg.RequestType,
            }, context.CancellationToken);

            return;
        }

        try
        {
            var result = await handler.HandleAsync(msg, context.CancellationToken);

            // Async handlers (e.g. Elsa dispatch) accept the work and finalize later via callback.
            // Leave the record in Processing; the callback endpoint sets the terminal status.
            if (result.Deferred)
            {
                logger.LogInformation("Request {CorrelationId} dispatched; awaiting async callback.", msg.CorrelationId);

                await shipper.ShipAsync(new BrokerLogEvent
                {
                    Timestamp     = DateTimeOffset.UtcNow,
                    EventType     = "RequestDispatched",
                    Level         = "Information",
                    Source        = "Processor",
                    Message       = $"Dispatched {msg.RequestType} [{msg.CorrelationId}]; awaiting callback",
                    CorrelationId = msg.CorrelationId.ToString(),
                    ClientId      = msg.ClientId,
                    RequestType   = msg.RequestType,
                }, context.CancellationToken);

                return;
            }

            record.Status      = result.Success ? RequestStatus.Completed : RequestStatus.Faulted;
            record.Result      = result.Output;
            record.ErrorDetail = result.ErrorDetail;
            record.UpdatedAt   = DateTime.UtcNow;

            await shipper.ShipAsync(new BrokerLogEvent
            {
                Timestamp     = DateTimeOffset.UtcNow,
                EventType     = result.Success ? "RequestCompleted" : "RequestFaulted",
                Level         = result.Success ? "Information" : "Error",
                Source        = "Processor",
                Message       = $"{msg.RequestType} [{msg.CorrelationId}] -> {record.Status}",
                CorrelationId = msg.CorrelationId.ToString(),
                ClientId      = msg.ClientId,
                RequestType   = msg.RequestType,
                Properties    = result.ErrorDetail is not null
                    ? new Dictionary<string, object?> { ["errorDetail"] = result.ErrorDetail }
                    : null,
            }, context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Handler threw for {RequestType} [{CorrelationId}]", msg.RequestType, msg.CorrelationId);
            record.Status      = RequestStatus.Faulted;
            record.ErrorDetail = ex.Message;
            record.UpdatedAt   = DateTime.UtcNow;

            await shipper.ShipAsync(new BrokerLogEvent
            {
                Timestamp     = DateTimeOffset.UtcNow,
                EventType     = "RequestFaulted",
                Level         = "Error",
                Source        = "Processor",
                Message       = $"Handler threw for {msg.RequestType} [{msg.CorrelationId}]",
                CorrelationId = msg.CorrelationId.ToString(),
                ClientId      = msg.ClientId,
                RequestType   = msg.RequestType,
                Exception     = ex.ToString(),
            }, context.CancellationToken);
        }

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
