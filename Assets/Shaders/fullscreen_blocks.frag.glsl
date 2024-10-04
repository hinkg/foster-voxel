#version 330

in vec3 v_position;
in vec2 v_tex;

out vec4 o_FragColor;

uniform sampler2D u_texture;
uniform vec4 u_uv;

void main(void) 
{
    vec2 uv = vec2(
        u_uv.x + fract(gl_FragCoord.x / u_uv.w) / u_uv.z,
        u_uv.y + (1.0 - fract(gl_FragCoord.y / u_uv.w)) / u_uv.z
    );

    o_FragColor = vec4(texture(u_texture, uv).rgb, 1.0);
}