using System;
using System.Linq;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class GunButtonTests
    {
        [Fact]
        public void Values_AreStableAndComplete()
        {
            var names = Enum.GetValues<GunButton>().Select(v => v.ToString()).ToArray();

            Assert.Equal(new[]
            {
                "Trigger", "A1", "A2", "B1", "B2", "C1", "C2", "AClick", "BClick",
                "LUp", "LDown", "LLeft", "LRight"
            }, names);
        }

        [Fact]
        public void Parse_IsCaseInsensitiveWhenAsked()
        {
            Assert.True(Enum.TryParse<GunButton>("trigger", ignoreCase: true, out var b));
            Assert.Equal(GunButton.Trigger, b);
        }
    }
}
