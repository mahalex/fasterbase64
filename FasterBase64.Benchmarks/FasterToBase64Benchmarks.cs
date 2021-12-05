using BenchmarkDotNet.Attributes;
using System;

public class FasterToBase64Benchmarks
{
    private byte[] bytes;
    private char[] chars;

    [Params(100, 1000, 10000)]
    public int N { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        bytes = new byte[N];
        var resultSize = (1 + (N - 1) / 3) * 4;
        chars = new char[resultSize];
        var random = new Random(1);
        random.NextBytes(bytes);
    }

    [Benchmark]
    public char Old()
    {
        System.Convert.TryToBase64Chars(bytes, chars, out var _);
        return chars[^1];
    }

    [Benchmark]
    public char New()
    {
        FasterBase64.Convert.TryToBase64Chars(bytes, chars, out var _);
        return chars[^1];
    }
}
