namespace Transit.Net.Impl;

/// <summary>
/// A wrapper class for lists, to semantically preserve how the list should be serialized.
/// Normally things tagged with "list" would be interpreted as LinkedLists, but in C# we want to
/// use regular Lists for performance.
/// </summary>
internal sealed class ListWrapper : List<object?>;
