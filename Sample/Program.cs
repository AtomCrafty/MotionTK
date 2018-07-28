using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using MotionTK;
using OpenTK;
using OpenTK.Audio.OpenAL;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace Sample {
	internal static class Program {

		internal static DataSource Source;
		internal static VideoPlayback Video;
		internal static AudioPlayback Audio;

		internal static GameWindow Window;

		internal static IntPtr AudioDevice;
		internal static ContextHandle AudioContext;
		internal static GraphicsContext GraphicsContext;

		internal static void Main(string[] args) {
			if(args.Length != 1) {
				Console.WriteLine("Usage: " + Assembly.GetExecutingAssembly().GetName().Name + " <video file>");
				Console.ReadLine();
				return;
			}

			Setup();
			LoadVideo(args[0]);
			Run();
			UnloadVideo();
			Cleanup();
		}

		internal static void LoadVideo(string path) {
			Source = new DataSource(path);
			Video = Source.VideoPlayback;
			Audio = Source.AudioPlayback;
		}

		internal static void UnloadVideo() {
			Source.Dispose();
		}

		internal static void Setup() {
			Thread.CurrentThread.Name = "Main Thread";

			using(var mre = new ManualResetEvent(false)) {
				new Thread(() => {
					Window = new GameWindow {
						// uncomment this line to disable the frame rate limitation
						//VSync = VSyncMode.Off,
						WindowBorder = WindowBorder.Fixed,
						ClientSize = new Size(1280, 720),
						Title = "MotionTK Playback Demo"
					};

					// ReSharper disable once AccessToDisposedClosure
					mre.Set();

					// Event loop
					while(Window.Exists) {
						Window.ProcessEvents();
						Thread.Sleep(5);
					}

				}) { IsBackground = true, Name = "Event Thread" }.Start();

				// Wait until event thread is set up
				mre.WaitOne();
			}

			// Initialize OpenAL
			AudioDevice = Alc.OpenDevice(null);
			AudioContext = Alc.CreateContext(AudioDevice, new int[0]);
			Alc.MakeContextCurrent(AudioContext);

			// The active graphics context must be available on the rendering thread
			GraphicsContext = new GraphicsContext(GraphicsMode.Default, Window.WindowInfo);
			GraphicsContext.MakeCurrent(Window.WindowInfo);
		}

		internal static void Run() {
			if(Video == null) return;

			Window.ClientSize = Video.Size;
			Window.Visible = true;
			Source.Play();

			GL.MatrixMode(MatrixMode.Projection);
			GL.LoadIdentity();
			GL.Ortho(0, 1, 1, 0, 0, 1);
			GL.Viewport(Video.Size);
			GraphicsContext.Update(Window.WindowInfo);

			var frameTimes = new DateTime[100];
			int frameTimeIndex = 0;

			// Render loop
			while(Window.Exists && Source.State == PlayState.Playing) {
				GL.Clear(ClearBufferMask.ColorBufferBit);

				Source.Update();
				Video.Draw();

				/*/ calculate fps
				var t1 = frameTimes[frameTimeIndex] = DateTime.Now;
				frameTimeIndex = (frameTimeIndex + 1) % 100;
				var t2 = frameTimes[frameTimeIndex];
				Console.WriteLine(t2 == default(DateTime) ? "????? FPS" : $"{100 / (t1 - t2).TotalSeconds:F2} FPS");
				//*/

				DrawProgressBar();
				Window.SwapBuffers();
				Thread.Sleep(10);
			}
		}

		internal static void DrawProgressBar() {
			double progress = Source.PlayingOffset.TotalMilliseconds / Source.FileLength.TotalMilliseconds;

			GL.Enable(EnableCap.Blend);
			GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
			GL.Color4(Color.FromArgb(100, Color.Red));
			GL.Begin(PrimitiveType.Quads);
			{
				GL.Vertex2(0, 1);
				GL.Vertex2(0, 1 - 30d / Window.ClientSize.Height);
				GL.Vertex2(progress, 1 - 30d / Window.ClientSize.Height);
				GL.Vertex2(progress, 1);
			}
			GL.End();
			GL.Color3(Color.White);
		}

		internal static void Cleanup() {
			Window.Close();
			GraphicsContext.Dispose();
			Alc.MakeContextCurrent(ContextHandle.Zero);
			Alc.DestroyContext(AudioContext);
			Alc.CloseDevice(AudioDevice);
		}
	}
}
