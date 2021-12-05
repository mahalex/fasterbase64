# FasterBase64

An implementation of a fast base64 encoding algorithm in C#.
The algorithm is described in
[Faster Base64 Encoding and Decoding Using AVX2 Instructions](https://arxiv.org/abs/1704.00605)
by Wojciech Muła and Daniel Lemire.

Benchmarks show that this implementation is about 8-10 times faster
than the standard .NET implementation
in `System.Convert.TryToBase64Chars()` and `System.Convert.TryFromBase64Chars()`.
We provide re-implementations of these two methods
within a static class `FasterBase64.Convert`.

* `FasterBase64.Convert.TryToBase64Chars()` works
  the same way as `System.Convert.TryToBase64Chars()`;
* `System.FasterBase64.TryFromBase64Chars()` works
  the same way as`System.Convert.TryFromBase64Chars()`,
  if the input does not contain whitespace. The standard .NET implementation
  differs from the RFC4648 standard in that it allows whitespace in the data.
  If you need an implementation that skips whitespace, it is easy
  to copy the data, omitting whitespace, and then call
  `System.FasterBase64.TryFromBase64Chars()`.
  We believe that (in all reasonable cases) this is still faster than
  using the standard implementation.

Caveats:
* Although we believe the implementation to be reasonably well tested,
  there might still be bugs.
* The implementation uses AVX2 instructions, but does not check if AVX2
  is available. AVX2 support can be easily checked by querying the property
  `System.Runtime.Intrinsics.X86.Avx2.IsSupported`.

TODO:
* NuGet package.

License:
* Copyright (c) Alexander Luzgarev, 2021, under the GPL license.
