// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class GunconDecoderTests
    {
        private const string GoldenPath = "data/decode-golden.txt";
        private const string CapturePath = "data/packets.txt";
        private const string CaptureGoldenPath = "data/packets-decoded.txt";

        /// <summary>
        /// Builds a frame whose checksum passes, by solving the a_sum chain for data[0].
        /// </summary>
        private static byte[] MakeValidFrame(Random rng)
        {
            var d = new byte[15];
            rng.NextBytes(d);

            long bSum = d[13] ^ d[12];
            bSum = bSum + d[11] + d[10] - d[9] - d[8];
            bSum = (bSum ^ d[7]) & 0xFF;

            long partial = d[6] ^ bSum;
            partial = partial - d[5] - d[4];
            partial = (partial ^ d[3]) + d[2] + d[1];

            d[0] = (byte)((partial - GunconDecoder.Key[7]) & 0xFF);
            return d;
        }

        private static IEnumerable<byte[]> Corpus()
        {
            var rng = new Random(20260902);
            for (int i = 0; i < 200; i++)
                yield return MakeValidFrame(rng);
        }

        /// <summary>One golden line: the frame in hex, a space, the 13 decoded bytes in hex or NULL.</summary>
        private static string GoldenLine(byte[] frame)
        {
            var decoded = new byte[13];
            return Convert.ToHexString(frame) + " "
                 + (GunconDecoder.TryDecode(frame, decoded) ? Convert.ToHexString(decoded) : "NULL");
        }

        [Fact]
        public void TryDecode_MatchesTheSyntheticGolden()
        {
            var produced = Corpus().Select(GoldenLine).ToArray();

            Assert.True(File.Exists(GoldenPath), $"{GoldenPath} is missing; it is committed test data.");
            Assert.Equal(File.ReadAllLines(GoldenPath), produced);
        }

        [Fact]
        public void TryDecode_DecodesEveryRealCapturedFrame()
        {
            Assert.True(File.Exists(CapturePath), $"{CapturePath} is missing; it is committed test data.");

            var frames = File.ReadAllLines(CapturePath).Where(l => l.Length == 30).Select(Convert.FromHexString).ToArray();
            Assert.Equal(500, frames.Length);

            var decoded = new byte[13];
            var rejected = new List<int>();
            for (int i = 0; i < frames.Length; i++)
                if (!GunconDecoder.TryDecode(frames[i], decoded)) rejected.Add(i);

            Assert.True(rejected.Count == 0,
                $"{rejected.Count} of {frames.Length} real frames failed the checksum; first at line {rejected.FirstOrDefault() + 1}");
        }

        [Fact]
        public void TryDecode_MatchesTheRealCaptureGolden()
        {
            var frames = File.ReadAllLines(CapturePath).Where(l => l.Length == 30).Select(Convert.FromHexString);
            var produced = frames.Select(GoldenLine).ToArray();

            if (!File.Exists(CaptureGoldenPath))
            {
                File.WriteAllLines(CaptureGoldenPath, produced);
                Assert.Fail($"Golden file did not exist; wrote {produced.Length} lines to {CaptureGoldenPath}. Inspect it, copy it into tests/Guncon3.Core.Tests/data/, commit it, and re-run.");
            }

            Assert.Equal(File.ReadAllLines(CaptureGoldenPath), produced);
        }

        [Fact]
        public void TryDecode_RejectsAChecksumFailure()
        {
            var frame = MakeValidFrame(new Random(1));
            frame[0] ^= 0xFF;
            Assert.False(GunconDecoder.TryDecode(frame, new byte[13]));
        }

        [Fact]
        public void TryDecode_RejectsBadInput()
        {
            var destination = new byte[13];

            Assert.False(GunconDecoder.TryDecode(new byte[14], destination));
            Assert.False(GunconDecoder.TryDecode(new byte[16], destination));
            Assert.False(GunconDecoder.TryDecode(MakeValidFrame(new Random(2)), new byte[12]));
        }

        [Fact]
        public void TryDecode_LeavesTheDestinationUntouchedWhenItRejects()
        {
            var destination = Enumerable.Repeat((byte)0xAA, 13).ToArray();
            var broken = MakeValidFrame(new Random(3));
            broken[0] ^= 0xFF;

            Assert.False(GunconDecoder.TryDecode(broken, destination));
            Assert.All(destination, b => Assert.Equal(0xAA, b));
        }

        [Fact]
        public void TryDecode_DoesNotAllocate()
        {
            var frame = MakeValidFrame(new Random(4));
            var destination = new byte[13];

            GunconDecoder.TryDecode(frame, destination);   // warm up the JIT

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
                GunconDecoder.TryDecode(frame, destination);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.Equal(0, after - before);
        }
    }
}
