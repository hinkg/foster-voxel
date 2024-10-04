namespace System.Collections.Generic;

public static class DictExt
{
    public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key) where TValue : new()
    {
        dict.TryGetValue(key, out TValue? val);
        if (val == null)
        {
            val = new TValue();
            dict.Add(key, val);
        }

        return val;
    }
}
