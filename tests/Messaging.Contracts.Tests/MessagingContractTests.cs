using System.Text.Json;
using Contracts;
using Messaging;
using Xunit;

namespace Messaging.Contracts.Tests;

public sealed class MessagingContractTests
{
    [Fact]
    public void V1_payload_preserves_outbox_message_identity_and_logical_fields()
    {
        var id = Guid.Parse("0efc2134-a079-405b-a0b6-a6b3a677b33f");
        var valueId = Guid.Parse("c31a1f73-4c40-49b0-a556-7eaa3d589db7");
        var at = new DateTimeOffset(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
        var original = new ValueReceivedV1(id, valueId, 10.50m, at);

        string json = JsonSerializer.Serialize(original);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(id.ToString(), document.RootElement.GetProperty("EventId").GetString());
        Assert.Equal(valueId.ToString(), document.RootElement.GetProperty("ValueId").GetString());
        Assert.Equal(10.50m, document.RootElement.GetProperty("Value").GetDecimal());
        Assert.Equal(at, document.RootElement.GetProperty("OccurredAt").GetDateTimeOffset());
        Assert.False(document.RootElement.TryGetProperty("MessageId", out _));
        Assert.False(document.RootElement.TryGetProperty("CorrelationId", out _));

        var deserialized = JsonSerializer.Deserialize<ValueReceivedV1>(json);
        Assert.NotNull(deserialized);
        Assert.Equal(original, deserialized);
        Assert.Equal(id, deserialized.MessageId);
    }

    [Fact]
    public void V1_additive_optional_correlation_identifier_round_trips()
    {
        var original = new ValueReceivedV1(Guid.NewGuid(), Guid.NewGuid(), 7m,
            new DateTimeOffset(2026, 9, 19, 9, 0, 0, TimeSpan.Zero), "request-42");

        var roundTrip = JsonSerializer.Deserialize<ValueReceivedV1>(JsonSerializer.Serialize(original));
        Assert.Equal(original, roundTrip);
        Assert.Equal("request-42", roundTrip?.CorrelationId);
    }

    [Fact]
    public void Transport_identifiers_are_explicit_and_versioned()
    {
        Assert.Equal("lab.events.v1", RabbitMqTopology.Exchange);
        Assert.Equal("consolidation.value-received.v1", RabbitMqTopology.ValueReceivedQueue);
        Assert.Equal("value.received.v1", RabbitMqTopology.ValueReceivedRoutingKey);
        Assert.Equal("ValueReceived.v1", RabbitMqTopology.ValueReceivedEventType);
    }
}
