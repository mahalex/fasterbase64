using FluentAssertions;
using FsCheck.Xunit;
using System;
using System.Collections.Generic;
using Xunit;

namespace FasterBase64.Tests
{
    public class ConvertTests
    {
        [Property(MaxTest = 1000)]
        public void TestTryToBase64CharsRandom(byte[] bytes)
        {
            var n = bytes.Length;
            var charsLength = GetExactLengthInChars(n);
            var expectedChars = new char[charsLength];
            var expected = System.Convert.TryToBase64Chars(bytes, expectedChars, out var expectedCharsWritten);

            var actualChars = new char[charsLength];
            var actual = FasterBase64.Convert.TryToBase64Chars(bytes, actualChars, out var actualCharsWritten);
            actual.Should().Be(expected);
            actualCharsWritten.Should().Be(expectedCharsWritten);
            for (var i = 0; i < n; i++)
            {
                actualChars[i].Should().Be(expectedChars[i]);
            }
        }

        [Theory]
        [MemberData(nameof(Base64Pairs))]
        public void TestTryToBase64CharsExactSize(byte[] bytes, char[] chars)
        {
            var n = chars.Length;
            var actualChars = new char[n];
            var actual = FasterBase64.Convert.TryToBase64Chars(bytes, actualChars, out var charsWritten);
            actual.Should().BeTrue();
            charsWritten.Should().Be(n);
            for (var i = 0; i < n; i++)
            {
                actualChars[i].Should().Be(chars[i]);
            }
        }

        [Theory]
        [MemberData(nameof(Base64Pairs))]
        public void TestTryFromBase64CharsExactSize(byte[] bytes, char[] chars)
        {
            var n = bytes.Length;
            var actualBytes = new byte[n];
            var actual = FasterBase64.Convert.TryFromBase64Chars(chars, actualBytes, out int bytesWritten);

            actual.Should().BeTrue();
            bytesWritten.Should().Be(n);
            for (var i = 0; i < n; i++)
            {
                actualBytes[i].Should().Be(bytes[i]);
            }
        }

        [Theory]
        [MemberData(nameof(TryToBase64CharsWrongSizeTestData))]
        public void TestTryToBase64CharsWrongSize(byte[] bytes, bool expected, char[] chars, int charsWritten)
        {
            var n = chars.Length;
            var actualChars = new char[n];
            var actual = FasterBase64.Convert.TryToBase64Chars(bytes, actualChars, out var actualCharsWritten);
            actual.Should().Be(expected);
            actualCharsWritten.Should().Be(charsWritten);
            for (var i = 0; i < n; i++)
            {
                actualChars[i].Should().Be(chars[i]);
            }
        }

        [Theory]
        [MemberData(nameof(TryFromBase64CharsWrongSizeTestData))]
        [MemberData(nameof(TryFromBase64CharsInvalidCharsTestData))]
        public void TestTryFromBase64CharsWrong(char[] chars, int n, int bytesWritten)
        {
            var actualBytes = new byte[n];
            var actual = FasterBase64.Convert.TryFromBase64Chars(chars, actualBytes, out var actualBytesWritten);
            actual.Should().BeFalse();
            actualBytesWritten.Should().Be(bytesWritten);
        }

        public static IEnumerable<object[]> Base64Pairs()
        {
            foreach (var (bytes, chars) in TryToBase64CharsExactSizeTestDataTyped())
            {
                yield return new object[] { bytes, chars };
            }
        }

        public static IEnumerable<object[]> TryToBase64CharsWrongSizeTestData()
        {
            foreach (var (bytes, expected, chars, charsWritten) in TryToBase64CharsWrongSizeTestDataTyped())
            {
                yield return new object[] { bytes, expected, chars, charsWritten };
            }
        }

        public static IEnumerable<object[]> TryFromBase64CharsWrongSizeTestData()
        {
            foreach (var (chars, n, bytesWritten) in TryFromBase64CharsWrongSizeTestDataTyped())
            {
                yield return new object[] { chars, n, bytesWritten };
            }
        }

        public static IEnumerable<object[]> TryFromBase64CharsInvalidCharsTestData()
        {
            foreach (var (chars, n, bytesWritten) in TryFromBase64CharsInvalidCharsTestDataTyped())
            {
                yield return new object[] { chars, n, bytesWritten };
            }
        }

        private static IEnumerable<(byte[] Bytes, char[] Chars)> TryToBase64CharsExactSizeTestDataTyped()
        {
            var random = new Random(1);

            foreach (var bytes in TestBytes(random))
            {
                var n = bytes.Length;
                var charsLength = GetExactLengthInChars(n);

                var chars = new char[charsLength];
                System.Convert.TryToBase64Chars(bytes, chars, out var _);
                yield return (bytes, chars);
            }
        }

        private static IEnumerable<(byte[] Bytes, bool Expected, char[] Chars, int CharsWritten)> TryToBase64CharsWrongSizeTestDataTyped()
        {
            var random = new Random(1);

            foreach (var bytes in TestBytes(random))
            {
                var n = bytes.Length;
                var exactCharsLength = GetExactLengthInChars(n);
                var charsLength = random.Next(0, exactCharsLength + 10);

                var chars = new char[charsLength];
                var expected = System.Convert.TryToBase64Chars(bytes, chars, out var charsWritten);
                yield return (bytes, expected, chars, charsWritten);
            }
        }

        private static IEnumerable<(char[] Chars, int N, int BytesWritten)> TryFromBase64CharsWrongSizeTestDataTyped()
        {
            var random = new Random(1);

            foreach (var bytes in TestBytes(random))
            {
                var n = bytes.Length;
                if (n == 0)
                {
                    continue;
                }

                var charsLength = GetExactLengthInChars(n);
                var chars = new char[charsLength];
                System.Convert.TryToBase64Chars(bytes, chars, out var charsWritten);
                var wrongN = random.Next(0, n - 1);
                var wrongBytes = new byte[wrongN];
                var expected = System.Convert.TryFromBase64Chars(chars, wrongBytes, out var bytesWritten);
                yield return (chars, wrongN, bytesWritten);
            }
        }

        private static IEnumerable<(char[] Chars, int N, int BytesWritten)> TryFromBase64CharsInvalidCharsTestDataTyped()
        {
            var random = new Random(1);
            var invalidChars = " \n\r\t!@#$%^&*()_\\\x0430\x0410".ToCharArray();

            foreach (var bytes in TestBytes(random))
            {
                var n = bytes.Length;
                if (n == 0)
                {
                    continue;
                }

                var charsLength = GetExactLengthInChars(n);
                var chars = new char[charsLength];
                System.Convert.TryToBase64Chars(bytes, chars, out var charsWritten);
                var wrongIndex = random.Next(0, charsLength - 1);
                var wrongChar = invalidChars[random.Next(0, invalidChars.Length - 1)];
                chars[wrongIndex] = wrongChar;
                var wrongBytes = new byte[n];
                var expected = System.Convert.TryFromBase64Chars(chars, wrongBytes, out var bytesWritten);
                yield return (chars, n, bytesWritten);
            }
        }

        private static int GetExactLengthInChars(int lengthInBytes)
        {
            return lengthInBytes == 0 ? 0 : (1 + (lengthInBytes - 1) / 3) * 4;
        }

        private static IEnumerable<byte[]> TestBytes(Random random)
        {
            yield return new byte[] { };
            yield return new byte[] { 0 };
            yield return new byte[] { 255 };
            for (var n = 1; n <= 128; n++)
            {
                for (var iteration = 1; iteration <= 10; iteration++)
                {
                    var bytes = new byte[n];
                    random.NextBytes(bytes);
                    yield return bytes;
                }
            }
        }
    }
}