using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class JoystickReportStateTests
    {
        // A deliberately asymmetric baseline: every field holds a different value,
        // so a comparison that comes from the wrong field cannot pass by accident.
        private static JoystickReportState Baseline()
            => new JoystickReportState(100, 200, 300, 400, 500, 0x0F, 0x01);

        [Fact]
        public void TwoIdenticalStates_AreEqual()
        {
            Assert.True(Baseline().Equals(Baseline()));
            Assert.True(Baseline() == Baseline());
            Assert.False(Baseline() != Baseline());
        }

        /// <summary>
        /// The test that earns this type its place: one case per field, each
        /// changing that field alone. A field the comparison forgets shows up here
        /// as an equality that should have been false — and nowhere else, because
        /// the only symptom at runtime is a virtual axis that silently stops
        /// following the stick.
        /// </summary>
        [Theory]
        [InlineData("X")]
        [InlineData("Y")]
        [InlineData("RX")]
        [InlineData("RY")]
        [InlineData("Z")]
        [InlineData("Buttons0")]
        [InlineData("Buttons1")]
        public void ChangingAnySingleField_MakesTheStatesUnequal(string field)
        {
            var a = Baseline();

            var b = field switch
            {
                "X" => new JoystickReportState(101, 200, 300, 400, 500, 0x0F, 0x01),
                "Y" => new JoystickReportState(100, 201, 300, 400, 500, 0x0F, 0x01),
                "RX" => new JoystickReportState(100, 200, 301, 400, 500, 0x0F, 0x01),
                "RY" => new JoystickReportState(100, 200, 300, 401, 500, 0x0F, 0x01),
                "Z" => new JoystickReportState(100, 200, 300, 400, 501, 0x0F, 0x01),
                "Buttons0" => new JoystickReportState(100, 200, 300, 400, 500, 0x1F, 0x01),
                "Buttons1" => new JoystickReportState(100, 200, 300, 400, 500, 0x0F, 0x00),
                _ => a
            };

            Assert.False(a.Equals(b), $"changing {field} left the states equal");
            Assert.True(a != b);
        }

        [Fact]
        public void Constructor_KeepsEachValueInItsOwnField()
        {
            var s = Baseline();

            Assert.Equal(100, s.X);
            Assert.Equal(200, s.Y);
            Assert.Equal(300, s.RX);
            Assert.Equal(400, s.RY);
            Assert.Equal(500, s.Z);
            Assert.Equal(0x0F, s.Buttons0);
            Assert.Equal(0x01, s.Buttons1);
        }

        [Fact]
        public void EqualStates_ShareAHashCode()
        {
            Assert.Equal(Baseline().GetHashCode(), Baseline().GetHashCode());
        }

        [Fact]
        public void Default_IsNotEqualToAPopulatedState()
        {
            Assert.NotEqual(default, Baseline());
        }
    }
}
