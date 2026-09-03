using System.Collections.Generic;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class KeySetBuilderTests
    {
        private static KeyValuePair<GunButton, byte>[] Map(params (GunButton Button, byte Code)[] pairs)
        {
            var a = new KeyValuePair<GunButton, byte>[pairs.Length];
            for (int i = 0; i < pairs.Length; i++)
                a[i] = new KeyValuePair<GunButton, byte>(pairs[i].Button, pairs[i].Code);
            return a;
        }

        private static Dictionary<GunButton, bool> Pressed(params GunButton[] down)
        {
            var d = new Dictionary<GunButton, bool>();
            foreach (GunButton b in System.Enum.GetValues<GunButton>()) d[b] = false;
            foreach (var b in down) d[b] = true;
            return d;
        }

        [Fact]
        public void Update_ReportsChangeOnFirstPress()
        {
            var b = new KeySetBuilder();
            var mapping = Map((GunButton.Trigger, 40));

            Assert.True(b.Update(mapping, Pressed(GunButton.Trigger)));
            Assert.Equal(1, b.Count);
            Assert.Equal(40, b.Keys[0]);
        }

        [Fact]
        public void Update_ReportsNoChangeWhenStateIsStable()
        {
            var b = new KeySetBuilder();
            var mapping = Map((GunButton.Trigger, 40));
            var pressed = Pressed(GunButton.Trigger);

            Assert.True(b.Update(mapping, pressed));
            Assert.False(b.Update(mapping, pressed));
            Assert.False(b.Update(mapping, pressed));
        }

        [Fact]
        public void Update_ReportsChangeOnRelease()
        {
            var b = new KeySetBuilder();
            var mapping = Map((GunButton.Trigger, 40));

            b.Update(mapping, Pressed(GunButton.Trigger));

            Assert.True(b.Update(mapping, Pressed()));
            Assert.Equal(0, b.Count);
        }

        [Fact]
        public void Update_SortsKeysAscending()
        {
            var b = new KeySetBuilder();
            var mapping = Map((GunButton.A1, 50), (GunButton.B1, 10), (GunButton.C1, 30));

            b.Update(mapping, Pressed(GunButton.A1, GunButton.B1, GunButton.C1));

            Assert.Equal(3, b.Count);
            Assert.Equal(new byte[] { 10, 30, 50 }, b.Keys.ToArray());
        }

        [Fact]
        public void Update_DeduplicatesSharedKeyCodes()
        {
            var b = new KeySetBuilder();
            var mapping = Map((GunButton.A1, 40), (GunButton.B1, 40));

            b.Update(mapping, Pressed(GunButton.A1, GunButton.B1));

            Assert.Equal(1, b.Count);
            Assert.Equal(40, b.Keys[0]);
        }

        [Fact]
        public void Update_CapsAtSixKeys()
        {
            var b = new KeySetBuilder();
            var mapping = Map(
                (GunButton.Trigger, 4), (GunButton.A1, 5), (GunButton.A2, 6),
                (GunButton.B1, 7), (GunButton.B2, 8), (GunButton.C1, 9),
                (GunButton.C2, 10));

            b.Update(mapping, Pressed(
                GunButton.Trigger, GunButton.A1, GunButton.A2,
                GunButton.B1, GunButton.B2, GunButton.C1, GunButton.C2));

            Assert.Equal(6, b.Count);
        }

        [Fact]
        public void Update_IgnoresButtonsMissingFromPressedState()
        {
            var b = new KeySetBuilder();
            var mapping = Map((GunButton.Trigger, 40));

            Assert.False(b.Update(mapping, new Dictionary<GunButton, bool>()));
            Assert.Equal(0, b.Count);
        }

        [Fact]
        public void Reset_ForcesTheNextUpdateToReportChange()
        {
            var b = new KeySetBuilder();
            var mapping = Map((GunButton.Trigger, 40));
            var pressed = Pressed(GunButton.Trigger);

            b.Update(mapping, pressed);
            Assert.False(b.Update(mapping, pressed));

            b.Reset();
            Assert.True(b.Update(mapping, pressed));
        }

        [Fact]
        public void Update_DoesNotAllocate()
        {
            var b = new KeySetBuilder();
            var mapping = Map((GunButton.Trigger, 40), (GunButton.A1, 41));
            var down = Pressed(GunButton.Trigger);
            var up = Pressed();

            b.Update(mapping, down);

            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
                b.Update(mapping, i % 2 == 0 ? down : up);
            long after = System.GC.GetAllocatedBytesForCurrentThread();

            Assert.Equal(0, after - before);
        }
    }
}
