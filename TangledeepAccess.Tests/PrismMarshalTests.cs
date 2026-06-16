using System;
using System.Runtime.InteropServices;
using System.Text;
using TangledeepAccess.Speech;
using Xunit;

namespace TangledeepAccess.Tests
{
    /// <summary>
    /// Covers the hand-rolled UTF-8 marshaling in the Prism binding. These are the
    /// only part of the interop layer testable off the native library, and a
    /// regression here would silently corrupt every spoken string, so they are
    /// pinned: NUL termination, multi-byte round-tripping, and empty/edge cases.
    /// </summary>
    public class PrismMarshalTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("Hello world")]
        [InlineData("Tangledeep")]
        [InlineData("café naïve — §")] // accents, em dash, section sign
        [InlineData("あいう")] // Japanese kana (3-byte each)
        [InlineData("\U0001F600")] // emoji (surrogate pair / 4-byte)
        public void RoundTripsThroughNativeBuffer(string input)
        {
            byte[] utf8 = PrismNative.ToUtf8(input);

            // Always NUL-terminated, and exactly one terminator past the payload.
            Assert.Equal(0, utf8[utf8.Length - 1]);
            Assert.Equal(Encoding.UTF8.GetByteCount(input) + 1, utf8.Length);

            // Decoding from a pinned pointer (as the library would hand one back)
            // reproduces the original string.
            GCHandle handle = GCHandle.Alloc(utf8, GCHandleType.Pinned);
            try
            {
                string decoded = PrismNative.FromUtf8(handle.AddrOfPinnedObject());
                Assert.Equal(input, decoded);
            }
            finally
            {
                handle.Free();
            }
        }

        [Fact]
        public void ToUtf8TreatsNullAsEmpty()
        {
            byte[] utf8 = PrismNative.ToUtf8(null);
            Assert.Single(utf8);
            Assert.Equal(0, utf8[0]);
        }

        [Fact]
        public void FromUtf8OfNullPointerIsNull()
        {
            Assert.Null(PrismNative.FromUtf8(IntPtr.Zero));
        }
    }
}
