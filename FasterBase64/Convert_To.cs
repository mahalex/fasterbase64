// The following code is based on
// Wojciech Muła, Daniel Lemire, Faster Base64 Encoding and Decoding using AVX2 instructions,
// arXiv:1704.00605v5.

// We process input data in chunks of 24 bytes; every chunk becomes
// 32 encoded characters.
// If there are leftovers that are smaller than 24 bytes, we process
// them by calling the .NET implementation.
// The main processing routine requires that input 24 bytes are located
// in the middle 24 bytes of a 32-byte vector that we load from memory.
// Due to that, special handling is required for the first chunk
// (we have to load first 32 bytes and shift it right by 4 bytes).
// Processing consists of three steps:
// 1. Split 24 input bytes into 32 6-bit chunks,
//    each chunk stored into the lower 6 bits of consecutive bytes.
//    This is where we need the data to be in the middle (and not the first)
//    24 bytes: we don't want to cross the lanes.
//    This way, the first 12 bytes stay in the first half of
//    Vector256, and expand to fill the first half; same with the
//    second half.
// 2. Convert a sequence of 32 bytes (with possible values from 0 to 63)
//    to their ASCII representations in base64.
// 3. Stores the resulting 32 bytes into the low bytes of 32 words
//    at the output location.
// Below we describe the steps in more detail.
//
// FIRST STEP: splitting into 6-bit chunks.
// Here we describe the procedure for the first half (containing
// the first 12 bytes of the input); the second half is processed
// at the same time in a symmetrical fashion.
// We split these 12 bytes into 4 chunks of 3 bytes each.
// Our target is to re-shuffle these 24 bits into 4 chunks
// of 6 bits each, and write them into lower bits of 4
// consecutive bytes:
// xxxxxxxx | yyyyyyyy | zzzzzzzz
// ->
// 00xxxxxx | 00xxyyyy | 00yyyyzz | 00zzzzzz
// First, we shuffle these 3 bytes into 4 bytes (repeating
// the middle byte) in the following way:
// xxxxxxxx | yyyyyyyy | zzzzzzzz              // inputVector
// ->
// yyyyyyyy | xxxxxxxx | zzzzzzzz | yyyyyyyy   // inputWithRepeat
// Then, we AND the result with 0x0fc0fc00 to extract both
// the "bbbbcc" and "aaaaaa" parts:
// 00000000 | 11111100 | 11000000 | 00001111   // 0x0fc0fc00
// 00000000 | xxxxxx00 | zz000000 | 0000yyyy   // masked1
// Another AND (with 0x003f03f0) extracts the "aabbbb" and "cccccc" parts:
// 11110000 | 00000011 | 00111111 | 00000000   // 0x003f03f0
// yyyy0000 | 000000xx | 00zzzzzz | 00000000   // masked2
// Multiplication shifts these parts into their proper place
// (note that we use multiplication with storing high words
// to effectively achieve right shift; and multiplication with
// storing low words to effectively achieve left shift).
// To understand how multiplication works, we need to rewrite
// our data as words:
// xxxxxx0000000000 | 0000yyyyzz000000         // masked1
// 0000000001000000 | 0000010000000000         // shift1
// 0000000000xxxxxx | 0000000000yyyyzz         // maskedAndShifted1
// Similarly, for the second part:
// 000000xxyyyy0000 | 0000000000zzzzzz         // masked2
// 0000000000010000 | 0000000100000000         // shift2
// 00xxyyyy00000000 | 00zzzzzz00000000         // maskedAndShifted2
// After rewriting it back to bytes, we get
// 00xxxxxx | 00000000 | 00yyyyzz | 00000000   // maskedAndShifted1
// 00000000 | 00xxyyyy | 00000000 | 00zzzzzz   // maskedAndShifted2
// Final result is OR of these two.
//
// SECOND STEP: encoding 6-bit values in base64.
// Recall that base64 maps
//  0..25 -> A..Z  (ASCII codes 65..90)
// 26..51 -> a..z (ASCII codes 97..122)
// 52..61 -> 0..9 (ASCII codes 48..57)
// 62     -> + (ASCII code 43)
// 63     -> . (ASCII code 47)
// Thus our job is to add the following offsets:
// 65 to values 0..25
// 71 to values 26..51
// -4 to values 52..61
// -19 to value 62
// -16 to value 63
// First, we subract 51 with saturation, so 0..25 and 26..51 become zero,
// while values 52..63 become 1..12.
// We have an offset map that says that 2..11 should map to -4,
// 12 should map to -19, and 13 should map to -16.
// Thus, the values 0..25 currently map to 0, while
// the mapping for all other values is off by 1.
// Now we need to add 1 in cases 26..63, and add 0 in cases 0..25.
// This is done by comparing the values with 25:
// if a value is greater than 25, we get -1, otherwise 0.
// This is (up to a sign) what we wanted, so we can just
// subtract the comparison result to achieve the required +1.
// It remains to apply the offset map and add the resulting
// offset to the input.
//
// THIRD STEP: store result to memory.
// This is quite easy: store the lower half of a 32-byte vector,
// interleaving bytes with zeros, then do the same for the higher half.

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace FasterBase64
{
    public static partial class Convert
    {
        public static unsafe bool TryToBase64Chars(ReadOnlySpan<byte> bytes, Span<char> chars, out int charsWritten)
        {
            var inputLength = bytes.Length;
            charsWritten = 0;
            if (inputLength == 0)
            {
                return true;
            }
            
            var outputLength = chars.Length;
            var expectedLength = (1 + (inputLength - 1) / 3) * 4;
            if (outputLength < expectedLength)
            {
                return false;
            }

            var permuter = Vector256.Create(0, 0, 1, 2, 3, 4, 5, 6);
            var mask1 = Vector256.Create(0x0fc0fc00).AsByte();
            var shift1 = Vector256.Create(0x04000040).AsUInt16();
            var mask2 = Vector256.Create(0x003f03f0).AsByte();
            var shift2 = Vector256.Create(0x01000010).AsUInt16();
            var const51 = Vector256.Create((byte)51);
            var const25 = Vector256.Create((byte)25);
            var shuffleVector = Vector256.Create(
                (byte)5, 4, 6, 5, 8, 7, 9, 8, 11, 10, 12, 11, 14, 13, 15, 14,
                1, 0, 2, 1, 4, 3, 5, 4, 7, 6, 8, 7, 10, 9, 11, 10);
            var offsetMap = Vector256.Create(
                (sbyte)65, 71, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -19, -16, 0, 0,
                65, 71, -4, -4, -4, -4, -4, -4, -4, -4, -4, -4, -19, -16, 0, 0).AsByte();

            if (inputLength >= 32)
            {
                fixed (byte* bytesPtr = bytes)
                fixed (short* charsPtr = MemoryMarshal.Cast<char, short>(chars))
                {
                    var currentInputPtr = bytesPtr;
                    var currentOutputPtr = charsPtr;
                    Vector256<byte> inputVector;
                    var preInputVector = Avx2.LoadVector256(currentInputPtr);
                    currentInputPtr -= 4;
                    inputVector = Avx2.PermuteVar8x32(preInputVector.AsInt32(), permuter).AsByte();
MainLoop:
                    var inputWithRepeat = Avx2.Shuffle(inputVector, shuffleVector);
                    var masked1 = Avx2.And(inputWithRepeat, mask1);
                    var maskedAndShifted1 = Avx2.MultiplyHigh(masked1.AsUInt16(), shift1);
                    var masked2 = Avx2.And(inputWithRepeat, mask2);
                    var maskedAndShifted2 = Avx2.MultiplyLow(masked2.AsUInt16(), shift2);
                    var shuffled = Avx2.Or(maskedAndShifted1, maskedAndShifted2).AsByte();

                    var shuffleResult = Avx2.SubtractSaturate(shuffled, const51);
                    var less = Avx2.CompareGreaterThan(shuffled.AsSByte(), const25.AsSByte()).AsByte();
                    shuffleResult = Avx2.Subtract(shuffleResult, less);
                    var offsets = Avx2.Shuffle(offsetMap, shuffleResult);
                    var translated = Avx2.Add(offsets, shuffled);

                    var lower = translated.GetLower();
                    var lowerInterleaved = Avx2.ConvertToVector256Int16(lower);
                    Avx2.Store(currentOutputPtr, lowerInterleaved);
                    currentOutputPtr += 16;
                    var upper = translated.GetUpper();
                    var upperInterleaved = Avx2.ConvertToVector256Int16(upper);
                    Avx2.Store(currentOutputPtr, upperInterleaved);
                    currentOutputPtr += 16;

                    currentInputPtr += 24;
                    inputLength -= 24;
                    if (inputLength >= 28)
                    {
                        inputVector = Avx2.LoadVector256(currentInputPtr);
                        goto MainLoop;
                    }

                    charsWritten = (int)(currentOutputPtr - charsPtr);
                }
            }

            var result = System.Convert.TryToBase64Chars(bytes[^inputLength..], chars[charsWritten..], out var charsWritten2);
            charsWritten += charsWritten2;
            return result;
        }
    }
}