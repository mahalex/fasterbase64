using BenchmarkDotNet.Attributes;
using System;

public class FasterFromBase64Benchmarks
{
    private byte[] bytes;
    private char[] chars;

    [Params(100, 1000, 10000)]
    public int N { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var originalBytes = new byte[N];
        var random = new Random(1);
        random.NextBytes(originalBytes);
        var resultSize = (1 + (N - 1) / 3) * 4;
        chars = new char[resultSize];
        System.Convert.TryToBase64Chars(originalBytes, chars, out var _);
        bytes = new byte[N];
    }

    [Benchmark]
    public byte Old()
    {
        System.Convert.TryFromBase64Chars(chars, bytes, out var _);
        return bytes[^1];
    }

    [Benchmark]
    public byte New()
    {
        FasterBase64.Convert.TryFromBase64Chars(chars, bytes, out var _);
        return bytes[^1];
    }
}
