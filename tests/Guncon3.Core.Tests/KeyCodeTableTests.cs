using System.Linq;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class KeyCodeTableTests
    {
        [Theory]
        [InlineData(4, "a")]
        [InlineData(29, "z")]
        [InlineData(30, "1")]
        [InlineData(39, "0")]
        [InlineData(40, "ENTER")]
        [InlineData(44, "SPACEBAR")]
        [InlineData(69, "F12")]
        [InlineData(99, "K.")]
        [InlineData(111, "F24")]
        public void NameOf_ReturnsDocumentedName(byte code, string expected)
            => Assert.Equal(expected, KeyCodeTable.NameOf(code));

        [Fact]
        public void NameOf_ReturnsNullForUnnamedCodes()
        {
            Assert.Null(KeyCodeTable.NameOf(0));
            Assert.Null(KeyCodeTable.NameOf(50));
            Assert.Null(KeyCodeTable.NameOf(200));
        }

        [Fact]
        public void Entries_AreAscendingAndWithinRange()
        {
            var codes = KeyCodeTable.Entries.Select(e => e.Code).ToArray();

            Assert.NotEmpty(codes);
            Assert.Equal(codes.OrderBy(c => c), codes);
            Assert.All(codes, c => Assert.InRange(c, (byte)4, (byte)111));
            Assert.All(KeyCodeTable.Entries, e => Assert.False(string.IsNullOrEmpty(e.Name)));
        }
    }
}
