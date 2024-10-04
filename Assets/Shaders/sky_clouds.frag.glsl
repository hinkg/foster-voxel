#version 330

in vec3 v_offset;
in vec3 v_position;
in vec2 v_tex;
in vec3 v_normal;

out vec4 o_FragColor;

uniform sampler2D u_skyTexture;
uniform vec3 u_sunDirection;
uniform vec3 u_moonDirection;

#define PI 3.1415926535

float luminance(vec3 col)
{
    return dot(col, vec3(0.2125, 0.7154, 0.0721));
}

void main(void) 
{
    float uv_y = acos(normalize(v_offset).z) / PI;
    float uv_x = atan(-normalize(v_offset).y, -normalize(v_offset).x) / (PI*2);
    vec4 sky = texture(u_skyTexture, vec2(uv_x, uv_y));

    float lumi = luminance(sky.rgb) + 0.2;

    float sunlight = max(dot(v_normal, normalize(vec3(u_sunDirection.x, u_sunDirection.y, 1.0))) * 0.5 + 0.5, 0.0) * clamp(dot(u_sunDirection, vec3(0.0, 0.0, 1.0)) * 4 + 0.6, 0.0, 1.0) * 0.8;
    
    // Darken sunlight during eclipse
    sunlight *= mix(1.0, smoothstep(1.0, 0.9905, dot(u_sunDirection, u_moonDirection)), 0.900); 
    
    lumi += sunlight;

    // Glow when sun/moon is behind cloud, visual effect is perhaps not worth the effort
    lumi *= 1 + pow(smoothstep(0.9, 1.0, dot(normalize(v_offset), u_sunDirection)), 3) * 0.2;
    lumi *= 1 + pow(smoothstep(0.9, 1.0, dot(normalize(v_offset), u_moonDirection)), 5) * 0.2;

    lumi = tanh(pow(lumi, 2.2));

    // Fade at horizon
    lumi *= clamp(1.0 - length(v_offset) / 1024, 0.0, 1.0);

    vec3 col = mix(sky.rgb, vec3(1.0), lumi);

    o_FragColor = vec4(col, 1.0) * 0.95;
}