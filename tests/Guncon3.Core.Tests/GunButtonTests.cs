using System;
using System.Linq;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class GunButtonTests
    {
        [Fact]
        public void Parse_IsCaseInsensitiveWhenAsked()
        {
            Assert.True(Enum.TryParse<GunButton>("trigger", ignoreCase: true, out var b));
            Assert.Equal(GunButton.Trigger, b);
        }

        /// <summary>
        /// Pins down the whole enum: the two assertions below cover every member,
        /// in order, so this is also the completeness check — there is deliberately
        /// no second test restating the same list.
        /// <para>
        /// The joystick feeder packs only the nine physical buttons into report
        /// indices 0..8. It does so from an array of named members, not from the
        /// enum's ordinal values, so a reordering would not by itself remap
        /// anything — this test pins down which buttons are physical and the
        /// order the feeder is expected to use, so a reorder that no longer
        /// matches that expectation is caught and looked at rather than assumed
        /// harmless.
        /// </para>
        /// </summary>
        [Fact]
        public void PhysicalButtons_OccupyTheFirstNineIndicesInDeclarationOrder()
        {
            var values = Enum.GetValues<GunButton>();

            var physical = values.Take(9).Select(v => v.ToString()).ToArray();
            Assert.Equal(new[]
            {
                "Trigger", "A1", "A2", "B1", "B2", "C1", "C2", "AClick", "BClick"
            }, physical);

            // Everything after index 8 is a digitalized stick direction, never a
            // physical button, and must never be sent as a joystick button.
            var rest = values.Skip(9).Select(v => v.ToString()).ToArray();
            Assert.Equal(new[]
            {
                "LUp", "LDown", "LLeft", "LRight",
                "RUp", "RDown", "RLeft", "RRight"
            }, rest);
        }
    }
}
