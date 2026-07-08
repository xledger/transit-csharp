using System.Collections;
using System.Collections.Generic;

namespace Transit.Net.Impl;

/// <summary>
/// A dictionary that supports null keys, matching Java's HashMap behavior.
/// Used by cmap (composite-key map) read handling where null can be a valid key.
/// </summary>
internal sealed class NullKeyDictionary : IDictionary, IDictionary<object?, object?>, IReadOnlyDictionary<object?, object?>
{
    private readonly Dictionary<object, object?> _inner = new();
    private bool _hasNullKey;
    private object? _nullValue;

    public object? this[object? key]
    {
        get => key is null
            ? (_hasNullKey ? _nullValue : throw new KeyNotFoundException("null key not found"))
            : _inner[key];
        set
        {
            if (key is null) { _hasNullKey = true; _nullValue = value; }
            else _inner[key] = value;
        }
    }

    public int Count => _inner.Count + (_hasNullKey ? 1 : 0);

    public bool ContainsKey(object? key)
        => key is null ? _hasNullKey : _inner.ContainsKey(key);

    bool IDictionary.Contains(object? key) => ContainsKey(key);

    bool ICollection<KeyValuePair<object?, object?>>.Contains(KeyValuePair<object?, object?> kvp) => kvp.Key is null
        ? _hasNullKey && EqualityComparer<object>.Default.Equals(kvp.Value, _nullValue)
        : ((ICollection<KeyValuePair<object, object?>>)_inner).Contains(kvp);

    public ICollection<object?> Keys
    {
        get
        {
            if (!_hasNullKey) return _inner.Keys;
            var keys = new List<object?>(_inner.Count + 1);
            foreach (var k in _inner.Keys) keys.Add(k);
            keys.Add(null);
            return keys;
        }
    }

    IEnumerable<object?> IReadOnlyDictionary<object?, object?>.Keys => Keys;

    ICollection IDictionary.Keys => (ICollection)Keys;

    public ICollection<object?> Values
    {
        get
        {
            if (!_hasNullKey) return _inner.Values;
            var values = new List<object?>(_inner.Count + 1);
            foreach (var v in _inner.Values) values.Add(v);
            values.Add(_nullValue);
            return values;
        }
    }

    IEnumerable<object?> IReadOnlyDictionary<object?, object?>.Values => Values;

    ICollection IDictionary.Values => (ICollection)Values;

    public bool IsFixedSize => false;
    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public void Add(object key, object? value) => this[key] = value;

    void ICollection<KeyValuePair<object?, object?>>.Add(KeyValuePair<object?, object?> kvp) => Add(kvp.Key, kvp.Value);

    public void Clear() { _inner.Clear(); _hasNullKey = false; _nullValue = null; }

    public bool Remove(object? key)
    {
        if (key is null)
        {
            if (_hasNullKey)
            {
                _hasNullKey = false;
                _nullValue = null;
                return true;
            }
            return false;
        }
        return _inner.Remove(key);
    }

    void IDictionary.Remove(object key) => Remove(key);

    bool ICollection<KeyValuePair<object?, object?>>.Remove(KeyValuePair<object?, object?> item)
        => throw new NotImplementedException();

    public void CopyTo(Array array, int index) => throw new NotImplementedException();

    void ICollection<KeyValuePair<object?, object?>>.CopyTo(KeyValuePair<object?, object?>[] array, int arrayIndex)
        => throw new NotImplementedException();

    public IDictionaryEnumerator GetEnumerator() => new NullKeyEnumerator(this);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    IEnumerator<KeyValuePair<object?, object?>> IEnumerable<KeyValuePair<object?, object?>>.GetEnumerator()
        => new NullKeyEnumerator(this);

    public bool TryGetValue(object? key, out object? value)
    {
        if (key is null)
        {
            if (_hasNullKey)
            {
                value = _nullValue;
                return true;
            }
            value = default;
            return false;
        }
        return _inner.TryGetValue(key, out value);
    }

    private sealed class NullKeyEnumerator : IDictionaryEnumerator, IEnumerator<KeyValuePair<object?, object?>>
    {
        private readonly NullKeyDictionary _dict;
        private readonly IEnumerator<KeyValuePair<object, object?>> _innerEnum;
        private bool _onNull;
        private bool _doneNull;

        public NullKeyEnumerator(NullKeyDictionary dict)
        {
            _dict = dict;
            _innerEnum = dict._inner.GetEnumerator();
        }

        public DictionaryEntry Entry => _onNull
            ? new DictionaryEntry(null!, _dict._nullValue)
            : new DictionaryEntry(_innerEnum.Current.Key, _innerEnum.Current.Value);

        public object Key => Entry.Key;
        public object? Value => Entry.Value;
        public object Current => Entry;

        KeyValuePair<object?, object?> IEnumerator<KeyValuePair<object?, object?>>.Current => _onNull
            ? new KeyValuePair<object?, object?>(null, _dict._nullValue)
            : new KeyValuePair<object?, object?>(_innerEnum.Current.Key, _innerEnum.Current.Value);

        public void Dispose() { }

        public bool MoveNext()
        {
            if (_innerEnum.MoveNext()) { _onNull = false; return true; }
            if (_dict._hasNullKey && !_doneNull) { _doneNull = true; _onNull = true; return true; }
            return false;
        }

        public void Reset() { _innerEnum.Reset(); _doneNull = false; _onNull = false; }
    }
}
