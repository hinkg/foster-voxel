#version 330

layout(location=0) in vec3 a_position;
layout(location=1) in vec2 a_uv;
layout(location=2) in vec3 a_normal;
layout(location=3) in vec4 a_color;
layout(location=4) in vec4 a_extra;

uniform mat4 u_transform;

out vec3 v_position;
out vec2 v_tex;

void main(void) 
{
	gl_Position = u_transform * vec4(a_position.xyz, 1.0);
	v_position = a_position.xyz;
	v_tex = vec2(a_uv.x, 1.0 - a_uv.y);
}