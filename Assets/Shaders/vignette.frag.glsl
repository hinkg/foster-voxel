#version 330

in vec3 v_position;
in vec2 v_tex;

out vec4 o_FragColor;

uniform float u_strength;

void main(void) 
{
    // vignette

    vec2 uv = v_tex * (1.0 - v_tex.yx);
    float vignette = uv.x * uv.y * 20.0;
    vignette = (1.0 - clamp(pow(vignette, 0.2), 0.0, 1.0)) * u_strength;

    o_FragColor = vec4(vec3(0.0), vignette);
}