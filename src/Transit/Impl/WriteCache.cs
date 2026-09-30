using System.Diagnostics.CodeAnalysis;

namespace Transit.Net.Impl;

/// <summary>
/// Cache for transit write encoding. Uses a mutable dictionary
/// (never shared across threads) for O(1) lookup.
/// </summary>
internal sealed class WriteCache
{
    public const int MinSizeCacheable = 4;
    public const int CacheCodeDigits = 44;
    public const int MaxCacheEntries = CacheCodeDigits * CacheCodeDigits;
    public const int BaseCharIdx = 48;

    // Values with no prefix or tag are their own cache key, so they avoid tuple hashing.
    private Dictionary<string, string>? _plain;
    private Dictionary<string, string> Plain => _plain ??= new(StringComparer.Ordinal);
    private Dictionary<(string Prefix, string Tag, string Value), string>? _tagged;
    private Dictionary<(string Prefix, string Tag, string Value), string> Tagged
        => _tagged ??= new(TaggedKeyComparer.Instance);

    // prefix and tag come from a tiny set of one- and two-character literals, so hashing
    // them costs more than it discriminates; the value alone carries the entropy.
    private sealed class TaggedKeyComparer : IEqualityComparer<(string Prefix, string Tag, string Value)>
    {
        public static readonly TaggedKeyComparer Instance = new();

        public bool Equals((string Prefix, string Tag, string Value) x, (string Prefix, string Tag, string Value) y)
            => string.Equals(x.Value, y.Value, StringComparison.Ordinal)
            && string.Equals(x.Tag, y.Tag, StringComparison.Ordinal)
            && string.Equals(x.Prefix, y.Prefix, StringComparison.Ordinal);

        public int GetHashCode((string Prefix, string Tag, string Value) key)
            => HashCode.Combine(
                key.Value.GetHashCode(StringComparison.Ordinal),
                key.Prefix.Length,
                key.Tag.Length == 0 ? 0 : key.Tag[0]);
    }
    private int _index;
    private readonly bool _enabled;

    public WriteCache() : this(true) { }

    public WriteCache(bool enabled)
    {
        _enabled = enabled;
        _index = 0;
    }

    public static bool IsCacheable(string s, bool asDictionaryKey)
    {
        return s.Length >= MinSizeCacheable &&
            (asDictionaryKey ||
                (s[0] == Constants.Esc &&
                (s[1] == ':' || s[1] == '$' || s[1] == '#')));
    }

    private static string IndexToCode(int index)
    {
        int hi = index / CacheCodeDigits;
        int lo = index % CacheCodeDigits;
        if (hi == 0)
            return string.Create(2, lo, static (span, lo) =>
            {
                span[0] = Constants.Sub;
                span[1] = (char)(lo + BaseCharIdx);
            });

        return string.Create(3, (hi, lo), static (span, state) =>
        {
            span[0] = Constants.Sub;
            span[1] = (char)(state.hi + BaseCharIdx);
            span[2] = (char)(state.lo + BaseCharIdx);
        });
    }

    /// <summary>
    /// The character at <paramref name="i"/> of prefix+tag+value, without composing them.
    /// </summary>
    private static char CharAt(string? prefix, string? tag, string s, int i)
    {
        var plen = prefix?.Length ?? 0;
        if (i < plen)
            return prefix![i];

        i -= plen;
        var tlen = tag?.Length ?? 0;
        return i < tlen ? tag![i] : s[i - tlen];
    }

    private static bool IsCacheable(string? prefix, string? tag, string s, bool asDictionaryKey)
    {
        if ((prefix?.Length ?? 0) + (tag?.Length ?? 0) + s.Length < MinSizeCacheable)
            return false;
        if (asDictionaryKey)
            return true;

        return CharAt(prefix, tag, s, 0) == Constants.Esc
            && CharAt(prefix, tag, s, 1) is ':' or '$' or '#';
    }

    /// <summary>
    /// Yields the substitution code for prefix+tag+value when that combination has already been
    /// written. Otherwise assigns it one (when cacheable) and returns false, leaving the caller
    /// to write the text out in full. Keying on the three parts means a repeat costs no
    /// composition and no allocation.
    /// </summary>
    public bool TryGetCode(string? prefix, string? tag, string s, bool asDictionaryKey,
        [MaybeNullWhen(false)] out string code)
    {
        code = null;
        if (!_enabled || !IsCacheable(prefix, tag, s, asDictionaryKey))
            return false;

        if (prefix is null && tag is null)
        {
            if (Plain.TryGetValue(s, out code))
                return true;

            if (_index == MaxCacheEntries)
                Init();

            Plain[s] = IndexToCode(_index++);
            return false;
        }

        var key = (prefix ?? "", tag ?? "", s);
        if (Tagged.TryGetValue(key, out code))
            return true;

        if (_index == MaxCacheEntries)
            Init();

        Tagged[key] = IndexToCode(_index++);
        return false;
    }

    public string CacheWrite(string s, bool asDictionaryKey)
        => TryGetCode(null, null, s, asDictionaryKey, out var code) ? code : s;

    public WriteCache Init()
    {
        _index = 0;
        _plain?.Clear();
        _tagged?.Clear();
        return this;
    }
}
