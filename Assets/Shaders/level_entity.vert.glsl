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
out float v_skyLight;
out float v_blockLight;
out float v_occlusion;
out vec2 v_block;
out vec4 v_shadowPos0;
out vec4 v_shadowPos1;
out vec4 v_shadowPos2;

uniform mat4 u_localViewMatrix;
uniform mat4 u_modelMatrix;
uniform mat4 u_localModelMatrix;
uniform float u_zPosition;

uniform mat4 u_shadowMatrix0;
uniform mat4 u_shadowMatrix1;
uniform mat4 u_shadowMatrix2;

void main(void)
{
	vec3 position = a_position;
	vec2 uv = a_uv;
	vec3 normal = a_normal;

	v_offset = (u_modelMatrix * vec4(position.xyz, 1.0)).xyz;
	v_position = (u_localModelMatrix * vec4(position.xyz, 1.0)).xyz + vec3(0, 0, u_zPosition);
	v_tex = uv;
	v_normal = normalize((transpose(inverse(u_modelMatrix)) * vec4(normal, 1.0)).xyz);
	v_skyLight = float(15);
	v_blockLight = float(0);
	v_occlusion = float(3);
	v_block = vec2(1);

	gl_Position = u_localViewMatrix * u_modelMatrix * vec4(position.xyz, 1.0);
	v_shadowPos0 = u_shadowMatrix0 * (u_modelMatrix * vec4(position.xyz, 1.0));
	v_shadowPos1 = u_shadowMatrix1 * (u_modelMatrix * vec4(position.xyz, 1.0));
	v_shadowPos2 = u_shadowMatrix2 * (u_modelMatrix * vec4(position.xyz, 1.0));
}
