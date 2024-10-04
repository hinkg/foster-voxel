#version 330

layout(location=0) in vec3 a_position;
layout(location=1) in vec3 a_color;

out vec3 v_color;

uniform mat4 u_localViewMatrix;

void main(void)
{
    v_color = a_color;
	gl_Position = u_localViewMatrix * vec4(a_position, 1.0);
}
