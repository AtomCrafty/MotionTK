using System;
using System.Drawing;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace MotionTK {
	public class VideoPlayback : Playback<VideoPacket> {
		public int TextureHandle { get; protected set; } = -1;

		protected TimeSpan _totalPlayTime;
		protected TimeSpan _elapsedTime;
		protected TimeSpan _frameTime;
		protected int _skipFrames;
		public uint PlayedFrameCount { get; private set; }
		public Size Size => DataSource.VideoSize;

		internal VideoPlayback(DataSource dataSource) : base(dataSource) { }

		internal void ResetBuffer() {
			if(DataSource == null) return;
			if(TextureHandle != 0) GL.DeleteTexture(TextureHandle);
			TextureHandle = 0;

			TextureHandle = GL.GenTexture();
			GL.BindTexture(TextureTarget.Texture2D, TextureHandle);
			GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, DataSource.VideoSize.Width, DataSource.VideoSize.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
		}

		public void Draw() {
			// Draw the Color Texture
			GL.Enable(EnableCap.Texture2D);
			GL.BindTexture(TextureTarget.Texture2D, TextureHandle);
			GL.Begin(PrimitiveType.Quads);
			{
				GL.TexCoord2(0.0f, 0.0f); GL.Vertex2(-1.0f, 1.0f);
				GL.TexCoord2(0.0f, 1.0f); GL.Vertex2(-1.0f, -1.0f);
				GL.TexCoord2(1.0f, 1.0f); GL.Vertex2(1.0f, -1.0f);
				GL.TexCoord2(1.0f, 0.0f); GL.Vertex2(1.0f, 1.0f);
			}
			GL.End();
			GL.Disable(EnableCap.Texture2D);
		}

		internal override void SourceReloaded() {
			if(!DataSource.HasVideo) return;

			_frameTime = DataSource.FrameDuration;
			ResetBuffer();
		}

		internal override void StateChanged(PlayState oldState, PlayState newState) {
			switch(newState) {

				case PlayState.Playing when oldState == PlayState.Stopped:
					_skipFrames = 1;
					PlayedFrameCount = 0;
					break;

				case PlayState.Stopped:
					_skipFrames = 0;
					PlayedFrameCount = 0;
					if(DataSource.HasVideo) ResetBuffer();
					while(PacketQueue.Any()) {
						PacketQueue.Take();
					}
					break;
			}
		}

		internal void Update(TimeSpan deltaTime) {
			if(DataSource == null || !DataSource.HasVideo) return;

			if(DataSource.State == PlayState.Playing) {

				_totalPlayTime += deltaTime;
				_elapsedTime += deltaTime;

				int jumps = (int)(_elapsedTime.Ticks / _frameTime.Ticks);
				_elapsedTime -= TimeSpan.FromTicks(_frameTime.Ticks * jumps);

				_skipFrames += jumps;
			}

			while(_skipFrames > 1 && PacketQueue.TryTake(out var ignoredPacket)) {
				_skipFrames--;
				PlayedFrameCount++;
				ignoredPacket.Dispose();
				//Console.WriteLine("Skipped frame " + PlayedFrameCount);
			}

			if(_skipFrames < 1 || !PacketQueue.TryTake(out var packet)) return;

			#region Debug
			var videoTime = TimeSpan.FromTicks(_frameTime.Ticks * PlayedFrameCount);
			Console.ForegroundColor = ConsoleColor.Cyan;
			Console.Write($"Frame {PlayedFrameCount + 1} ({_totalPlayTime} ~ {videoTime}) ");
			Console.ForegroundColor = ConsoleColor.Red;
			Console.Write(_totalPlayTime - videoTime);
			Console.ForegroundColor = ConsoleColor.Green;
			Console.Write($" [{_frameTime}]    \r");
			Console.ResetColor();
			#endregion

			_skipFrames--;
			PlayedFrameCount++;

			// update texture
			GL.BindTexture(TextureTarget.Texture2D, TextureHandle);
			GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, DataSource.VideoSize.Width, DataSource.VideoSize.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, packet.RgbaBuffer);

			packet.Dispose();
		}

		~VideoPlayback() => Dispose();

		public override void Dispose() {
			GC.SuppressFinalize(this);
			if(TextureHandle == -1) return;
			GL.DeleteTexture(TextureHandle);
			TextureHandle = -1;
			base.Dispose();
		}
	}
}
