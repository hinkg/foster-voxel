#version 330

// Copyright (c) 2024 Felix Westin
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

/////////////////////////////////////////////////////////////////////////

// Fast semi-physical atmosphere with planet view and aerial perspective.
//
// I have long since dreamed of (and tried making) a function that
// generates plausible atmospheric scattering and transmittance without
// expensive ray marching that also supports aerial perspectives and
// offers simple controls over perceived atmospheric density which do not
// affect the color of the output.
//
// This file represents my latest efforts in making such a function and
// this time I am happy enough with the result to release it.
//
// Big thanks to:
// Inigo Quilez (https://iquilezles.org) for this site and his great
// library of shader resources.
// Sébastien Hillaire (https://sebh.github.io) for his many papers on
// atmospheric and volumetric rendering.

/////////////////////////////////////////////////////////////////////////

#ifndef FAST_ATMOSPHERE_INCLUDED
#define FAST_ATMOSPHERE_INCLUDED

// Config
// #define DRAW_PLANET                // Draw planet ground sphere.
#define PREVENT_CAMERA_GROUND_CLIP // Force camera to stay above horizon. Useful for certain games.
#define LIGHT_COLOR_IS_RADIANCE    // Comment out if light color is not in radiometric units.
#define DENSITY                    1.0 // Atmosphere density. 1 is Earth-like.
#define AERIAL_SCALE               8.0 // Higher value = more aerial perspective. A value of 1 is tuned to match reference implementation.
#define NIGHT_LIGHT                0.0002 // Optional, cheap (free) non-physical night lighting. Can also use a 2nd light for moon instead.
#define EXPOSURE                   3.0 // Tuned to match reference.

// Math
#define INFINITY 3.402823466e38
#define PI 3.1415926535

// Atmosphere
#define ATMOSPHERE_HEIGHT 100000.0
#define PLANET_RADIUS     6371000.0
#define PLANET_CENTER     vec3(0, -PLANET_RADIUS, 0)
#define C_RAYLEIGH        (vec3(5.802, 13.558, 33.100) * 1e-6) * 1.0
#define C_MIE             (vec3(3.996, 3.996, 3.996) * 1e-6)
#define C_OZONE           (vec3(0.650, 1.881, 0.085) * 1e-6) * 3.1
#define RAYLEIGH_MAX_LUM  2.0
#define MIE_MAX_LUM       0.5

// Magic numbers
#define M_TRANSMITTANCE       0.75
#define M_LIGHT_TRANSMITTANCE 1e6
#define M_MIN_LIGHT_ELEVATION -0.4
#define M_DENSITY_HEIGHT_MOD  1e-12
#define M_DENSITY_CAM_MOD     10.0

float sq(float x) { return x*x; }
float pow4(float x) { return sq(x)*sq(x); }
float pow8(float x) { return pow4(x)*pow4(x); }

// https://iquilezles.org/articles/intersectors/
vec2 SphereIntersection(vec3 rayStart, vec3 rayDir, vec3 sphereCenter, float sphereRadius)
{
	vec3 oc = rayStart - sphereCenter;
    float b = dot(oc, rayDir);
    float c = dot(oc, oc) - sq(sphereRadius);
    float h = sq(b) - c;
    if (h < 0.0)
    {
        return vec2(-1.0, -1.0);
    }
    else
    {
        h = sqrt(h);
        return vec2(-b-h, -b+h);
    }
}
vec2 PlanetIntersection(vec3 rayStart, vec3 rayDir)
{
	return SphereIntersection(rayStart, rayDir, PLANET_CENTER, PLANET_RADIUS);
}
vec2 AtmosphereIntersection(vec3 rayStart, vec3 rayDir)
{
	return SphereIntersection(rayStart, rayDir, PLANET_CENTER, PLANET_RADIUS + ATMOSPHERE_HEIGHT);
}
float PhaseR(float costh)
{
	return (1.0+sq(costh))*0.06;
}
float PhaseM(float costh, float g)
{
	// g = min(g, 0.9381); // Assume validated input by user so we can skip
	float k = 1.55*g-0.55*sq(g)*g;
	float a = 1.0-sq(k);
	float b = 12.57*sq(1.0-k*costh);
	return a/b;
}

vec3 GetLightTransmittance(vec3 position, vec3 lightDir, float multiplier)
{
	float lightExtinctionAmount = pow8(smoothstep(1.0, M_MIN_LIGHT_ELEVATION, lightDir.y));
	return exp(-(C_RAYLEIGH + C_MIE + C_OZONE) * lightExtinctionAmount * DENSITY * multiplier * M_LIGHT_TRANSMITTANCE);
}
vec3 GetLightTransmittance(vec3 position, vec3 lightDir)
{
	return GetLightTransmittance(position, lightDir, 1.0);
}

void GetRayleighMie(float opticalDepth, float densityR, float densityM, out vec3 R, out vec3 M)
{
    // Approximate marched Rayleigh + Mie scattering with some exp magic.
    R = (1.0 - exp(-opticalDepth * densityR * C_RAYLEIGH / RAYLEIGH_MAX_LUM)) * RAYLEIGH_MAX_LUM;
	M = (1.0 - exp(-opticalDepth * densityM * C_MIE / MIE_MAX_LUM)) * MIE_MAX_LUM;
}

// Main function
// Transmittance stores atmospheric transmittance in xyz and planet intersection flag in w.
// Fog parameter can be used in cases where you want to fade the world out
// (like in a game with limited far clip plane).
vec3 GetAtmosphere(vec3 rayStart, vec3 rayDir, float rayLength, vec3 lightDir,
	vec3 lightColor, out vec4 transmittance, float fogFactor)
{
#ifdef PREVENT_CAMERA_GROUND_CLIP
	rayStart.y = max(rayStart.y, 1.0);
#endif

	// Planet and atmosphere intersection to get optical depth
	// TODO: Could simplify to circle intersection test if flat horizon is acceptable
	vec2 t1 = PlanetIntersection(rayStart, rayDir);
	vec2 t2 = AtmosphereIntersection(rayStart, rayDir);
    
    // Note: This only works if camera XZ is at 0. Otherwise, swap for line below.
    float altitude = rayStart.y;
    //float altitude = (length(rayStart - PLANET_CENTER) - PLANET_RADIUS);
    float normAltitude = rayStart.y / ATMOSPHERE_HEIGHT;

	if (t2.y < 0.0)
	{
		// Outside of atmosphere looking into space, return nothing
		transmittance = vec4(1, 1, 1, 0);
		return vec3(0, 0, 0);
	}
    else
    {
        // In case camera is outside of atmosphere, subtract distance to entry.
        t2.y -= max(0.0, t2.x);

#ifdef DRAW_PLANET
        float opticalDepth = t1.x > 0.0 ? min(t1.x, t2.y) : t2.y;
#else
        float opticalDepth = t2.y;
#endif

        // Optical depth modulators
        opticalDepth = min(rayLength, opticalDepth);
        opticalDepth = min(opticalDepth * AERIAL_SCALE, t2.y);

        // Altitude-based density modulators
        float h = 1.0-1.0/(2.0+sq(t2.y)*M_DENSITY_HEIGHT_MOD);
        h = pow(h, 1.0+normAltitude*M_DENSITY_CAM_MOD); // Really need a pow here, bleh
        float sqh = sq(h);
        float densityR = sqh * DENSITY;
        float densityM = sq(sqh)*h * DENSITY;

#ifdef NIGHT_LIGHT
        float nightLight = NIGHT_LIGHT;
#else
        float nightLight = 0.0;
#endif

        // Apply light transmittance (makes sky red as sun approaches horizon)
        lightColor *= GetLightTransmittance(rayStart, lightDir, h); // h bias makes twilight sky brighter

#ifndef LIGHT_COLOR_IS_RADIANCE
        // If used in an environment where light "color" is not defined in radiometric units
        // we need to multiply with PI to correct the output.
        lightColor *= PI;
#endif

        vec3 R, M;
        GetRayleighMie(opticalDepth, densityR, densityM, R, M);

        float costh = dot(rayDir, lightDir);
        float phaseR = PhaseR(costh);
        float phaseM = PhaseM(costh, 0.85);
        vec3 A = phaseR * lightColor + nightLight;
        vec3 B = phaseM * lightColor + nightLight;
        vec3 C = (C_RAYLEIGH * densityR + C_MIE * densityM + C_OZONE * densityR) * pow4(1.0 - normAltitude) * M_TRANSMITTANCE;
        
        // Combined scattering
        vec3 scattering = R * A + M * B;

        // View extinction, matched to reference
        transmittance.xyz = exp(-opticalDepth * C);
        // Store planet intersection flag in transmittance.w, useful for occluding celestial bodies etc.
        transmittance.w = step(t1.x, 0.0);

        if (fogFactor > 0.0)
        {
            // 2nd sample (all the way to atmosphere exit), used for fog fade.
            opticalDepth = t2.y;
            GetRayleighMie(opticalDepth, densityR, densityM, R, M);
            vec3 scattering2 = R * A + M * B;
            vec3 transmittance2 = exp(-opticalDepth * C);

            //scattering2 *= mix(vec3(1, 0, 0), vec3(1, 1, 1), sq(fogFactor)); // Fog color test
            scattering = mix(scattering, scattering2, fogFactor);
            transmittance.xyz = mix(transmittance.xyz, transmittance2, fogFactor);
        }

        return scattering * EXPOSURE;
    }
}

// Overloaded functions in case you are not interested in transmittance/fading
vec3 GetAtmosphere(vec3 rayStart, vec3 rayDir, float rayLength, vec3 lightDir, vec3 lightColor, out vec4 transmittance)
{
    return GetAtmosphere(rayStart, rayDir, rayLength, lightDir, lightColor, transmittance, 0.0);
}
vec3 GetAtmosphere(vec3 rayStart, vec3 rayDir, float rayLength, vec3 lightDir, vec3 lightColor)
{
    vec4 transmittance;
    return GetAtmosphere(rayStart, rayDir, rayLength, lightDir, lightColor, transmittance, 0.0);
}

#endif // FAST_ATMOSPHERE_INCLUDED

//
//
//

in vec2 v_tex;

out vec4 o_fragColor;

uniform vec3 u_sunDirection;
uniform vec3 u_moonDirection;

vec3 TonemapACES(vec3 x)
{
	const float A = 2.51f;
	const float B = 0.03f;
	const float C = 2.43f;
	const float D = 0.59f;
	const float E = 0.14f;
	return (x * (A * x + B)) / (x * (C * x + D) + E);
}

void main(void)
{
    vec3 normal = vec3(
        -cos(v_tex.x * PI*2) * abs(sin(v_tex.y * PI)),
        -sin(v_tex.x * PI*2) * abs(sin(v_tex.y * PI)),
         cos(v_tex.y * PI)
    );
    
    vec3 rayOrigin = vec3(0.0, 1900.0, 0.0);
    vec3 rayDir = normal.xzy;
    float rayLength = INFINITY;

	//

	vec3 sunDir = u_sunDirection.xzy;
    vec3 sunRadiance = vec3(1.0, 1.0, 1.0);

	vec3 moonDir = u_moonDirection.xzy;
    vec3 moonRadiance = vec3(0.01) * max(dot(sunDir, -moonDir) * 0.75 + 0.25, 0.0);

	// Eclipse
	sunRadiance *= mix(1.0, smoothstep(1.0, 0.9905, dot(sunDir, moonDir)), 0.990); 
    
    vec4 transmittance;
	float fogFactor = 0.0;
    float atmosphereOcclusion = 1.0;

	// Apply atmosphere (sun)
    vec3 color = GetAtmosphere(rayOrigin, rayDir, rayLength, sunDir, sunRadiance, transmittance, fogFactor) * atmosphereOcclusion;

	// Apply atmosphere (moon)
    color += GetAtmosphere(rayOrigin, rayDir, rayLength, moonDir, moonRadiance);

	color = pow(color, vec3(1.0 / 2.2));
	color = TonemapACES(color);

	o_fragColor = vec4(color.rgb, 1.0);
}