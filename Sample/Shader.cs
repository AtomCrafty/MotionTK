using OpenTK.Graphics.OpenGL4;

namespace Sample; 

internal class Shader : GlBase {
	public Shader(string vertSource, string fragSource) {
		Handle = GL.CreateProgram();

		int vert = CreateShader(ShaderType.VertexShader, vertSource);
		int frag = CreateShader(ShaderType.FragmentShader, fragSource);

		GL.AttachShader(Handle, vert);
		GL.AttachShader(Handle, frag);

		GL.LinkProgram(Handle);

		GL.DetachShader(Handle, vert);
		GL.DetachShader(Handle, frag);
		GL.DeleteShader(vert);
		GL.DeleteShader(frag);

		int CreateShader(ShaderType type, string source) {
			int shader = GL.CreateShader(type);
			GL.ShaderSource(shader, source);
			GL.CompileShader(shader);
			string infoLogFrag = GL.GetShaderInfoLog(shader);
			if(infoLogFrag != string.Empty)
				Console.WriteLine(infoLogFrag);
			return shader;
		}
	}

	public int GetAttribLocation(string name) => GL.GetAttribLocation(Handle, name);

	public void Use() => GL.UseProgram(Handle);

	public override void Dispose() {
		if(Handle == 0) return;
		GL.DeleteProgram(Handle);
		Handle = 0;
	}
}