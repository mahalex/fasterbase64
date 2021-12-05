// The following code is based on
// Wojciech Muła, Daniel Lemire, Faster Base64 Encoding and Decoding using AVX2 instructions,
// arXiv:1704.00605v5.

// We process input data in chunks of 32 chars; every chunk becomes
// 24 decoded bytes.
// If there are leftovers that are smaller than 34 (!) bytes, we process
// them by calling the .NET implementation. The reason for the number 34
// is we don't want to deal with '=' padding characters that can
// happen at the end of the string (up to 2 of them).
// Note, however, that (in contrast to the .NET implementation)
// we do not allow whitespace in the input.
// If you know that your input might contain whitespace characters,
// you should remove them first, and then call the conversion method.
//
// Processing consists of three steps:
// 1. Take the next 32 characters and decode them from WTF-16 to 6-bit values.
//    Here we also check that all characters in the input are valid.
//    After this step, we have a vector of 32 bytes, each having a value
//    between 0 and 63.
// 2. Pack these 32 6-bit chunks into 24 bytes.
// 3. Write the resulting bytes to the output location.
// Below we describe the steps in more detail.
//
// FIRST STEP: decoding WTF-16 into 6-bit values.
// We start by taking two blocks of 16 characters (32 bytes),
// checking them for the values that do not fit into the first byte,
// and pack 32 characters into 32 bytes. This involves crossing the
// lanes.
// Every byte is then split into high and low nibble.
// A character is a valid base64 character if and only if
// a) its high nibble H is 2, 3, 4, 5, 6, 7;
// b) when H = 2, the low nibble is 11 or 15;
//    when H = 3, the low nibble is in 0..9;
//    when H = 4 or 6, the low nibble is non-zero;
//    when H = 5 or 7, the low nibble is in 0..10.
// This is exactly what happens in the code; see Appendix C
// of [Muła, Lemire] for the detailed explanations.
//
// SECOND STEP: packing 32 6-bit chunks into 24 bytes.
// To achieve that we need only four instructions; namely, we
// a) use MultiplyAddAdjacent (on byte level) to pack the data
//    within (16-bit) words;
// b) use MultiplyAddAdjacent (on word level) to pack the data
//    within (32-bit) double words;
// c) use Shuffle to pack the data within 128-bit lanes;
// d) use PermuteVar8x32 to pack data into first 24 bytes of
//    our 32-byte vector. This, of course, involves crossing
//    the lanes.
// We demonstrate the first two steps on 32-bit double words:
// For a), we multiply the bytes alternately with 0x40 and 0x01,
// which results in shifts by 6 bits of every other byte.
// Then the instruction adds together adjacent pairs of resulting
// 16-bit integers. So, starting from
// 00xxxxxx | 00yyyyyy | 00zzzzzz | 00tttttt
// we multiply by
// 01000000 | 00000001 | 01000000 | 00000001
// to get intermediate results
// 0000xxxxxx000000, 0000000000yyyyyy, 0000zzzzzz000000, 0000000000tttttt
// and add them pairwise, so we get
// 0000xxxxxxyyyyyy | 0000zzzzzztttttt
// Rewriting this into bytes, we get
// xxyyyyyy | 0000xxxx | zztttttt | 0000zzzz
// Step b) is similar: we shift every other word by 12 bits.
// So, we multiply by
// 0001000000000000 | 0000000000000001
// to get intermediate results
// 00000000xxxxxxyyyyyy000000000000 and 00000000000000000000zzzzzztttttt
// and add them pairwise, so we get
// 00000000xxxxxxyyyyyyzzzzzztttttt
// This is exactly what is needed to pack four 6-bit values into
// three 8-bit values.
// Step c) then shuffles the bytes to pack the data bytes at the
// beginning of each lane; step d) permutes them to move everything
// into the first 24 bytes.

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace FasterBase64
{
    public static partial class Convert
    {
        public static unsafe bool TryFromBase64Chars(
            ReadOnlySpan<char> chars,
            Span<byte> bytes,
            out int bytesWritten)
        {
            var inputLength = chars.Length;
            var outputLength = bytes.Length;
            bytesWritten = 0;
            if (inputLength >= 34)
            {
                var utf8mask = Vector256.Create((ushort)0xff00).AsInt16();
                var const2f = Vector256.Create((byte) 0x2f);
                var lutLo = Vector256.Create(
                    (byte)21, 17, 17, 17, 17, 17, 17, 17, 17, 17, 19, 26, 27, 27, 27, 26,
                    21, 17, 17, 17, 17, 17, 17, 17, 17, 17, 19, 26, 27, 27, 27, 26);
                var lutHi = Vector256.Create(
                    (byte)16, 16, 1, 2, 4, 8, 4, 8, 16, 16, 16, 16, 16, 16, 16, 16,
                    16, 16, 1, 2, 4, 8, 4, 8, 16, 16, 16, 16, 16, 16, 16, 16);
                var lutRoll = Vector256.Create(
                    (sbyte)0, 16, 19, 4, -65, -65, -71, -71, 0, 0, 0, 0, 0, 0, 0, 0,
                    0, 16, 19, 4, -65, -65, -71, -71, 0, 0, 0, 0, 0, 0, 0, 0);
                var helper1 = Vector256.Create(0x01400140).AsSByte();
                var helper2 = Vector256.Create(0x00011000);
                var helper3 = Vector256.Create(
                    (sbyte)2, 1, 0, 6, 5, 4, 10, 9, 8, 14, 13, 12, -1, -1, -1, -1,
                    2, 1, 0, 6, 5, 4, 10, 9, 8, 14, 13, 12, -1, -1, -1, -1);
                var helper4 = Vector256.Create(0, 1, 2, 4, 5, 6, -1, -1);
                fixed (byte* bytesPtr = bytes)
                fixed (short* charsPtr = MemoryMarshal.Cast<char, short>(chars))
                {
                    var currentBytesPtr = bytesPtr;
                    var currentInputPtr = charsPtr;

                    while (inputLength >= 34 && outputLength >= 32)
                    {
                        var input1 = Avx2.LoadVector256(currentInputPtr);
                        if (!Avx2.TestZ(input1, utf8mask))
                        {
                            bytesWritten = 0;
                            return false;
                        }

                        var input2 = Avx2.LoadVector256(currentInputPtr + 16);
                        if (!Avx2.TestZ(input2, utf8mask))
                        {
                            bytesWritten = 0;
                            return false;
                        }

                        currentInputPtr += 32;
                        inputLength -= 32;

                        var packedInput = Avx2.PackUnsignedSaturate(input1, input2);
                        var input = Avx2.Permute4x64(packedInput.AsUInt64(), (byte)0b_11_01_10_00).AsByte();

                        var hiNibbles = Avx2.ShiftRightLogical(input.AsInt32(), 4).AsByte();
                        var loNibbles = Avx2.And(input, const2f);
                        var lo = Avx2.Shuffle(lutLo, loNibbles);
                        var eq2f = Avx2.CompareEqual(input, const2f);
                        hiNibbles = Avx2.And(hiNibbles, const2f);
                        var hi = Avx2.Shuffle(lutHi, hiNibbles);
                        var roll = Avx2.Shuffle(lutRoll, Avx2.Add(eq2f, hiNibbles).AsSByte());
                        if (!Avx2.TestZ(lo, hi))
                        {
                            bytesWritten = 0;
                            return false;
                        }

                        var fromAscii = Avx2.Add(input.AsSByte(), roll);

                        var mergeXYandZT = Avx2.MultiplyAddAdjacent(fromAscii.AsByte(), helper1);
                        var packedWithinLanes = Avx2.MultiplyAddAdjacent(mergeXYandZT, helper2.AsInt16());
                        packedWithinLanes = Avx2.Shuffle(packedWithinLanes.AsByte(), helper3.AsByte()).AsInt32();
                        var final = Avx2.PermuteVar8x32(packedWithinLanes, helper4).AsByte();
                        Avx2.Store(currentBytesPtr, final);

                        bytesWritten += 24;
                        currentBytesPtr += 24;
                        outputLength -= 24;
                    }
                }
            }

            var result = System.Convert.TryFromBase64Chars(chars[^inputLength..], bytes[bytesWritten..], out var bytesWritten2);
            if (result)
            {
                bytesWritten += bytesWritten2;
            }
            else
            {
                bytesWritten = 0;
            }

            return result;
        }
    }
}