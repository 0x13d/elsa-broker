namespace ElsaBroker.Switchboard.Data;

/// <summary>
/// Seeds the Switchboard SQLite database with realistic sample data for development.
/// Only populates data if the database is empty.
/// </summary>
public static class SwitchboardSeeder
{
    /// <summary>
    /// Seeds the database with sample log events if no events exist.
    /// Creates correlated request lifecycles across multiple clients and request types.
    /// </summary>
    public static async Task SeedAsync(SwitchboardDbContext db)
    {
        if (db.LogEvents.Any())
            return;

        var now = DateTimeOffset.UtcNow;
        var records = new List<LogEventRecord>();

        // Correlated request lifecycles
        var flows = new[]
        {
            // Completed flows
            new { CorrelationId = "req-a1b2c3d4", ClientId = "ClientA", RequestType = "InvoiceProcess", MinutesAgo = 1380, Fault = false },
            new { CorrelationId = "req-e5f6g7h8", ClientId = "ClientA", RequestType = "PolicyRenewal", MinutesAgo = 1200, Fault = false },
            new { CorrelationId = "req-i9j0k1l2", ClientId = "ClientB", RequestType = "ClaimSubmission", MinutesAgo = 1020, Fault = false },
            new { CorrelationId = "req-m3n4o5p6", ClientId = "ClientA", RequestType = "InvoiceProcess", MinutesAgo = 840, Fault = false },
            new { CorrelationId = "req-q7r8s9t0", ClientId = "ClientB", RequestType = "PolicyRenewal", MinutesAgo = 720, Fault = false },
            new { CorrelationId = "req-u1v2w3x4", ClientId = "ClientA", RequestType = "ClaimSubmission", MinutesAgo = 600, Fault = false },
            new { CorrelationId = "req-y5z6a7b8", ClientId = "ClientB", RequestType = "InvoiceProcess", MinutesAgo = 480, Fault = false },
            new { CorrelationId = "req-c9d0e1f2", ClientId = "ClientA", RequestType = "PolicyRenewal", MinutesAgo = 360, Fault = false },
            new { CorrelationId = "req-g3h4i5j6", ClientId = "ClientB", RequestType = "ClaimSubmission", MinutesAgo = 240, Fault = false },
            new { CorrelationId = "req-k7l8m9n0", ClientId = "ClientA", RequestType = "InvoiceProcess", MinutesAgo = 120, Fault = false },
            new { CorrelationId = "req-o1p2q3r4", ClientId = "ClientB", RequestType = "PolicyRenewal", MinutesAgo = 60, Fault = false },
            new { CorrelationId = "req-s5t6u7v8", ClientId = "ClientA", RequestType = "InvoiceProcess", MinutesAgo = 30, Fault = false },

            // Faulted flows
            new { CorrelationId = "req-w9x0y1z2", ClientId = "ClientB", RequestType = "InvoiceProcess", MinutesAgo = 900, Fault = true },
            new { CorrelationId = "req-a3b4c5d6", ClientId = "ClientA", RequestType = "ClaimSubmission", MinutesAgo = 540, Fault = true },
            new { CorrelationId = "req-e7f8g9h0", ClientId = "ClientB", RequestType = "PolicyRenewal", MinutesAgo = 180, Fault = true },
        };

        foreach (var flow in flows)
        {
            var baseTime = now.AddMinutes(-flow.MinutesAgo);

            // Submitted
            records.Add(new LogEventRecord
            {
                Timestamp = baseTime,
                EventType = "RequestSubmitted",
                Level = "Information",
                Source = "Queue",
                CorrelationId = flow.CorrelationId,
                ClientId = flow.ClientId,
                RequestType = flow.RequestType,
                Message = $"{flow.RequestType} request submitted by {flow.ClientId}"
            });

            // Processing
            records.Add(new LogEventRecord
            {
                Timestamp = baseTime.AddSeconds(2),
                EventType = "RequestProcessing",
                Level = "Information",
                Source = "Processor",
                CorrelationId = flow.CorrelationId,
                ClientId = flow.ClientId,
                RequestType = flow.RequestType,
                Message = $"Consumer picked up {flow.RequestType} request"
            });

            // Dispatched to Elsa
            records.Add(new LogEventRecord
            {
                Timestamp = baseTime.AddSeconds(3),
                EventType = "RequestDispatched",
                Level = "Information",
                Source = "Processor",
                CorrelationId = flow.CorrelationId,
                ClientId = flow.ClientId,
                RequestType = flow.RequestType,
                Message = $"Dispatched to Elsa workflow for {flow.RequestType}"
            });

            if (flow.Fault)
            {
                // Faulted
                records.Add(new LogEventRecord
                {
                    Timestamp = baseTime.AddSeconds(8),
                    EventType = "RequestFaulted",
                    Level = "Error",
                    Source = "Processor",
                    CorrelationId = flow.CorrelationId,
                    ClientId = flow.ClientId,
                    RequestType = flow.RequestType,
                    Message = $"{flow.RequestType} request faulted during processing",
                    Exception = flow.RequestType switch
                    {
                        "InvoiceProcess" => "System.Net.Http.HttpRequestException: Connection refused (localhost:13002)\n   at System.Net.Http.HttpConnectionPool.SendAsync()\n   at ElsaBroker.Processor.Handlers.ElsaDispatchHandler.HandleAsync()",
                        "ClaimSubmission" => "System.TimeoutException: The operation has timed out after 30000ms.\n   at ElsaBroker.Processor.Handlers.ElsaDispatchHandler.HandleAsync()\n   at MassTransit.Middleware.ConsumerMessageFilter`2.Send()",
                        _ => "System.InvalidOperationException: Workflow definition 'policy-renewal-workflow' not found in registry.\n   at ElsaBroker.Processor.WorkflowRegistry.GetDefinitionId(String requestType)\n   at ElsaBroker.Processor.Handlers.ElsaDispatchHandler.HandleAsync()"
                    }
                });
            }
            else
            {
                // Callback received
                records.Add(new LogEventRecord
                {
                    Timestamp = baseTime.AddSeconds(12),
                    EventType = "CallbackReceived",
                    Level = "Information",
                    Source = "Queue",
                    CorrelationId = flow.CorrelationId,
                    ClientId = flow.ClientId,
                    RequestType = flow.RequestType,
                    Message = $"Callback received from Elsa workflow for {flow.RequestType}"
                });

                // Completed
                records.Add(new LogEventRecord
                {
                    Timestamp = baseTime.AddSeconds(13),
                    EventType = "RequestCompleted",
                    Level = "Information",
                    Source = "Queue",
                    CorrelationId = flow.CorrelationId,
                    ClientId = flow.ClientId,
                    RequestType = flow.RequestType,
                    Message = $"{flow.RequestType} request completed successfully"
                });
            }
        }

        // A few standalone warning events
        records.Add(new LogEventRecord
        {
            Timestamp = now.AddMinutes(-660),
            EventType = "RequestProcessing",
            Level = "Warning",
            Source = "Processor",
            CorrelationId = "req-slow-1",
            ClientId = "ClientA",
            RequestType = "InvoiceProcess",
            Message = "Request processing took longer than expected (15.2s)"
        });

        records.Add(new LogEventRecord
        {
            Timestamp = now.AddMinutes(-300),
            EventType = "RequestProcessing",
            Level = "Warning",
            Source = "Processor",
            CorrelationId = "req-slow-2",
            ClientId = "ClientB",
            RequestType = "PolicyRenewal",
            Message = "Elsa workflow dispatch retry #2 after transient failure"
        });

        records.Add(new LogEventRecord
        {
            Timestamp = now.AddMinutes(-45),
            EventType = "RequestProcessing",
            Level = "Warning",
            Source = "Queue",
            ClientId = "ClientA",
            Message = "Outbox publish delayed; SQL transport backpressure detected"
        });

        db.LogEvents.AddRange(records);
        await db.SaveChangesAsync();
    }
}
