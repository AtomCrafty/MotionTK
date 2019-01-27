#version 330 core

layout(location = 0) in vec2 vPosition;

out vec2 texCoord;

void main(void) {
    gl_Position = vec4(vPosition, 1.0, 1.0);
}