namespace Transit.Net.Impl;

/// <summary>
/// Cache for transit read decoding. Uses a fixed-size object array.
/// </summary>
internal sealed class ReadCache
{
    private readonly object[] _cache;
    private int _index;

    public ReadCache()
    {
        _cache = new object[WriteCache.MaxCacheEntries];
        _index = 0;
    }

    private static bool IsCacheCode(string s)
        => s[0] == Constants.Sub && !s.Equals(Constants.DirectoryAsList, StringComparison.Ordinal);

    private static int CodeToIndex(string s)
    {
        if (s.Length == 2)
            return s[1] - WriteCache.BaseCharIdx;

        return ((s[1] - WriteCache.BaseCharIdx) * WriteCache.CacheCodeDigits) +
               (s[2] - WriteCache.BaseCharIdx);
    }

    /// <summary>
    /// Resolves a substitution code straight from its UTF-8 bytes, so a repeated key or value
    /// does not allocate a string only for it to be discarded. Returns false when the bytes are
    /// not a two- or three-byte code, leaving the caller to take the string path.
    /// </summary>
    public bool TryReadCode(ReadOnlySpan<byte> utf8, out object value)
    {
        if (utf8.Length is 2 or 3 && utf8[0] == (byte)Constants.Sub
            && !(utf8.Length == 2 && utf8[1] == (byte)' '))
        {
            var index = utf8.Length == 2
                ? utf8[1] - WriteCache.BaseCharIdx
                : ((utf8[1] - WriteCache.BaseCharIdx) * WriteCache.CacheCodeDigits)
                  + (utf8[2] - WriteCache.BaseCharIdx);

            // Derived from raw bytes, so a malformed document can put it out of range. Falling
            // through leaves the string path to handle it exactly as it did before.
            if ((uint)index < (uint)_cache.Length)
            {
                value = _cache[index]!;
                return true;
            }
        }

        value = null!;
        return false;
    }

    public object CacheRead(string s, bool asDictionaryKey)
        => CacheRead(s, asDictionaryKey, null);

    public object CacheRead(string s, bool asDictionaryKey, AbstractParser? p)
    {
        if (s.Length != 0)
        {
            if (IsCacheCode(s))
                return _cache[CodeToIndex(s)];

            if (WriteCache.IsCacheable(s, asDictionaryKey))
            {
                if (_index == WriteCache.MaxCacheEntries)
                    Init();

                return _cache[_index++] = (p != null ? p.ParseString(s) : s);
            }
        }

        return p != null ? p.ParseString(s) : s;
    }

    public ReadCache Init()
    {
        if (_index > 0)
            Array.Clear(_cache, 0, _index);
        _index = 0;
        return this;
    }
}
