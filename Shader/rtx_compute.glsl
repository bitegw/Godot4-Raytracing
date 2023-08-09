#[compute]
#version 450

// #include "res://Shader/rtx_utilities.gdshaderinc"

struct Light {
    float positionX;
    float positionY;
    float positionZ;
    float colorR;
    float colorG;
    float colorB;
    float intensity;
    bool isDirectionalLight;
    float directionX;
    float directionY;
    float directionZ;
};

struct Surface {
    float positionX;
    float positionY;
    float positionZ;
    float rotationX;
    float rotationY;
    float rotationZ;
    float scaleX;
    float scaleY;
    float scaleZ;
    float boxMinX;
    float boxMinY;
    float boxMinZ;
    float boxMaxX;
    float boxMaxY;
    float boxMaxZ;
    int materialID;
    int indexStart;
    int indexEnd;
};

struct Material {
	float albedoR;
	float albedoG;
	float albedoB;
    int textureID;
	float emissiveR;
	float emissiveG;
	float emissiveB;
    int emissiveTextureID;
    float roughness;
    int roughnessTextureID;
    float alpha;
    int alphaTextureID;
};

// Invocations in the (x, y, z) dimension
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, rgba32f) uniform image2D OUTPUT_TEXTURE;

layout(binding = 1, std430) restrict buffer SettingsBuffer {
    int width;
    int height;
    uint numRays;
    uint maxBounces;
    bool temporalAccumulation;
    float recentFrameBias;
    bool checkerboard;
} settingsBuffer;

layout(binding = 2, std430) restrict buffer CameraBuffer {
    mat4 cameraBufferToWorld;
    vec3 worldSpaceCameraPosition;
    float width;
    float height;
    float near;
    float frame;
} cameraBuffer;

layout(binding = 3, std430) restrict buffer LightBuffer {
    Light lights[];
} lightBuffer;

// Mesh buffer
layout(binding = 4, std430) readonly buffer SurfaceBuffer {
    Surface surfaces[];
} surfaceBuffer;

// Vertex buffer
layout(binding = 5, std430) readonly buffer VertexBuffer {
    float vertices[]; // Or your vertex type
} vertexBuffer;

layout(binding = 6, std430) readonly buffer NormalBuffer {
    float normals[]; // Or your vertex type
} normalBuffer;

layout(binding = 7, std430) readonly buffer UVBuffer {
    vec2 uvs[]; // Or your vertex type
} uvBuffer;

// Index buffer
layout(binding = 8, std430) readonly buffer IndexBuffer {
    int indices[];
} indexBuffer;

// Material buffer
layout(binding = 9, std430) readonly buffer MaterialBuffer {
    Material materials[];
} materialBuffer;

// // Albedo texture array
// layout(binding = 8) uniform texture2DArray albedoTextures;

// // Roughness texture array
// layout(binding = 9) uniform texture2DArray roughnessTextures;

// // Emissive texture array
// layout(binding = 10) uniform texture2DArray emissiveTextures;

// // Alpha texture array
// layout(binding = 11) uniform texture2DArray alphaTextures;

// layout(binding = 12, rgba32f) uniform image2D ENVIRONMENT;

const float FAR = 4000.0f;
const float RAY_POS_NORMAL_NUDGE = 0.01f;
const float TWO_PI = 6.28318530718f;

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
	Material material;
};

vec3 GetSkyGradient(Ray ray) {
	float t = 0.5f * (ray.dir.y + 1.0f);
    vec3 a = vec3(0.239, 0.20, 0.153);
    vec3 b = vec3(1, 1, 1);
    vec3 c = vec3(0.5882, 0.7137, 0.8745);
	vec3 ab = mix(a, b, smoothstep(0, 0.15, t - 0.4));
    vec3 bc = mix(b, c, smoothstep(0.55, 1, t + 0.2));
    
    return mix(ab, bc, smoothstep(0.45, 0.55, t));
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

bool RayTriangle(in Ray ray, vec3 a, vec3 b, vec3 c, vec3 normA, vec3 normB, vec3 normC, inout HitInfo hitInfo)
{
    vec3 edge1 = b - a;
    vec3 edge2 = c - a;
    vec3 h = cross(ray.dir, edge2);
    float a_dot_edge1 = dot(edge1, h);
    
    if (a_dot_edge1 > -1E-6 && a_dot_edge1 < 1E-6) {
        return false; // Ray and triangle are parallel
    }
    
    float f = 1.0 / a_dot_edge1;
    vec3 s = ray.origin - a;
    float u = f * dot(s, h);
    
    if (u < 0.0 || u > 1.0) {
        return false;
    }
    
    vec3 q = cross(s, edge1);
    float v = f * dot(ray.dir, q);
    
    if (v < 0.0 || u + v > 1.0) {
        return false;
    }
    
    float t = f * dot(edge2, q);
    
    if (t > 1E-6 && t < hitInfo.dist) {
        // Calculate the triangle normal based on the barycentric coordinates
        vec3 normal = normalize(normA * (1.0 - u - v) + normB * u + normC * v);
        
        // Calculate the dot product between ray direction and triangle normal
        float dotProduct = dot(normal, ray.dir);

        hitInfo.normal = normal;
        
        // Check if the hit is from the front side of the triangle
        if (dotProduct > 0.0) {
            return false; // Return false if hit is from the back side
        }

        hitInfo.didHit = true;
        hitInfo.dist = t;
        hitInfo.point = ray.origin + ray.dir * t;
        
        // Set the hitInfo normal to the calculated normal
        
        
        return true;
    }

    return false;
}

bool IntersectAABB(vec3 rayOrigin, vec3 rayDir, vec3 boxMin, vec3 boxMax) {
    vec3 tMin = (boxMin - rayOrigin) / rayDir;
    vec3 tMax = (boxMax - rayOrigin) / rayDir;
    vec3 t1 = min(tMin, tMax);
    vec3 t2 = max(tMin, tMax);
    float tNear = max(max(t1.x, t1.y), t1.z);
    float tFar = min(min(t2.x, t2.y), t2.z);
    return (tNear > tFar);
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

mat3 rotationXMatrix(float angle) {
    float cosAngle = cos(angle);
    float sinAngle = sin(angle);
    
    return mat3(
        vec3(1.0, 0.0, 0.0),
        vec3(0.0, cosAngle, -sinAngle),
        vec3(0.0, sinAngle, cosAngle)
    );
}

mat3 rotationYMatrix(float angle) {
    float cosAngle = cos(angle);
    float sinAngle = sin(angle);
    
    return mat3(
        vec3(cosAngle, 0.0, sinAngle),
        vec3(0.0, 1.0, 0.0),
        vec3(-sinAngle, 0.0, cosAngle)
    );
}

mat3 rotationZMatrix(float angle) {
    float cosAngle = cos(angle);
    float sinAngle = sin(angle);
    
    return mat3(
        vec3(cosAngle, -sinAngle, 0.0),
        vec3(sinAngle, cosAngle, 0.0),
        vec3(0.0, 0.0, 1.0)
    );
}

void TraceScene(in Ray ray, inout HitInfo hitInfo) {
    Surface surface;
    Material material;
    Ray offsetRay;
    offsetRay.energy = ray.energy;

    Material defaultMaterial;
    defaultMaterial.albedoR = 1;
    defaultMaterial.albedoG = 0;
    defaultMaterial.albedoB = 1;
    defaultMaterial.emissiveR = 1;
    defaultMaterial.emissiveG = 0;
    defaultMaterial.emissiveB = 1;
    defaultMaterial.roughness = 0.5f;
    defaultMaterial.alpha = 1.0f;

    defaultMaterial.textureID = -1;
    defaultMaterial.emissiveTextureID = -1;
    defaultMaterial.roughnessTextureID = -1;
    defaultMaterial.alphaTextureID = -1;

    for(int i=0; i < surfaceBuffer.surfaces.length(); i++) {
        surface = surfaceBuffer.surfaces[i];
        vec3 position = vec3(surface.positionX, surface.positionY, surface.positionZ);
        vec3 boxMin = vec3(surface.boxMinX, surface.boxMinY, surface.boxMinZ);
        vec3 boxMax = vec3(surface.boxMaxX, surface.boxMaxY, surface.boxMaxZ);

        mat3 rotationMatrix = rotationZMatrix(surface.rotationZ) * rotationYMatrix(surface.rotationY) * rotationXMatrix(surface.rotationX);
        mat3 scaleMatrix = mat3(
            vec3(surface.scaleX, 0.0, 0.0),
            vec3(0.0, surface.scaleY, 0.0),
            vec3(0.0, 0.0, surface.scaleZ)
        );
        mat3 transformMatrix =  rotationMatrix * scaleMatrix;

        if(IntersectAABB(ray.origin - position, ray.dir, transformMatrix * boxMin, transformMatrix * boxMax)) {
            continue;
        }

        if(surface.materialID >= 0) {
            material = materialBuffer.materials[surface.materialID];
        } else {
            material = defaultMaterial;
        }

        for(int j=surface.indexStart; j<surface.indexEnd; j+=3) {
            int offsetAX = indexBuffer.indices[j] * 3;
            int offsetAY = indexBuffer.indices[j] * 3 + 1;
            int offsetAZ = indexBuffer.indices[j] * 3 + 2;
            int offsetBX = indexBuffer.indices[j+1] * 3;
            int offsetBY = indexBuffer.indices[j+1] * 3 + 1;
            int offsetBZ = indexBuffer.indices[j+1] * 3 + 2;
            int offsetCX = indexBuffer.indices[j+2] * 3;
            int offsetCY = indexBuffer.indices[j+2] * 3 + 1;
            int offsetCZ = indexBuffer.indices[j+2] * 3 + 2;
            vec3 a = vec3(vertexBuffer.vertices[offsetAX], vertexBuffer.vertices[offsetAY], vertexBuffer.vertices[offsetAZ]);
            vec3 b = vec3(vertexBuffer.vertices[offsetBX], vertexBuffer.vertices[offsetBY], vertexBuffer.vertices[offsetBZ]);
            vec3 c = vec3(vertexBuffer.vertices[offsetCX], vertexBuffer.vertices[offsetCY], vertexBuffer.vertices[offsetCZ]);
            vec3 nA = vec3(normalBuffer.normals[offsetAX], normalBuffer.normals[offsetAY], normalBuffer.normals[offsetAZ]);
            vec3 nB = vec3(normalBuffer.normals[offsetBX], normalBuffer.normals[offsetBY], normalBuffer.normals[offsetBZ]);
            vec3 nC = vec3(normalBuffer.normals[offsetCX], normalBuffer.normals[offsetCY], normalBuffer.normals[offsetCZ]);

            a = (transformMatrix * a) + position;
            b = (transformMatrix * b) + position;
            c = (transformMatrix * c) + position;
            nA = normalize(transformMatrix * nA);
            nB = normalize(transformMatrix * nB);
            nC = normalize(transformMatrix * nC);

            if(RayTriangle(ray, 
                a, 
                b, 
                c, 
                nA, 
                nB, 
                nC,
                hitInfo
            )) {
                hitInfo.material = material;
            }
        }
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
     
    for(uint rayIndex = 0u; rayIndex <= settingsBuffer.numRays; ++rayIndex) {
        for (uint bounceIndex = 0u; bounceIndex <= settingsBuffer.maxBounces; ++bounceIndex)
        {
            // shoot a ray out into the world
            HitInfo hitInfo;
            hitInfo.dist = FAR;
            TraceScene(nextRay, hitInfo);
            
            // if the ray missed, we are done
            if (hitInfo.dist == FAR) {
                vec3 skyColor = GetSkyGradient(ray) / settingsBuffer.numRays;
                ret += skyColor * throughput;
                break;
            }
                
            // update the ray position
            nextRay.origin = (nextRay.origin + nextRay.dir * hitInfo.dist) + hitInfo.normal * RAY_POS_NORMAL_NUDGE;
                
            // calculate new ray direction, in a cosine weighted hemisphere oriented at normal
            vec3 diffuseDir = normalize(hitInfo.normal + RandomUnitVector(rngState));
            vec3 specularDir = reflect(ray.dir, hitInfo.normal);

            Material mat = hitInfo.material;

            nextRay.dir = mix(specularDir, diffuseDir, mat.roughness);
                
            // add in emissive lighting
            ret += vec3(mat.emissiveR, mat.emissiveG, mat.emissiveB) * throughput;
                
            // update the colorMultiplier
            throughput *= vec3(mat.albedoR, mat.albedoG, mat.albedoB);
        }
    }
  
    // return pixel color
    return ret;
}

// Buffer for all spheres in the scene
/*layout(binding = 4, std430) restrict buffer SpheresBuffer {
    vec4 xyzr[];
}
spheres;*/

// The code we want to execute in each invocation
void main() {
    // gl_GlobalInvocationID.x uniquely identifies this invocation across all work groups
    float u = gl_GlobalInvocationID.x / float(settingsBuffer.width);
    float v = gl_GlobalInvocationID.y / float(settingsBuffer.height);

    // Skip rendering this pixel if it's odd/even compared to last frame, if checkerboard is enabled.
    if(settingsBuffer.checkerboard && int(gl_GlobalInvocationID.x+gl_GlobalInvocationID.y)%2 == int(cameraBuffer.frame)%2) {
        return;
    }

    vec2 uv = vec2(1.0f - u, v);

    vec3 viewLocal = vec3(uv * 2.0f - 1.0f, 1.0f) * vec3(cameraBuffer.width, cameraBuffer.height, cameraBuffer.near);
    vec4 view = vec4(viewLocal, 1) * cameraBuffer.cameraBufferToWorld;
    vec3 origin = cameraBuffer.worldSpaceCameraPosition;
    vec3 dir = -normalize(view.xyz - origin);
    Ray ray = CreateRay(origin, dir);

    uint rngState = uint(uint(gl_GlobalInvocationID.x) * uint(1973) + uint(gl_GlobalInvocationID.y) * uint(9277) + cameraBuffer.frame * uint(26699)) | uint(1);

    vec4 color = vec4(GetColorForRay(ray, rngState), 1.0f);

    // vec4 color = vec4(surfaceBuffer.surfaces[1].indexStart/12, (surfaceBuffer.surfaces[1].indexEnd - 6)/48, 0, 1.0f);

    // vec4 color = vec4(materialBuffer.materials[0].albedoR,materialBuffer.materials[0].albedoG,materialBuffer.materials[0].albedoB, 1.0f);
    // Material mat = materialBuffer.materials[surfaceBuffer.surfaces[0].materialID];
    // vec4 color = vec4(mat.albedoR, mat.albedoG, mat.albedoB, 1);
    
    // vec4 color = vec4(surfaceBuffer.surfaces.length());
    // vec4 color = vec4(surfaceBuffer.surfaces[0].indexStart, surfaceBuffer.surfaces[0].indexEnd, 1, 1);

    // int j = int(cameraBuffer.frame/60) % 6;
    // settingsBuffer.temporalAccumulation = false;
    // int offsetAX = indexBuffer.indices[j] * 3;
    // int offsetAY = indexBuffer.indices[j] * 3 + 1;
    // int offsetAZ = indexBuffer.indices[j] * 3 + 2;
    // vec3 vert = vec3(vertexBuffer.vertices[offsetAX], vertexBuffer.vertices[offsetAY], vertexBuffer.vertices[offsetAZ]);
    // vec3[] verts = {vec3(1,0,1), vec3(-1,0,1), vec3(1,0,-1), vec3(-1,0,1), vec3(-1,0,-1), vec3(1,0,-1)};
    // vec3 vertRef = verts[j];
    // vec4 color;
    // if(v > 0.55f) {
    //     if(u > 0.66f) {
    //         color = vec4(-vert.z, vert.z, 0, 1);
    //     } else if(u > 0.33f) {
    //         color = vec4(-vert.y, vert.y, 0, 1);
    //     } else {
    //         color = vec4(-vert.x, vert.x, 0, 1);
    //     }
    // } else if(v <= 0.45f) {
    //     if(u > 0.66f) {
    //         color = vec4(-vertRef.z, vertRef.z, 0, 1);
    //     } else if(u > 0.33f) {
    //         color = vec4(-vertRef.y, vertRef.y, 0, 1);
    //     } else {
    //         color = vec4(-vertRef.x, vertRef.x, 0, 1);
    //     }
    // } else {
    //     color = vec4(0.5f);
    // }

    ivec2 texel = ivec2(gl_GlobalInvocationID.xy);
    vec4 lastFrameColor = imageLoad(OUTPUT_TEXTURE, texel);

    if(settingsBuffer.temporalAccumulation) {
        color = mix(lastFrameColor, color, settingsBuffer.recentFrameBias + 1.0f / (cameraBuffer.frame + 1.0f));
    }

    imageStore(OUTPUT_TEXTURE, texel, color);
}