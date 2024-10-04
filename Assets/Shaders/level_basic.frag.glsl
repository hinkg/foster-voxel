#version 330

in vec3 v_offset;
in vec3 v_position;
in vec2 v_tex;
in vec3 v_normal;
in float v_skyLight;
in float v_blockLight;
in float v_occlusion;
in vec2 v_block;
in vec2 v_climateBlock;
in vec2 v_climate;
in vec4 v_shadowPos0;
in vec4 v_shadowPos1;
in vec4 v_shadowPos2;

out vec4 frag_color;

uniform sampler2D u_terrainTexture;
uniform sampler2D u_skyTexture;
uniform vec3 u_sunDirection;
uniform vec3 u_moonDirection;
uniform float u_viewDistance;

uniform sampler2D u_climateColorTexture;
uniform sampler2D u_cloudTexture;
uniform vec2 u_cloudOffset;

uniform sampler2DShadow u_shadowTexture0;
uniform sampler2DShadow u_shadowTexture1;
uniform sampler2DShadow u_shadowTexture2;

#define DO_TEXTURE_AA
#define DO_CLOUD_SHADOWS
#define DO_SHADOWS

#define PI 3.1415926535

float hash13(vec3 p3)
{
	p3  = fract(p3 * .1031);
    p3 += dot(p3, p3.zyx + 31.32);
    return fract((p3.x + p3.y) * p3.z);
}

vec4 sampleTexture(sampler2D tex, vec2 uv) 
{
#ifdef DO_TEXTURE_AA 
    vec2 texture_size = textureSize(tex, 0).xy;

    vec2 box_size = clamp(fwidth(uv) * texture_size, 1e-5, 1);
    vec2 tx = uv * texture_size - 0.5 * box_size;
    vec2 tx_offset = smoothstep(vec2(1) - box_size, vec2(1), fract(tx));

    vec2 fuv = (floor(tx) + 0.5 + tx_offset);

    return texture(tex, fuv / texture_size);
#else
    return texture(tex, uv);
#endif
}

float sampleCloudShadow() 
{
    float zOffset = 256 - v_position.z;
    vec2 offset = vec2(
        zOffset * u_sunDirection.x * (1.0 / u_sunDirection.z), 
        zOffset * u_sunDirection.y * (1.0 / u_sunDirection.z)
    );

    float alpha = clamp(dot(u_sunDirection, vec3(0.0, 0.0, 1.0)) * 8 - 2.2, 0.0, 1.0);
    vec4 cloudColor = texture(u_cloudTexture, (v_offset.xy + u_cloudOffset + offset) / (textureSize(u_cloudTexture, 0) * 8)); 

    return 1.0 - cloudColor.r * alpha * 0.6;
}

float sampleShadow() 
{
    vec2 texelSize = 1.0 / textureSize(u_shadowTexture0, 0);
    
    // Select cascade (crappy method)

    vec3 proj0 = v_shadowPos0.xyz / v_shadowPos0.w * 0.5 + 0.5;
    vec3 proj1 = v_shadowPos1.xyz / v_shadowPos1.w * 0.5 + 0.5;
    vec3 proj2 = v_shadowPos2.xyz / v_shadowPos2.w * 0.5 + 0.5;
    vec3 proj = proj0;
    int t = 0;

    if (all(greaterThan(proj0.xy, vec2(0.0))) && all(lessThan(proj0.xy, vec2(1.0))) && proj0.z < 1.0 && proj0.z > -1.0) {
        proj = proj0;
        t = 0;
    } else if (all(greaterThan(proj1.xy, vec2(0.0))) && all(lessThan(proj1.xy, vec2(1.0))) && proj1.z < 1.0 && proj1.z > -1.0) {
        proj = proj1;
        t = 1;
    } else {
        proj = proj2;
        t = 2;
    }

    // Crappy smooth sampling, using 3x3 sample filter

    float shadow = 0.0;

    for(int y = -1; y <= 1; y++) 
    { 
        for(int x = -1; x <= 1; x++) 
        {
            shadow += texture(t == 0 ? u_shadowTexture0 : (t == 1 ? u_shadowTexture1 : u_shadowTexture2), vec3(proj.xy + vec2(x, y) * texelSize, proj.z));
        }
    }

    shadow /= 9;

    // Fade shadows when sun is close to horizon
    float shadowStrength = clamp(dot(u_sunDirection, vec3(0.0, 0.0, 1.0)) * 4, 0.0, 1.0);

    // Add shadows again when moon is up and sun is below horizon
    shadowStrength += clamp(dot(u_sunDirection, vec3(0.0, 0.0, -1.0)) * 16, 0.0, 1.0) * clamp(dot(u_moonDirection, vec3(0.0, 0.0, 1.0)) * 4, 0.0, 1.0) * clamp(dot(u_moonDirection, -u_sunDirection) * 2, 0.0, 1.0);

    return mix(1.0, shadow, shadowStrength);
}

vec2 makeTextureCoords() 
{
    return clamp(v_tex, vec2(0.0), vec2(1.0)) / 32 + v_block / 16 + vec2(1.0 / 64);
}

vec2 makeClimateTextureCoords() 
{
    return clamp(v_tex, vec2(0.0), vec2(1.0)) / 32 + v_climateBlock / 16 + vec2(1.0 / 64);
}

vec4 sampleClimateColor() 
{
    return texture(u_climateColorTexture, vec2(v_climate.x, v_climate.y));
}

float luminance(vec3 col)
{
    return dot(col, vec3(0.2125, 0.7154, 0.0721));
}

float getSkyLuminance(void) 
{
    vec3 skyColor = texture(u_skyTexture, vec2(0, 1)).rgb;
    return clamp(luminance(skyColor) * 2 + 0.2, 0.0, 1.0);
}

vec3 getHorizonColor(void) 
{
    vec3 viewNormal = normalize(v_offset);
    float uv_x = atan(-viewNormal.y, -viewNormal.x) / (PI*2);
    return texture(u_skyTexture, vec2(uv_x, 0.5)).rgb;
}

float getLight(void) 
{
    float light = 0.0;

    // Sky lighting

    float skyLight = getSkyLuminance();

    float sunStrength = clamp(dot(u_sunDirection, vec3(0.0, 0.0, 1.0)) * 4, 0.0, 1.0);
    float moonStrength = clamp(dot(u_moonDirection, vec3(0.0, 0.0, 1.0)) * 4, 0.0, 1.0);
	float eclipse = mix(1.0, smoothstep(1.0, 0.9905, dot(u_sunDirection, u_moonDirection)), 0.990); 

	float sunlight = (dot(v_normal, vec3(normalize(u_sunDirection.xy), 1.0)) + 1) / 2 * sunStrength;
	float moonlight = (dot(v_normal, vec3(normalize(u_moonDirection.xy), 1.0)) + 1) / 2 * moonStrength;

    light += (sunlight * 0.3 + (moonlight * 0.10 * (1.0 - sunlight))) * eclipse;

    float shadow = 1.0;

#ifdef DO_CLOUD_SHADOWS
    shadow = min(shadow, sampleCloudShadow());
#endif

#ifdef DO_SHADOWS
    shadow = min(shadow, sampleShadow());
#endif

    light *= shadow;

    light += skyLight * 0.7;
    light *= clamp(v_skyLight / 15.0, 0.0, 1.0);

    // Block lighting

    float blockLight = clamp(v_blockLight / 15.0, 0.0, 1.0);

    light += blockLight * 0.8;
    light = tanh(pow(light, 2.2));

    float occlusion = clamp(v_occlusion / 3.0, 0.0, 1.0) * 0.75 + 0.25;
    occlusion = min(occlusion, 1.0);
    light *= occlusion;

    return clamp(light, 0.0, 1.0);
}

vec3 getFog(out float fogValue) 
{
    vec3 horizonColor = getHorizonColor();
    fogValue = pow(clamp(length(v_offset.xyz) / (u_viewDistance) - 0.05, 0.0, 1.0), 1.5);
    return horizonColor;
}

//

void mainOpaque(void) 
{
    vec3 color = sampleTexture(u_terrainTexture, makeTextureCoords()).rgb; 
    
    // climate color
    vec4 climateColorMask = sampleTexture(u_terrainTexture, makeClimateTextureCoords());
    color = mix(color, sampleClimateColor().rgb * climateColorMask.rgb, climateColorMask.a); 

    float light = getLight();
    color *= light;

    // random variation
    color *= hash13(floor(v_position / 1 + 0.001)) * 0.05 + 0.975;
    color *= hash13(floor(v_position / 4 + 1.001)) * 0.05 + 0.975;

    float fogValue = 0.0;
    vec3 fogColor = getFog(fogValue);
    color = mix(color, fogColor, fogValue);

    frag_color = vec4(color, 1.0);
}

void mainLiquid(void) 
{
    vec3 color = sampleTexture(u_terrainTexture, makeTextureCoords()).rgb; 
    
	float light = getLight();
    color *= light;

    // random variation
    color *= hash13(floor(v_position / 1 + 0.001)) * 0.05 + 0.975;
    color *= hash13(floor(v_position / 4 + 1.001)) * 0.05 + 0.975;

    float fogValue = 0.0;
    vec3 fogColor = getFog(fogValue);
    color = mix(color, fogColor, fogValue);

    frag_color = vec4(color, 1.0) * 0.8;
}

void mainEntity(void) 
{
    vec3 color = sampleTexture(u_terrainTexture, v_tex).rgb; 

	float light = getLight();
    color *= light;

    float fogValue = 0.0;
    vec3 fogColor = getFog(fogValue);
    color = mix(color, fogColor, fogValue);

    frag_color = vec4(color, 1.0);
}
