using System.Text.Json;

namespace Kontent.Ai.Delivery.Configuration;

/// <summary>
/// Holds the <see cref="JsonSerializerOptions"/> the SDK reads the wire with, under a service type only
/// this assembly names.
/// </summary>
/// <remarks>
/// <see cref="JsonSerializerOptions"/> itself is a service type that belongs to the application, and
/// registering an instance of it is ordinary. Sharing that type with the SDK made whichever registration
/// came first win: an application's own options replaced the SDK's, and without
/// <c>ContentItemConverterFactory</c> no raw item JSON is captured, so hydration finds nothing and typed
/// models deserialize empty - with no error anywhere. A private type keeps the two apart, and the
/// application's registration means what it says again.
/// </remarks>
internal sealed class DeliveryJsonOptions(JsonSerializerOptions value)
{
    public JsonSerializerOptions Value { get; } = value;
}
