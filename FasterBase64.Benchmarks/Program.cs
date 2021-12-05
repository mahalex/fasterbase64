using BenchmarkDotNet.Running;

public class Program
{
    public static void Main()
    {
        BenchmarkRunner.Run<FasterToBase64Benchmarks>();
    }
}
