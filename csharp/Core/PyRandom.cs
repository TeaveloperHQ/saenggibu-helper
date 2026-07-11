namespace Saenggibu;

/// <summary>
/// CPython random.Random(int seed) 를 bit-exact 재현(MT19937 + _randbelow/shuffle/choice/random).
/// Python 코드가 random.Random(42) 등 고정 시드로 결정론적 변형을 만들므로 파리티에 필요.
/// _randommodule.c / random.py 알고리즘 그대로.
/// </summary>
public sealed class PyRandom
{
    private const int N = 624, M = 397;
    private const uint MatrixA = 0x9908b0df, Upper = 0x80000000, Lower = 0x7fffffff;
    private readonly uint[] _mt = new uint[N];
    private int _mti = N + 1;

    public PyRandom(long seed) => Seed(seed);

    private void InitGenrand(uint s)
    {
        _mt[0] = s;
        for (int i = 1; i < N; i++)
            _mt[i] = (uint)(1812433253u * (_mt[i - 1] ^ (_mt[i - 1] >> 30)) + (uint)i);
        _mti = N;
    }

    private void InitByArray(uint[] key)
    {
        InitGenrand(19650218u);
        int i = 1, j = 0;
        int k = Math.Max(N, key.Length);
        for (; k > 0; k--)
        {
            _mt[i] = (uint)((_mt[i] ^ ((_mt[i - 1] ^ (_mt[i - 1] >> 30)) * 1664525u)) + key[j] + (uint)j);
            i++; j++;
            if (i >= N) { _mt[0] = _mt[N - 1]; i = 1; }
            if (j >= key.Length) j = 0;
        }
        for (k = N - 1; k > 0; k--)
        {
            _mt[i] = (uint)((_mt[i] ^ ((_mt[i - 1] ^ (_mt[i - 1] >> 30)) * 1566083941u)) - (uint)i);
            i++;
            if (i >= N) { _mt[0] = _mt[N - 1]; i = 1; }
        }
        _mt[0] = 0x80000000u;
    }

    /// <summary>random.seed(int): abs(seed)를 32비트 워드 배열(리틀엔디언)로 → init_by_array.</summary>
    public void Seed(long seed)
    {
        ulong n = (ulong)Math.Abs(seed);
        var key = new List<uint>();
        if (n == 0) key.Add(0);
        while (n > 0) { key.Add((uint)(n & 0xffffffff)); n >>= 32; }
        InitByArray(key.ToArray());
    }

    public uint GenrandUint32()
    {
        uint y;
        if (_mti >= N)
        {
            int kk;
            for (kk = 0; kk < N - M; kk++)
            {
                y = (_mt[kk] & Upper) | (_mt[kk + 1] & Lower);
                _mt[kk] = _mt[kk + M] ^ (y >> 1) ^ ((y & 1) != 0 ? MatrixA : 0u);
            }
            for (; kk < N - 1; kk++)
            {
                y = (_mt[kk] & Upper) | (_mt[kk + 1] & Lower);
                _mt[kk] = _mt[kk + (M - N)] ^ (y >> 1) ^ ((y & 1) != 0 ? MatrixA : 0u);
            }
            y = (_mt[N - 1] & Upper) | (_mt[0] & Lower);
            _mt[N - 1] = _mt[M - 1] ^ (y >> 1) ^ ((y & 1) != 0 ? MatrixA : 0u);
            _mti = 0;
        }
        y = _mt[_mti++];
        y ^= y >> 11;
        y ^= (y << 7) & 0x9d2c5680u;
        y ^= (y << 15) & 0xefc60000u;
        y ^= y >> 18;
        return y;
    }

    /// <summary>random.random() — genrand_res53(53비트 double).</summary>
    public double Random()
    {
        uint a = GenrandUint32() >> 5;
        uint b = GenrandUint32() >> 6;
        return (a * 67108864.0 + b) * (1.0 / 9007199254740992.0);
    }

    /// <summary>random.getrandbits(k).</summary>
    public ulong GetRandBits(int k)
    {
        if (k <= 32) return GenrandUint32() >> (32 - k);
        ulong result = 0;
        int shift = 0;
        while (k > 0)
        {
            uint r = GenrandUint32();
            if (k < 32) r >>= (32 - k);
            result |= (ulong)r << shift;
            k -= 32; shift += 32;
        }
        return result;
    }

    private static int BitLength(int n)
    {
        int b = 0;
        while (n > 0) { b++; n >>= 1; }
        return b;
    }

    /// <summary>random._randbelow(n) — getrandbits 기반 rejection sampling.</summary>
    public int RandBelow(int n)
    {
        if (n <= 0) return 0;
        int k = BitLength(n);
        ulong r = GetRandBits(k);
        while (r >= (ulong)n) r = GetRandBits(k);
        return (int)r;
    }

    /// <summary>random.shuffle(x) — 제자리 Fisher-Yates(역순).</summary>
    public void Shuffle<T>(IList<T> x)
    {
        for (int i = x.Count - 1; i >= 1; i--)
        {
            int j = RandBelow(i + 1);
            (x[i], x[j]) = (x[j], x[i]);
        }
    }

    /// <summary>random.choice(seq).</summary>
    public T Choice<T>(IReadOnlyList<T> seq) => seq[RandBelow(seq.Count)];
}
