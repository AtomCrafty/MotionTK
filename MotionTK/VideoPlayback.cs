using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace MotionTK {
	public class VideoPlayback : IDisposable {
		private int _textureHandle;

		public readonly DataSource DataSource;
		internal readonly BlockingCollection<VideoPacket> PacketQueue = new BlockingCollection<VideoPacket>();
		internal readonly object Lock = new object();

		private DateTime _startTime;
		private TimeSpan _totalPlayTime;
		private TimeSpan _elapsedTime;
		private TimeSpan _frameTime;
		private int _frameJump;
		public uint PlayedFrameCount { get; private set; }
		public Size Size => DataSource.VideoSize;

		public VideoPlayback(DataSource dataSource) {
			DataSource = dataSource;
			lock(DataSource.PlaybackLock) DataSource.VideoPlaybacks.Add(this);
			SourceReloaded();
		}

		public void Destroy() {
			if(DataSource == null) return;
			lock(DataSource.PlaybackLock) {
				DataSource.VideoPlaybacks.Remove(this);
			}
		}

		internal void ResetBuffer() {
			if(DataSource == null) return;
			if(_textureHandle != 0) GL.DeleteTexture(_textureHandle);
			_textureHandle = 0;

			GL.GenTextures(1, out _textureHandle);
			GL.BindTexture(TextureTarget.Texture2D, _textureHandle);
			GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, DataSource.VideoSize.Width, DataSource.VideoSize.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
			GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
		}

		public void Draw() {
			// Draw the Color Texture
			GL.BindTexture(TextureTarget.Texture2D, _textureHandle);
			GL.Begin(PrimitiveType.Quads);
			{
				GL.TexCoord2(0.0f, 0.0f); GL.Vertex2(-1.0f, 1.0f);
				GL.TexCoord2(0.0f, 1.0f); GL.Vertex2(-1.0f, -1.0f);
				GL.TexCoord2(1.0f, 1.0f); GL.Vertex2(1.0f, -1.0f);
				GL.TexCoord2(1.0f, 0.0f); GL.Vertex2(1.0f, 1.0f);
			}
			GL.End();
		}

		internal void SourceReloaded() {
			if(!DataSource.HasVideo) return;

			_frameTime = DataSource.VideoFrameDuration;
			ResetBuffer();
		}

		internal void StateChanged(PlayState oldState, PlayState newState) {
			if(newState == PlayState.Playing && oldState == PlayState.Stopped) {
				_frameJump = 1;
				PlayedFrameCount = 0;
			}
			else if(newState == PlayState.Stopped) {
				_frameJump = 0;
				PlayedFrameCount = 0;
				if(DataSource.HasVideo) ResetBuffer();
				lock(Lock) {
					while(PacketQueue.Any()) {
						PacketQueue.Take();
					}
				}
			}
		}

		internal void Update(TimeSpan deltaTime) {
			if(_startTime == default(DateTime)) _startTime = DateTime.Now;
			if(DataSource == null || !DataSource.HasVideo) return;

			if(DataSource.State == PlayState.Playing) {

				_totalPlayTime += deltaTime;
				_elapsedTime += deltaTime;
				double jumps = Math.Floor(_elapsedTime.TotalMilliseconds / _frameTime.TotalMilliseconds);
				if(double.IsNaN(jumps)) jumps = 0;
				_elapsedTime -= TimeSpan.FromTicks((long)(_frameTime.TotalMilliseconds * jumps * TimeSpan.TicksPerMillisecond));
				_frameJump += (int)jumps;
			}

			lock(Lock) {
				while(PacketQueue.Any() && _frameJump > 1) {
					_frameJump--;
					PlayedFrameCount++;
					Console.WriteLine("Skipped frame " + PlayedFrameCount);
					PacketQueue.Take().Dispose();
				}

				if(_frameJump == 0 || !PacketQueue.TryTake(out var packet)) return;

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

				_frameJump--;
				PlayedFrameCount++;

				// update texture
				GL.BindTexture(TextureTarget.Texture2D, _textureHandle);
				GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, DataSource.VideoSize.Width, DataSource.VideoSize.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, packet.RgbaBuffer);

				packet.Dispose();
			}
		}

		public void Dispose() {
			DataSource?.Dispose();
			while(PacketQueue.TryTake(out var packet)) packet.Dispose();
			PacketQueue.Dispose();
		}
	}
}
