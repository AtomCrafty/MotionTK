using System;
using System.Drawing;
using System.Threading;
using MotionTK;
using OpenTK;
using OpenTK.Audio.OpenAL;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace Sample {
	internal static class Program {

		private static DataSource Source;
		private static VideoPlayback Video;
		private static AudioPlayback Audio;

		private static GameWindow Window;

		private static IntPtr AudioDevice;
		private static ContextHandle AudioContext;

		private static void Main(string[] args) {
			Setup();
			LoadVideo(args.Length > 0 ? args[0] : "video.mp4");
			Run();
			UnloadVideo();
			Cleanup();
		}

		private static void LoadVideo(string path) {
			Source = new DataSource(path);
			Video = new VideoPlayback(Source);
			Audio = new AudioPlayback(Source);
		}

		private static void UnloadVideo() {
			Source.Dispose();
		}

		private static void Setup() {
			Thread.CurrentThread.Name = "Main Thread";

			using(var mre = new ManualResetEvent(false)) {
				new Thread(() => {
					Window = new GameWindow {
						ClientSize = new Size(1280, 720),
						Title = "MotionTK Playback Demo"
					};

					// ReSharper disable once AccessToDisposedClosure
					mre.Set();

					// Event loop
					while(Window.Exists) Window.ProcessEvents();

				}) { IsBackground = true, Name = "Event Thread" }.Start();

				// Wait until event thread is set up
				mre.WaitOne();
			}

			// Initialize OpenAL
			AudioDevice = Alc.OpenDevice(null);
			AudioContext = Alc.CreateContext(AudioDevice, new int[0]);
			Alc.MakeContextCurrent(AudioContext);

			// The active graphics context umst be on the rendering thread
			new GraphicsContext(GraphicsMode.Default, Window.WindowInfo).MakeCurrent(Window.WindowInfo);
			GL.Enable(EnableCap.Texture2D);
		}

		private static void Run() {

			Window.ClientSize = Video.Size;
			Window.Visible = true;
			Source.Play();

			//GL.MatrixMode(MatrixMode.Modelview);
			//GL.LoadIdentity();

			// Render loop
			while(Window.Exists) {
				GL.Clear(ClearBufferMask.ColorBufferBit);

				Source.Update();
				Video.Draw();

				Window.SwapBuffers();
				Thread.Sleep(10);
			}
		}

		private static void Cleanup() {
			Window.Close();
			Alc.MakeContextCurrent(ContextHandle.Zero);
			Alc.DestroyContext(AudioContext);
			Alc.CloseDevice(AudioDevice);
		}
	}
}
