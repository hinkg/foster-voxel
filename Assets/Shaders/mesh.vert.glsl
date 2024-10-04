#version 330

layout(location=0) in vec3 a_position;
layout(location=1) in vec2 a_uv;
layout(location=2) in vec3 a_normal;
layout(location=3) in vec4 a_color;
layout(location=4) in vec4 a_extra;

out vec3 v_offset;
out vec3 v_position;
out vec2 v_tex;
out vec3 v_normal;

uniform mat4 u_modelMatrix;
uniform mat4 u_localViewMatrix;

void main(void)
{
	v_offset = (u_modelMatrix * vec4(a_position.xyz, 1.0)).xyz;
	v_position = a_position.xyz;
	v_tex    = a_uv;
	v_normal = normalize((transpose(inverse(u_modelMatrix)) * vec4(a_normal, 1.0)).xyz);

	gl_Position = u_localViewMatrix * u_modelMatrix * vec4(a_position.xyz, 1.0);
}
