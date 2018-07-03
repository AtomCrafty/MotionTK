using System;
using static FFmpeg.AutoGen.ffmpeg;

namespace MotionTK {
	public unsafe class VideoPacket : IDisposable {

		public IntPtr RgbaBuffer { get; private set; }
		public TimeSpan Timestamp { get; private set; }

		public VideoPacket(byte* rgbaBuffer, int width, int height, TimeSpan timestamp) {
			ulong size = (ulong)width * (ulong)height * 4;
			RgbaBuffer = new IntPtr(av_malloc(size));
			Timestamp = timestamp;

			// copy buffer
			var src = (long*)rgbaBuffer;
			var dest = (long*)RgbaBuffer.ToPointer();
			for(uint i = 0; i < size / sizeof(long); i++) {
				dest[i] = src[i];
			}
		}

		~VideoPacket() {
			Dispose();
		}

		public void Dispose() {
			if(RgbaBuffer == IntPtr.Zero) return;
			av_free(RgbaBuffer.ToPointer());
			RgbaBuffer = IntPtr.Zero;
			Timestamp = TimeSpan.Zero;
		}
	}
}
