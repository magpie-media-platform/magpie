namespace Magpie.Models.Enums;

/// <summary>
/// The data type of a configurable setting value.
/// </summary>
public enum DataType
{
    /// <summary>
    /// A string value.
    /// </summary>
    String = 0,

    /// <summary>
    /// A number value.
    /// </summary>
    Number,

    /// <summary>
    /// A boolean value.
    /// </summary>
    Boolean,

    /// <summary>
    /// A date value.
    /// </summary>
    Date,

    /// <summary>
    /// A list of mapping rows.
    /// </summary>
    MappingsList
}
