# FasterBase64

An implementation of a fast base64 encoding algorithm in C#.
The algorithm is described in
[Faster Base64 Encoding and Decoding Using AVX2 Instructions](https://arxiv.org/abs/1704.00605)
by Wojciech Muła and Daniel Lemire.

Benchmarks (see below) show that this implementation is
about 8-10 times faster than the standard .NET implementation
in `System.Convert.TryToBase64Chars()` and `System.Convert.TryFromBase64Chars()`.
We provide re-implementations of these two methods
within a static class `FasterBase64.Convert`.


* `FasterBase64.Convert.TryToBase64Chars()` works
  the same way as `System.Convert.TryToBase64Chars()`;
* `FasterBase64.FasterBase64.TryFromBase64Chars()` works
  the same way as`System.Convert.TryFromBase64Chars()`,
  if the input does not contain whitespace. The standard .NET implementation
  differs from the RFC4648 standard in that it allows whitespace in the data.
  If you need an implementation that skips whitespace, it is easy
  to copy the data, omitting whitespace, and then call
  `System.FasterBase64.TryFromBase64Chars()`.
  We believe that (in all reasonable cases) this is still faster than
  using the standard implementation.

## Benchmarks for encoding

AMD Ryzen 9 3950X 16-Core Processor, 3493 Mhz,
Windows 10.0.22509, .NET 6.0.0 (6.0.21.52210)

| Method |     N |        Mean |     Error |    StdDev |
|------- |------ |------------:|----------:|----------:|
|    Old |   100 |    95.05 ns |  0.758 ns |  0.672 ns |
|    New |   100 |    25.06 ns |  0.507 ns |  0.498 ns |
|    Old |  1000 |   872.76 ns | 11.321 ns | 10.036 ns |
|    New |  1000 |    92.05 ns |  0.633 ns |  0.592 ns |
|    Old | 10000 | 8,723.65 ns | 79.673 ns | 74.526 ns |
|    New | 10000 |   724.58 ns |  9.693 ns |  9.067 ns |

Here "Old" denotes usage of `System.Convert.TryToBase64Chars()`,
"New" denotes usage of `FasterBase64.Convert.TryToBase64Chars()`,
"N" is the size of input byte array.

## Benchmarks for decoding

AMD Ryzen 9 3950X 16-Core Processor, 3493 Mhz,
Windows 10.0.22509, .NET 6.0.0 (6.0.21.52210)

| Method |     N |        Mean |     Error |    StdDev |
|------- |------ |------------:|----------:|----------:|
|    Old |   100 |    94.66 ns |  0.839 ns |  0.743 ns |
|    New |   100 |    54.08 ns |  0.547 ns |  0.457 ns |
|    Old |  1000 |   831.63 ns |  9.560 ns |  8.474 ns |
|    New |  1000 |   131.31 ns |  1.085 ns |  0.906 ns |
|    Old | 10000 | 8,254.94 ns | 64.406 ns | 57.094 ns |
|    New | 10000 | 1,011.61 ns |  6.320 ns |  5.602 ns |

Here "Old" denotes usage of `System.Convert.TryFromBase64Chars()`,
"New" denotes usage of `FasterBase64.Convert.TryFromBase64Chars()`,
"N" is the size of byte array, that is then encoded in base64 using
`System.Convert.TryToBase64Chars()` to form input to the
benchmarked methods.

## Caveats
* Although we believe the implementation to be reasonably well tested,
  there might still be bugs.
* The implementation uses AVX2 instructions, but does not check if AVX2
  is available. AVX2 support can be easily checked by querying the property
  `System.Runtime.Intrinsics.X86.Avx2.IsSupported`.

## TODO
* NuGet package.

## License
* Copyright (c) Alexander Luzgarev, 2021, under the GPL license.
