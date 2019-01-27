using System;

namespace Sample {
	internal abstract class GlBase : IDisposable {
		public int Handle { get; protected set; }
		public abstract void Dispose();
	}
}