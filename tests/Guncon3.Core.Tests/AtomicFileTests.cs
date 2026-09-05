// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.IO;
using Guncon3.Core;
using Xunit;

namespace Guncon3.Core.Tests
{
    public class AtomicFileTests
    {
        [Fact]
        public void Write_ReplacesTheTargetAndLeavesNoTemporaryBehind()
        {
            using var file = new TempFile();
            File.WriteAllText(file.Path, "old");

            AtomicFile.Write(file.Path, w => w.Write("new"));

            Assert.Equal("new", File.ReadAllText(file.Path));
            Assert.False(File.Exists(file.Path + ".tmp"));
        }

        [Fact]
        public void Write_AFailureInTheCallbackLeavesTheOriginalAndNoTemporary()
        {
            using var file = new TempFile();
            File.WriteAllText(file.Path, "old");

            Assert.Throws<InvalidOperationException>(
                () => AtomicFile.Write(file.Path, _ => throw new InvalidOperationException("no")));

            Assert.Equal("old", File.ReadAllText(file.Path));
            Assert.False(File.Exists(file.Path + ".tmp"));
        }

        [Fact]
        public void WriteLines_WritesEveryLineAndLeavesNoTemporaryBehind()
        {
            using var file = new TempFile();

            AtomicFile.WriteLines(file.Path, new[] { "a", "b" });

            Assert.Equal(new[] { "a", "b" }, File.ReadAllLines(file.Path));
            Assert.False(File.Exists(file.Path + ".tmp"));
        }
    }
}
