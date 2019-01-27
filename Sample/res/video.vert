#version 330 core

layout(location = 0) in vec2 vPosition;
layout(location = 1) in vec2 vTexCoord;

out vec2 texCoord;

void main(void) {
    texCoord = vTexCoord;
    gl_Position = vec4(vPosition, 1.0, 1.0);
}