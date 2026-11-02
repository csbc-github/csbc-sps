namespace ThreeDScan.Infrastructure.Export;

/// <summary>
/// 對齊 CPython 3.11 x64 整數 XY tuple 的 set 迭代。原版依發現順序為同尺寸物件排名，
/// 重現該表可避免物件名稱任意變動。
/// </summary>
/// <remarks>由 Hank Scan3D 的 CompatibleGridSet 搬入，邏輯不改。</remarks>
internal sealed class CompatibleGridSet
{
    private struct Entry
    {
        public GridCell Key;
        public ulong Hash;
        public byte State;
    }

    private Entry[] _table = new Entry[8];
    private int _fill;

    /// <summary>元素數。</summary>
    public int Count { get; private set; }

    /// <summary>目前佔用的格子（表迭代順序）。</summary>
    public IEnumerable<GridCell> Cells
    {
        get
        {
            return _table.Where(e => e.State == 1).Select(e => e.Key);
        }
    }

    private static ulong Hash(GridCell key)
    {
        const ulong p1 = 11400714785074694791, p2 = 14029467366897019727, p5 = 2870177450012600261;
        static ulong IntegerHash(long n)
        {
            ulong magnitude = n < 0 ? (ulong)(-(n + 1)) + 1 : (ulong)n;
            long hash = (long)(magnitude % 2305843009213693951UL);
            if (n < 0)
            {
                hash = -hash;
            }

            return unchecked((ulong)(hash == -1 ? -2 : hash));
        }

        unchecked
        {
            ulong acc = p5;
            foreach (long value in new[] { key.X, key.Y })
            {
                acc += IntegerHash(value) * p2;
                acc = (acc << 31) | (acc >> 33);
                acc *= p1;
            }

            acc += 2UL ^ (p5 ^ 3527539UL);
            return acc == ulong.MaxValue ? 1546275796 : acc;
        }
    }

    private int Find(GridCell key, ulong hash, out int free)
    {
        int mask = _table.Length - 1, i = (int)(hash & (ulong)mask);
        ulong perturb = hash;
        free = -1;
        while (true)
        {
            int probes = i + 9 <= mask ? 9 : 0;
            for (int j = i; j <= i + probes; j++)
            {
                ref var entry = ref _table[j];
                if (entry.State == 0)
                {
                    return j;
                }

                if (entry.State == 1 && entry.Hash == hash && entry.Key == key)
                {
                    return j;
                }

                if (entry.State == 2)
                {
                    free = j;
                }
            }

            perturb >>= 5;
            i = (int)(((ulong)i * 5 + 1 + perturb) & (ulong)mask);
        }
    }

    /// <summary>是否含此格。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Extraction.CompatibleGridSet</c>。</remarks>
    /// <param name="key">格子。</param>
    public bool Contains(GridCell key)
    {
        int index = Find(key, Hash(key), out _);
        return _table[index].State == 1;
    }

    /// <summary>加入格子。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Extraction.CompatibleGridSet</c>。</remarks>
    /// <param name="key">格子。</param>
    public void Add(GridCell key)
    {
        ulong hash = Hash(key);
        int index = Find(key, hash, out int free);
        if (_table[index].State == 1)
        {
            return;
        }

        if (free >= 0)
        {
            index = free;
        }
        else
        {
            _fill++;
        }

        _table[index] = new Entry { Key = key, Hash = hash, State = 1 };
        Count++;
        if (free < 0 && (long)_fill * 5 >= (long)(_table.Length - 1) * 3)
        {
            Resize(Count > 50000 ? Count * 2 : Count * 4);
        }
    }

    private void InsertClean(Entry entry)
    {
        int mask = _table.Length - 1, i = (int)(entry.Hash & (ulong)mask);
        ulong perturb = entry.Hash;
        while (true)
        {
            if (_table[i].State == 0)
            {
                _table[i] = entry;
                return;
            }

            if (i + 9 <= mask)
            {
                for (int j = 1; j <= 9; j++)
                {
                    if (_table[i + j].State == 0)
                    {
                        _table[i + j] = entry;
                        return;
                    }
                }
            }

            perturb >>= 5;
            i = (int)(((ulong)i * 5 + 1 + perturb) & (ulong)mask);
        }
    }

    private void Resize(int minimumUsed)
    {
        int size = 8;
        while (size <= minimumUsed)
        {
            size = checked(size * 2);
        }

        if (size == 8 && _table.Length == 8 && _fill == Count)
        {
            return;
        }

        var old = _table;
        _table = new Entry[size];
        foreach (var entry in old)
        {
            if (entry.State == 1)
            {
                InsertClean(entry);
            }
        }

        _fill = Count;
    }

    /// <summary>複製集合（保留 CPython 迭代順序）。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Extraction.CompatibleGridSet</c>。</remarks>
    public CompatibleGridSet Copy()
    {
        var copy = new CompatibleGridSet();
        if (Count * 5 >= 7 * 3)
        {
            copy.Resize(Count * 2);
        }

        if (copy._table.Length == _table.Length && _fill == Count)
        {
            copy._table = (Entry[])_table.Clone();
        }
        else
        {
            foreach (var entry in _table)
            {
                if (entry.State == 1)
                {
                    copy.InsertClean(entry);
                }
            }
        }

        copy.Count = Count;
        copy._fill = Count;
        return copy;
    }

    /// <summary>移除多個格子。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Extraction.CompatibleGridSet</c>。</remarks>
    /// <param name="keys">要移除的格子。</param>
    public void RemoveAll(IEnumerable<GridCell> keys)
    {
        foreach (var key in keys)
        {
            int index = Find(key, Hash(key), out _);
            if (_table[index].State == 1)
            {
                _table[index].State = 2;
                Count--;
            }
        }

        if (_fill - Count > (_table.Length - 1) / 4)
        {
            Resize(Count > 50000 ? Count * 2 : Count * 4);
        }
    }
}
