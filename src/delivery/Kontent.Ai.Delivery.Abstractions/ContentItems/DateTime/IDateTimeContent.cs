namespace Kontent.Ai.Delivery.Abstractions;

/// <summary>
/// Represents DateTimeElement content in a form of structured data 
/// </summary>
public interface IDateTimeContent
{
    /// <summary>
    /// The instant the element holds, as the UTC value the API stores. Null when the element is empty.
    /// </summary>
    DateTime? Value { get; }

    /// <summary>
    /// IANA zone name the UI displays <see cref="Value"/> in (e.g. <c>Europe/Prague</c>); null when unset.
    /// It never shifts the instant — pass it to <see cref="TimeZoneInfo.FindSystemTimeZoneById(string)"/> to
    /// render local wall time.
    /// </summary>
    string? DisplayTimezone { get; }
}
