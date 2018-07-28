# MotionTK
Video playback library for OpenTK built on FFmpeg, inspired by the [Motion library for SFML](https://github.com/zsbzsb/Motion).

## About
I was looking for a lightweight video playback library for OpenTK, but couldn't find anything that met my requirements.
From my SFML days I remembered the [Motion library](https://github.com/zsbzsb/Motion) by [Zachariah Brown](https://zbrown.net/), so I rewrote a simplified version of it in C#.

## Dependencies
To function properly, this library requires a working copy of OpenTK and some of the FFmpeg libraries.
Specifically, these libraries are required:

- OpenTK.dll
- avcodec-58.dll
- avformat-58.dll
- avutil-58.dll
- swresample-3.dll
- swscale-5.dll

OpenTK itself requires OpenGL and OpenAL to be installed.

## Usage
Videos are loaded by instantiating the `DisplaySource` class.
From there, you can access the `VideoPlayback` property, which exposes a `Draw` function to render the current frame as well as the `TextureHandle` itself.
The audio `SourceHandle` can be accessed via the `AudioPlayback` property.
The playback control functions (`Play`, `Pause` and the like) are available via the `DisplaySource` instance.
Don't forget to dispose all disposable instances you create!

For a working example check the "Sample" project included with the sources.

## Contributing
If you experience any problems, please file a github issue or contact me under atomcrafty@frucost.net.

You are of course also invited to fix it yourself and create a pull request :)
