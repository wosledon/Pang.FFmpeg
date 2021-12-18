using FFmpeg.AutoGen;

namespace Pang.FFmpeg.Core.Audio
{
    public sealed unsafe class AudioConvert
    {
        private SwrContext* SwrContext;

        private long OutCHLayout { get; }
        private AVSampleFormat outSampleFormat { get; }

        public AudioConvert()
        {
            SwrContext = ffmpeg.swr_alloc();
        }
    }
}