using MassTransit;
using Microsoft.AspNetCore.Authorization;
using ElsaBroker.Abstractions;
using ElsaBroker.Contracts;
using ElsaBroker.Data;
using ElsaBroker.Data.Entities;
using ElsaBroker.Data.Logging;
using ElsaBroker.Data.Registry;
using ElsaBroker.Queue.Auth;

namespace ElsaBroker.Queue.Endpoints;

public static class RequestEndpoints
{
    public static void MapRequestEndpoints(this WebApplication app)
    {
        // POST /requests — inbound from authorized client servers only
        // ClientId is taken from the mTLS certificate claim — NOT from the request body.
        app.MapPost("/requests", [Authorize(AuthenticationSchemes = "mtls")] async (
            SubmitRequestDto    dto,
            HttpContext         ctx,
            RequestTypeRegistry registry,
            BrokerDbContext     db,
            IPublishEndpoint    bus,
            ILogShipper         shipper,
            CancellationToken   ct) =>
        {
            // ClientId comes from the authenticated cert, not caller-supplied DTO
            var clientId = ctx.User.FindFirst(MtlsClaimTypes.ClientId)?.Value;
            if (string.IsNullOrWhiteSpace(clientId))
                return Results.Unauthorized();

            // Validate against registry
            var definition = registry.Get(clientId, dto.RequestType);
            if (definition is null)
                return Results.BadRequest(new { error = $"Unknown request type '{dto.RequestType}' for client '{clientId}'" });

            var missing = definition.RequiredKeys
                .Except(dto.Keys.Keys, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (missing.Count > 0)
                return Results.BadRequest(new { error = "Missing required keys", keys = missing });

            if (!definition.AllowAdditionalKeys)
            {
                var extra = dto.Keys.Keys
                    .Except(definition.RequiredKeys, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (extra.Count > 0)
                    return Results.BadRequest(new { error = "Unexpected keys", keys = extra });
            }

            // Write audit record + publish atomically via outbox
            var correlationId = NewId.NextGuid();
            var record = new RequestRecord
            {
                CorrelationId = correlationId,
                ClientId      = clientId,   // from cert, authoritative
                RequestType   = dto.RequestType,
                Status        = RequestStatus.Queued,
                SubmittedAt   = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow,
            };
            record.SetKeys(dto.Keys);
            if (dto.Payload is not null)
                record.PayloadJson = System.Text.Json.JsonSerializer.Serialize(dto.Payload);

            db.RequestRecords.Add(record);

            await bus.Publish<ISubmitRequest>(new
            {
                CorrelationId = correlationId,
                ClientId      = clientId,
                dto.RequestType,
                dto.Keys,
                Payload       = dto.Payload ?? new Dictionary<string, object>(),
                SubmittedAt   = DateTime.UtcNow,
            }, ct);

            await db.SaveChangesAsync(ct);

            await shipper.ShipAsync(new BrokerLogEvent
            {
                Timestamp     = DateTimeOffset.UtcNow,
                EventType     = "RequestSubmitted",
                Level         = "Information",
                Source        = "Queue",
                Message       = $"Request {correlationId} submitted by {clientId}",
                CorrelationId = correlationId.ToString(),
                ClientId      = clientId,
                RequestType   = dto.RequestType,
            }, ct);

            return Results.Accepted($"/requests/{correlationId}", new { correlationId });
        });

        // GET /requests/{id} — status poll, also mTLS protected
        // Clients can only see their own records.
        app.MapGet("/requests/{id:guid}", [Authorize(AuthenticationSchemes = "mtls")] async (
            Guid            id,
            HttpContext     ctx,
            BrokerDbContext db,
            CancellationToken ct) =>
        {
            var clientId = ctx.User.FindFirst(MtlsClaimTypes.ClientId)?.Value;
            var record   = await db.RequestRecords.FindAsync([id], ct);

            if (record is null) return Results.NotFound();

            // Enforce ownership — a client can only poll its own requests
            if (!string.Equals(record.ClientId, clientId, StringComparison.OrdinalIgnoreCase))
                return Results.Forbid();

            return Results.Ok(new
            {
                record.CorrelationId,
                record.ClientId,
                record.RequestType,
                Keys        = record.GetKeys(),
                record.Status,
                record.Result,
                record.ErrorDetail,
                record.SubmittedAt,
                record.UpdatedAt,
            });
        });
    }
}

// ClientId is NOT in the DTO anymore — it comes from the cert
public record SubmitRequestDto(
    string                      RequestType,
    Dictionary<string, string>  Keys,
    Dictionary<string, object>? Payload);
