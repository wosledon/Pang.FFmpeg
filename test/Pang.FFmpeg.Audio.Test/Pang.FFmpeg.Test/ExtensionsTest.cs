using System;
using Pang.FFmpeg.Core.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Pang.FFmpeg.Test
{
    public unsafe class ExtensionsTest
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public ExtensionsTest(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        [Fact]
        public void Test1()
        {
            var buffer = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };

            byte* res = buffer.ToArrayPointer();

            for (int i = 0; i < buffer.Length; i++)
            {
                _testOutputHelper.WriteLine(res[i].ToString());
            }
        }
    }
}