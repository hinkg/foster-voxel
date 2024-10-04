#version 330

layout(location=0) in vec4 a_vertex;
layout(location=1) in vec4 a_vertex2;

out vec3 v_offset;
out vec3 v_position;
out vec2 v_tex;
out vec3 v_normal;
out float v_skyLight;
out float v_blockLight;
out float v_occlusion;
out vec2 v_block;
out vec2 v_climateBlock;
out vec2 v_climate;
out vec4 v_shadowPos0;
out vec4 v_shadowPos1;
out vec4 v_shadowPos2;

uniform vec3 u_offset;
uniform mat4 u_localViewMatrix;

uniform mat4 u_shadowMatrix0;
uniform mat4 u_shadowMatrix1;
uniform mat4 u_shadowMatrix2;

void main(void)
{
	vec3 position = vec3(
		(uint(a_vertex.x)) & uint(0xFF),
		(uint(a_vertex.x) >> 8) & uint(0xFF),
		uint(a_vertex.y)
	);

	uint block = (uint(a_vertex.z) >> 8) & uint(0xFF);

	vec2 uv = vec2(
		(uint(a_vertex.z) >> 7) & uint(0x1),
		(uint(a_vertex.z) >> 6) & uint(0x1)
	); 

	vec3 normal = vec3(
		int((uint(a_vertex.z) >> 4) & uint(0x3)) - 1,
		int((uint(a_vertex.z) >> 2) & uint(0x3)) - 1,
		int((uint(a_vertex.z) >> 0) & uint(0x3)) - 1
	); 

	uint skyLight = (uint(a_vertex.w) >> 4) & uint(0xF);
	uint blockLight = (uint(a_vertex.w)) & uint(0xF);
	uint occlusion = (uint(a_vertex.w) >> 8) & uint(0xF);

	vec2 climate = vec2(
		(uint(a_vertex2.x) >> 8) & uint(0xFF),
		(uint(a_vertex2.x)) & uint(0xFF)
	) / 255;

	uint climateBlock = (uint(a_vertex2.y)) & uint(0xFF);

	//

	v_offset = vec4(position.xyz - u_offset, 1.0).xyz;
	v_position = position.xyz;
	v_tex    = vec2(uv.x, 1.0 - uv.y);
	v_normal = normal;
	v_skyLight = float(skyLight);
	v_blockLight = float(blockLight);
	v_occlusion = float(occlusion);

	v_block = vec2(
		(uint(block) >> 4) & uint(0xF),
		(uint(block) >> 0) & uint(0xF)
	); 

	v_climateBlock = vec2(
		(uint(climateBlock) >> 4) & uint(0xF),
		(uint(climateBlock) >> 0) & uint(0xF)
	); 

	v_climate = climate;

	gl_Position = u_localViewMatrix * vec4(position.xyz - u_offset, 1.0);
	v_shadowPos0 = u_shadowMatrix0 * vec4(position.xyz - u_offset, 1.0);
	v_shadowPos1 = u_shadowMatrix1 * vec4(position.xyz - u_offset, 1.0);
	v_shadowPos2 = u_shadowMatrix2 * vec4(position.xyz - u_offset, 1.0);
}