// SPDX-License-Identifier: GPL-2.0-only
using System;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class KeyTransitionsTests
    {
        private static (byte[] Released, byte[] Pressed) Run(byte[] previous, byte[] current)
        {
            var t = new KeyTransitions();
            t.Compute(previous, current);
            return (t.Released.ToArray(), t.Pressed.ToArray());
        }

        [Fact]
        public void IdenticalSets_ProduceNothing()
        {
            var (released, pressed) = Run(new byte[] { 4, 5, 6 }, new byte[] { 4, 5, 6 });
            Assert.Empty(released);
            Assert.Empty(pressed);
        }

        [Fact]
        public void EmptyToSomething_PressesEverything()
        {
            var (released, pressed) = Run(Array.Empty<byte>(), new byte[] { 4, 5 });
            Assert.Empty(released);
            Assert.Equal(new byte[] { 4, 5 }, pressed);
        }

        [Fact]
        public void SomethingToEmpty_ReleasesEverything()
        {
            var (released, pressed) = Run(new byte[] { 4, 5 }, Array.Empty<byte>());
            Assert.Equal(new byte[] { 4, 5 }, released);
            Assert.Empty(pressed);
        }

        [Fact]
        public void OneKeySwapped_ReleasesTheOldAndPressesTheNew()
        {
            var (released, pressed) = Run(new byte[] { 4, 7 }, new byte[] { 5, 7 });
            Assert.Equal(new byte[] { 4 }, released);
            Assert.Equal(new byte[] { 5 }, pressed);
        }

        [Fact]
        public void Interleaved_SplitsCorrectly()
        {
            var (released, pressed) = Run(new byte[] { 4, 30, 44, 82 }, new byte[] { 30, 40, 44, 79, 90 });
            Assert.Equal(new byte[] { 4, 82 }, released);
            Assert.Equal(new byte[] { 40, 79, 90 }, pressed);
        }

        [Fact]
        public void FullSetToDisjointFullSet_ReleasesSixAndPressesSix()
        {
            var (released, pressed) = Run(new byte[] { 4, 5, 6, 7, 8, 9 }, new byte[] { 10, 11, 12, 13, 14, 15 });
            Assert.Equal(6, released.Length);
            Assert.Equal(6, pressed.Length);
        }

        [Fact]
        public void Compute_ForgetsThePreviousResult()
        {
            var t = new KeyTransitions();
            t.Compute(Array.Empty<byte>(), new byte[] { 4, 5, 6 });
            t.Compute(new byte[] { 4, 5, 6 }, new byte[] { 4, 5, 6 });

            Assert.True(t.Released.IsEmpty);
            Assert.True(t.Pressed.IsEmpty);
        }

        [Fact]
        public void Compute_RejectsMoreThanMaxKeys()
        {
            var t = new KeyTransitions();
            var seven = new byte[KeySetBuilder.MaxKeys + 1];

            Assert.Throws<ArgumentException>(() => t.Compute(seven, Array.Empty<byte>()));
            Assert.Throws<ArgumentException>(() => t.Compute(Array.Empty<byte>(), seven));
        }
    }
}
