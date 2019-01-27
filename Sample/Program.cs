using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using MotionTK;
using OpenTK;
using OpenTK.Audio.OpenAL;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Input;

namespace Sample {
	internal static class Program {

		internal static DataSource Source;
		internal static VideoPlayback Video;
		internal static AudioPlayback Audio;

		internal static GameWindow Window;

		internal static IntPtr AudioDevice;
		internal static ContextHandle AudioContext;
		internal static GraphicsContext GraphicsContext;

		internal static Size PreviousSize;

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
						// comment out this line to disable the frame rate limitation
						//VSync = VSyncMode.Off,
						ClientSize = new Size(1280, 720),
						Title = "MotionTK Playback Demo"
					};

					Window.KeyDown += (s, args) => {
						if(args.Key == Key.Enter) {
							Window.WindowState = Window.WindowState == WindowState.Fullscreen ? WindowState.Normal : WindowState.Fullscreen;
						}
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
			GlSetup();
		}

		internal static void Cleanup() {
			GlCleanup();
			Window.Close();
			GraphicsContext.Dispose();
			Alc.MakeContextCurrent(ContextHandle.Zero);
			Alc.DestroyContext(AudioContext);
			Alc.CloseDevice(AudioDevice);
		}

		internal static void Run() {
			if(Video == null) return;

			Window.ClientSize = PreviousSize = Video.Size;
			Window.Visible = true;
			Window.VSync = VSyncMode.Off;
			Source.Play();

			ResetViewport();

			var frameTimes = new DateTime[100];
			int frameTimeIndex = 0;

			// Render loop
			while(Window.Exists && Source.State == PlayState.Playing) {
				if(Window.ClientSize != PreviousSize) ResetViewport();

				GL.Clear(ClearBufferMask.ColorBufferBit);

				Source.Update();
				DrawVideo();

				/*/ calculate fps
				var t1 = frameTimes[frameTimeIndex] = DateTime.Now;
				frameTimeIndex = (frameTimeIndex + 1) % 100;
				var t2 = frameTimes[frameTimeIndex];
				Console.WriteLine(t2 == default(DateTime) ? "????? FPS\r" : $"{100 / (t1 - t2).TotalSeconds:F2} FPS\r");
				//*/

				DrawProgressBar();
				Window.SwapBuffers();
				//Thread.Sleep(10);
			}
		}

		#region Drawing

		internal static readonly float[] VideoVertices = {
			-1, -1, 0, 1,
			-1, +1, 0, 0,
			+1, +1, 1, 0,
			+1, -1, 1, 1,
		};

		internal static readonly uint[] VideoIndices = {
			0, 1, 2,
			2, 3, 0
		};

		internal static int VideoVertexArray;
		internal static int VideoVertexBuffer;
		internal static int VideoElementBuffer;
		internal static Shader VideoShader;

		internal static readonly uint[] ProgressIndices = {
			0, 1, 2,
			2, 3, 0
		};

		internal static int ProgressVertexArray;
		internal static int ProgressVertexBuffer;
		internal static int ProgressElementBuffer;
		internal static Shader ProgressShader;

		private static void GlSetup() {
			// setup video data
			using(var vertReader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Sample.res.video.vert") ?? throw new FileLoadException("Unable to load shader source", "res/video.vert")))
			using(var fragReader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Sample.res.video.frag") ?? throw new FileLoadException("Unable to load shader source", "res/video.frag")))
				VideoShader = new Shader(vertReader.ReadToEnd(), fragReader.ReadToEnd());

			VideoVertexBuffer = GL.GenBuffer();
			GL.BindBuffer(BufferTarget.ArrayBuffer, VideoVertexBuffer);
			GL.BufferData(BufferTarget.ArrayBuffer, VideoVertices.Length * sizeof(float), VideoVertices, BufferUsageHint.StaticDraw);

			VideoElementBuffer = GL.GenBuffer();
			GL.BindBuffer(BufferTarget.ElementArrayBuffer, VideoElementBuffer);
			GL.BufferData(BufferTarget.ElementArrayBuffer, VideoIndices.Length * sizeof(int), VideoIndices, BufferUsageHint.StaticDraw);

			VideoVertexArray = GL.GenVertexArray();
			GL.BindVertexArray(VideoVertexArray);
			GL.BindBuffer(BufferTarget.ArrayBuffer, VideoVertexBuffer);
			GL.BindBuffer(BufferTarget.ElementArrayBuffer, VideoElementBuffer);

			int vPosition = VideoShader.GetAttribLocation("vPosition");
			int vTexCoord = VideoShader.GetAttribLocation("vTexCoord");
			GL.VertexAttribPointer(vPosition, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
			GL.VertexAttribPointer(vTexCoord, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
			GL.EnableVertexAttribArray(vPosition);
			GL.EnableVertexAttribArray(vTexCoord);

			GL.BindVertexArray(0);

			// setup progress data
			using(var vertReader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Sample.res.progress.vert") ?? throw new FileLoadException("Unable to load shader source", "res/progress.vert")))
			using(var fragReader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Sample.res.progress.frag") ?? throw new FileLoadException("Unable to load shader source", "res/progress.frag")))
				ProgressShader = new Shader(vertReader.ReadToEnd(), fragReader.ReadToEnd());

			ProgressVertexBuffer = GL.GenBuffer();

			ProgressElementBuffer = GL.GenBuffer();
			GL.BindBuffer(BufferTarget.ElementArrayBuffer, ProgressElementBuffer);
			GL.BufferData(BufferTarget.ElementArrayBuffer, ProgressIndices.Length * sizeof(int), ProgressIndices, BufferUsageHint.StaticDraw);

			ProgressVertexArray = GL.GenVertexArray();
			GL.BindVertexArray(ProgressVertexArray);
			GL.BindBuffer(BufferTarget.ArrayBuffer, ProgressVertexBuffer);
			GL.BindBuffer(BufferTarget.ElementArrayBuffer, ProgressElementBuffer);

			vPosition = ProgressShader.GetAttribLocation("vPosition");
			GL.VertexAttribPointer(vPosition, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
			GL.EnableVertexAttribArray(vPosition);

			GL.BindVertexArray(0);
		}

		private static void GlCleanup() {
			GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
			GL.BindVertexArray(0);
			GL.UseProgram(0);

			GL.DeleteBuffer(VideoElementBuffer);
			GL.DeleteBuffer(VideoVertexBuffer);
			GL.DeleteVertexArray(VideoVertexArray);
			VideoShader.Dispose();
		}

		internal static void DrawVideo() {
			VideoShader.Use();
			GL.BindVertexArray(VideoVertexArray);
			GL.ActiveTexture(TextureUnit.Texture0);
			GL.BindTexture(TextureTarget.Texture2D, Video.TextureHandle);
			GL.DrawElements(BeginMode.Triangles, VideoIndices.Length, DrawElementsType.UnsignedInt, 0);
		}

		internal static void DrawProgressBar() {
			float progress = (float)(Source.PlayingOffset.TotalMilliseconds / Source.FileLength.TotalMilliseconds);

			GL.Enable(EnableCap.Blend);
			GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

			var progressVertices = new[] {
				-1,         -1,
				progress-1, -1,
				progress-1, -0.9f,
				-1,         -0.9f
			};

			GL.BindBuffer(BufferTarget.ArrayBuffer, ProgressVertexBuffer);
			GL.BufferData(BufferTarget.ArrayBuffer, progressVertices.Length * sizeof(float), progressVertices, BufferUsageHint.StaticDraw);

			ProgressShader.Use();
			GL.BindVertexArray(ProgressVertexArray);
			GL.DrawElements(BeginMode.Triangles, ProgressIndices.Length, DrawElementsType.UnsignedInt, 0);
		}

		private static void ResetViewport() {
			var wsize = Window.ClientSize;
			var vsize = Video.Size;

			double scale = Math.Min((double)wsize.Width / vsize.Width, (double)wsize.Height / vsize.Height);

			var size = new Size((int)(vsize.Width * scale), (int)(vsize.Height * scale));
			var point = new Point((wsize.Width - size.Width) / 2, (wsize.Height - size.Height) / 2);

			GL.Viewport(point, size);
			GraphicsContext.Update(Window.WindowInfo);
		}

		#endregion
	}
}
