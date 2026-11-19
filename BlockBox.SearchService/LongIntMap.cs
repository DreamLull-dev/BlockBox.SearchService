namespace BlockBox.SearchService;

/// <summary>
/// 专用 long→int 哈希表（开放寻址、线性探测、只增不删）。
/// <para>
/// 索引里的“文件引用号 → 记录下标”映射每百万条要存一次，<see cref="Dictionary{TKey,TValue}"/>
/// 每条记录（哈希、链表指针、键、值）约 30–40 字节；这里每条只占 13 字节 × 装填系数，
/// 235 万条实测能省下几十 MB，查找路径也少一层间接跳转。
/// </para>
/// <para>删除靠重建（索引里的删除本来就是墓碑 + 整体压缩），所以不实现 Remove。</para>
/// </summary>
internal sealed class LongIntMap
{
    private long[] _keys;
    private int[] _values;
    private bool[] _used;
    private int _mask;
    private int _count;

    internal LongIntMap(int capacity)
    {
        int size = 8;
        // 目标装填系数 ≤ 0.7
        while (size < (long)capacity * 3 / 2) size <<= 1;
        _keys = new long[size];
        _values = new int[size];
        _used = new bool[size];
        _mask = size - 1;
    }

    internal int Count => _count;

    internal bool TryGetValue(long key, out int value)
    {
        int index = Hash(key) & _mask;
        while (_used[index])
        {
            if (_keys[index] == key)
            {
                value = _values[index];
                return true;
            }
            index = (index + 1) & _mask;
        }
        value = 0;
        return false;
    }

    /// <summary>插入或覆盖。</summary>
    internal void Set(long key, int value)
    {
        if ((long)(_count + 1) * 10 > (long)_used.Length * 7) Grow();
        int index = Hash(key) & _mask;
        while (_used[index])
        {
            if (_keys[index] == key)
            {
                _values[index] = value;
                return;
            }
            index = (index + 1) & _mask;
        }
        _used[index] = true;
        _keys[index] = key;
        _values[index] = value;
        _count++;
    }

    private static int Hash(long key)
    {
        // Knuth 乘法散列的高 32 位；& mask 后自然落在非负区间。
        ulong mixed = (ulong)key * 0x9E3779B97F4A7C15UL;
        return (int)(mixed >> 32);
    }

    private void Grow()
    {
        long[] oldKeys = _keys;
        int[] oldValues = _values;
        bool[] oldUsed = _used;

        int size = oldUsed.Length << 1;
        _keys = new long[size];
        _values = new int[size];
        _used = new bool[size];
        _mask = size - 1;
        _count = 0;

        for (int i = 0; i < oldUsed.Length; i++)
            if (oldUsed[i]) Set(oldKeys[i], oldValues[i]);
    }
}
