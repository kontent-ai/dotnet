namespace Kontent.Ai.Management.Models.VariantFilter;

/// <summary>
/// Column to order the items-with-variants filter results by.
/// </summary>
public enum VariantFilterOrderColumn
{
    /// <summary>
    /// Order by item name.
    /// </summary>
    [JsonStringEnumMemberName("name")]
    Name,

    /// <summary>
    /// Order by the variant's due date.
    /// </summary>
    [JsonStringEnumMemberName("due_date")]
    DueDate,

    /// <summary>
    /// Order by the variant's last-modified timestamp.
    /// </summary>
    [JsonStringEnumMemberName("last_modified")]
    LastModified
}
