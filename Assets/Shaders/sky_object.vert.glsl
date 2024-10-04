#version 330

layout(location=0) in vec3 a_position;
layout(location=1) in vec2 a_uv;
layout(location=2) in vec3 a_normal;
layout(location=3) in vec4 a_color;
layout(location=4) in vec4 a_extra;

out vec2 v_tex;
out vec3 v_normal;

uniform mat4 u_viewMatrix;
uniform mat4 u_modelMatrix;

void main(void) 
{
	gl_Position = u_viewMatrix * u_modelMatrix * vec4(a_position.xyz, 1.0);
	v_tex    = a_uv;
	v_normal = (u_modelMatrix * vec4(a_position, 1.0)).xyz;
}