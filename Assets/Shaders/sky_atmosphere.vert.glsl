#version 330

layout(location = 0) in vec3 a_position;
layout(location = 1) in vec2 a_UV;
layout(location = 2) in vec3 a_normal;
layout(location = 3) in vec4 a_color;
layout(location = 4) in vec4 a_extra;

out vec2 v_tex;

void main(void) 
{
	gl_Position = vec4(a_position.x, a_position.y, 0.0, 1.0);
	v_tex = a_UV;
}