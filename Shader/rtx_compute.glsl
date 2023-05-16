#[compute]
#version 450

// #include "res://Shader/rtx_utilities.gdshaderinc"

const uint MAX_BOUNCES = 30u;
const uint NUM_RAYS = 100u;
const float FAR = 4000.0f;
const float RAY_POS_NORMAL_NUDGE = 0.01f;
const float TWO_PI = 6.28318530718f;
// const float RECENT_FRAME_BIAS = 0.1f;

struct Ray {
    vec3 origin;
    vec3 dir;
    vec3 energy;
};

struct HitInfo {
	bool didHit;
	bool frontFace;
	float dist;
	vec3 point;
	vec3 normal;
	vec3 albedo;
	vec3 emissive;
};

struct Material {
	vec3 albedo;
	vec3 emissive;
};

vec3 GetSkyGradient(Ray ray) {
	float t = 0.5f * (ray.dir.y + 1.0f);
	return (1.0f - t) * vec3(1.0) + t * vec3(0.15f, 0.15f, 0.45f);
}

Ray CreateRay(vec3 origin, vec3 dir) {
    Ray ray;
    ray.origin = origin;
    ray.dir = dir;
    ray.energy = vec3(1.0f);

    return ray;
}

bool RaySphere(in Ray ray, in vec3 sphereCentre, in float sphereRadius, inout HitInfo hitInfo)
{
	vec3 offsetRayOrigin = ray.origin - sphereCentre;
	// From the equation: sqrLength(rayOrigin + rayDir * dst) = radius^2
	// Solving for dst results in a quadratic equation with coefficients:
	float a = dot(ray.dir, ray.dir); // a = 1 (assuming unit vector)
	float bHalf = dot(offsetRayOrigin, ray.dir);
	float c = dot(offsetRayOrigin, offsetRayOrigin) - sphereRadius * sphereRadius;
	// Quadratic discriminant
	float discriminant = bHalf * bHalf - a * c;

	// No solution when d < 0 (ray misses sphere)
	if (discriminant >= 0.0f) {
		// Distance to nearest intersection point (from quadratic formula)
		float dist = (-bHalf - sqrt(discriminant)) / a;

		// Ignore intersections that occur behind the ray
		if (dist >= 0.0f && dist < hitInfo.dist) {
			hitInfo.didHit = true;
			hitInfo.dist = dist;
			hitInfo.point = ray.origin + ray.dir * dist;
			hitInfo.normal = normalize(hitInfo.point - sphereCentre);
            return true;
		}

		/*hitInfo.frontFace = dot(ray.dir, hitInfo.normal) <= 0.0f;
        if(hitInfo.frontFace) hitInfo.normal *= -1.0f;*/
	}

    return false;
}

uint WangHash(inout uint seed)
{
    seed = uint(seed ^ uint(61)) ^ uint(seed >> uint(16));
    seed *= uint(9);
    seed = seed ^ (seed >> 4u);
    seed *= uint(0x27d4eb2d);
    seed = seed ^ (seed >> 15u);
    return seed;
}
 
float RandomFloat01(inout uint state)
{
    return float(WangHash(state)) / 4294967296.0;
}
 
vec3 RandomUnitVector(inout uint state)
{
    float z = RandomFloat01(state) * 2.0 - 1.0;
    float a = RandomFloat01(state) * TWO_PI;
    float r = sqrt(1.0 - z * z);
    float x = r * cos(a);
    float y = r * sin(a);
    return vec3(x, y, z);
}

void TraceScene(in Ray ray, inout HitInfo hitInfo) {
	if(RaySphere(ray, vec3(0,0,-5), 1, hitInfo)) {
		hitInfo.albedo = vec3(0.8f, 0.2f, 0.2f);
        hitInfo.emissive = vec3(0.0f, 0.0f, 0.0f);  
	}

	if(RaySphere(ray, vec3(3,0,-5), 1, hitInfo)) {
		hitInfo.albedo = vec3(0.2f, 0.8f, 0.2f);
        hitInfo.emissive = vec3(0.0f, 0.0f, 0.0f);  
	}

	if(RaySphere(ray, vec3(-3,0,-5), 1, hitInfo)) {
		hitInfo.albedo = vec3(0.5f, 0.5f, 0.8f);
        hitInfo.emissive = vec3(0.0f, 0.0f, 0.0f);  
	}

	if(RaySphere(ray, vec3(0,3,-3), 1.5f, hitInfo)) {
		hitInfo.albedo = vec3(1.0f, 1.0f, 1.0f);
        hitInfo.emissive = vec3(1.0f, 1.0f, 1.0f);  
	}

    if(RaySphere(ray, vec3(0,-21, 0), 20, hitInfo)) {
		hitInfo.albedo = vec3(0.2f, 0.9f, 0.1f);
        hitInfo.emissive = vec3(0.0f, 0.0f, 0.0f);  
	}
}

vec3 GetColorForRay(in Ray ray, inout uint rngState)
{
    vec3 ret = vec3(0.0f, 0.0f, 0.0f);
    vec3 throughput = vec3(1.0f, 1.0f, 1.0f);
	
	Ray nextRay;
	nextRay.origin = ray.origin;
	nextRay.dir = ray.dir;

    vec3 test;

    bool directHit = false;
     
    for(uint rayIndex = 0u; rayIndex <= NUM_RAYS; ++rayIndex) {
        for (uint bounceIndex = 0u; bounceIndex <= MAX_BOUNCES; ++bounceIndex)
        {
            // shoot a ray out into the world
            HitInfo hitInfo;
            hitInfo.dist = FAR;
            TraceScene(nextRay, hitInfo);

            if(hitInfo.didHit && bounceIndex == 0u) {
                directHit = true;
            }
            
            // if the ray missed, we are done
            if (hitInfo.dist == FAR) {
                //if(!directHit) 
                vec3 skyColor = GetSkyGradient(ray) / NUM_RAYS;
                ret += skyColor * throughput;
                break;
            }
                
            // update the ray position
            nextRay.origin = (nextRay.origin + nextRay.dir * hitInfo.dist) + hitInfo.normal * RAY_POS_NORMAL_NUDGE;
                
            // calculate new ray direction, in a cosine weighted hemisphere oriented at normal
            nextRay.dir = normalize(hitInfo.normal + RandomUnitVector(rngState));        
                
            // add in emissive lighting
            ret += hitInfo.emissive * throughput;
                
            // update the colorMultiplier
            throughput *= hitInfo.albedo;      
        }
    }
  
    // return pixel color
    return ret;
}

// Invocations in the (x, y, z) dimension
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, rgba32f) uniform image2D OUTPUT_TEXTURE;

layout(binding = 1, std430) restrict buffer SizeBuffer {
    int width;
    int height;
}
size_data;

layout(binding = 2, std430) restrict buffer CameraBuffer {
    mat4 cameraToWorld;
    vec3 worldSpaceCameraPosition;
    float width;
    float height;
    float near;
    float frame;
}
camera_data;

layout(binding = 3, std430) restrict buffer DirectionalLightBuffer {
    vec3 position;
    float intensity;
}
directional_light_data;

layout(binding = 4, rgba32f) uniform image2D LAST_TEXTURE;

// Buffer for all spheres in the scene
/*layout(set = 0, binding = 4, std430) restrict buffer SpheresBuffer {
    vec4 xyzr[];
}
spheres;*/

// The code we want to execute in each invocation
void main() {
    // gl_GlobalInvocationID.x uniquely identifies this invocation across all work groups
    float u = gl_GlobalInvocationID.x / float(size_data.width);
    float v = gl_GlobalInvocationID.y / float(size_data.height);
    vec2 uv = vec2(1.0f - u, v);

    vec3 viewLocal = vec3(uv * 2.0f - 1.0f, 1.0f) * vec3(camera_data.width, camera_data.height, camera_data.near);
    vec4 view = vec4(viewLocal, 1) * camera_data.cameraToWorld;
    vec3 origin = camera_data.worldSpaceCameraPosition;
    vec3 dir = -normalize(view.xyz - origin);
    Ray ray = CreateRay(origin, dir);

    uint rngState = uint(uint(gl_GlobalInvocationID.x) * uint(1973) + uint(gl_GlobalInvocationID.y) * uint(9277) + camera_data.frame * uint(26699)) | uint(1);
    vec4 color = vec4(GetColorForRay(ray, rngState), 1.0f);

    ivec2 texel = ivec2(gl_GlobalInvocationID.xy);
    vec4 lastFrameColor = imageLoad(LAST_TEXTURE, texel);
    imageStore(LAST_TEXTURE, texel, color);

    color = mix(lastFrameColor, color, 1.0f / (camera_data.frame + 1.0f));
    imageStore(OUTPUT_TEXTURE, texel, color);
}