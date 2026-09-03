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

        [Fact]
        public void Decode_MatchesGolden()
        {
            var produced = Corpus()
                .Select(f => Convert.ToHexString(f) + " " + (GunconDecoder.Decode(f) is List<byte> r ? Convert.ToHexString(r.ToArray()) : "NULL"))
                .ToArray();

            if (!File.Exists(GoldenPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(GoldenPath));
                File.WriteAllLines(GoldenPath, produced);
                Assert.Fail($"Golden file did not exist; wrote {produced.Length} lines to {GoldenPath}. Inspect it, commit it, and re-run.");
            }

            Assert.Equal(File.ReadAllLines(GoldenPath), produced);
        }

        [Fact]
        public void Decode_ProducesThirteenBytesForValidFrames()
        {
            foreach (var frame in Corpus())
            {
                var decoded = GunconDecoder.Decode(frame);
                Assert.NotNull(decoded);
                Assert.Equal(13, decoded.Count);
            }
        }

        [Fact]
        public void Decode_ReturnsNullOnChecksumFailure()
        {
            var frame = MakeValidFrame(new Random(1));
            frame[0] ^= 0xFF;
            Assert.Null(GunconDecoder.Decode(frame));
        }

        [Fact]
        public void Decode_ReturnsEmptyForWrongLength()
        {
            Assert.Empty(GunconDecoder.Decode(new byte[14]));
            Assert.Empty(GunconDecoder.Decode(null));
        }

        [Fact]
        public void Decode_HandlesRealCapturedFrames()
        {
            const string path = "data/packets.txt";
            if (!File.Exists(path)) return;   // capture is optional

            int decoded = 0, rejected = 0;
            foreach (var line in File.ReadAllLines(path).Where(l => l.Length == 30))
            {
                var result = GunconDecoder.Decode(Convert.FromHexString(line));
                if (result == null) rejected++;
                else { Assert.Equal(13, result.Count); decoded++; }
            }

            Assert.True(decoded > 0, "no captured frame decoded");
            Assert.True(rejected < decoded, $"more frames rejected ({rejected}) than decoded ({decoded})");
        }

        [Fact]
        public void TryDecode_AgreesWithDecode()
        {
            var destination = new byte[13];

            foreach (var frame in Corpus())
            {
                var expected = GunconDecoder.Decode(frame);

                Assert.True(GunconDecoder.TryDecode(frame, destination));
                Assert.Equal(expected.ToArray(), destination);
            }
        }

        [Fact]
        public void TryDecode_RejectsBadInput()
        {
            var destination = new byte[13];

            Assert.False(GunconDecoder.TryDecode(new byte[14], destination));
            Assert.False(GunconDecoder.TryDecode(MakeValidFrame(new Random(2)), new byte[12]));

            var broken = MakeValidFrame(new Random(3));
            broken[0] ^= 0xFF;
            Assert.False(GunconDecoder.TryDecode(broken, destination));
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
