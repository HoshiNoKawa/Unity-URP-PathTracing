#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Macros.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

// Struct
struct Ray
{
    float3 origin;
    float3 direction;
    float3 invDir;
};

struct Material
{
    float3 baseColor;
    float3 emissionColor;
    float metallic;
    float roughness;
    int enableEmission;
    float specularTransmission;
    float subsurface;
    float specular;
    float specularTint;
    float anisotropic;
    float sheen;
    float sheenTint;
    float clearcoat;
    float clearcoatGloss;
    float IOR;
    int texIndex;
};

struct HitPayload
{
    Material mat;
    float4 tangent;
    float3 position;
    float3 geometricNormal;
    float3 shadingNormal;
    float2 uv;
    float closestT;
    bool hit;
};

struct Sphere
{
    Material mat;
    float3 center;
    float radius;
};

struct Plane
{
    Material mat;
    float height;
};

struct Triangle
{
    float4 tangent0, tangent1, tangent2;
    float3 vert0, vert1, vert2;
    float3 normal0, normal1, normal2;
    float2 uv0, uv1, uv2;
};

struct BVHNode
{
    float3 AABBMin;
    float3 AABBMax;
    int leftChild;
    int triStart;
};

struct Mesh
{
    Material mat;
    float4x4 M;
    float4x4 nM;
    int rootNode;
    int nodeOffset;
    int triOffset;
};

float4x4 _CameraToWorld;
float4x4 _CameraInverseProjection;

// Create
Ray CreateRay(float2 uv)
{
    Ray ray;
    ray.origin = mul(_CameraToWorld, float4(0.0f, 0.0f, 0.0f, 1.0f)).xyz;

    float3 direction = mul(_CameraInverseProjection, float4(uv, 0.0f, 1.0f)).xyz;
    direction = mul(_CameraToWorld, float4(direction, 0.0f)).xyz;
    ray.direction = normalize(direction);

    return ray;
}

Material CreateMaterial()
{
    Material mat = {
        float3(0, 0, 0), float3(0, 0, 0), 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0
    };
    return mat;
}

HitPayload CreateHitPayload()
{
    HitPayload payload;
    payload.hit = false;
    payload.closestT = FLT_MAX;
    payload.position = 0;
    payload.geometricNormal = 0;
    payload.shadingNormal = 0;
    payload.tangent = 0;
    payload.uv = 0;
    payload.mat = CreateMaterial();

    return payload;
}

// Triangle CreateTriangle(uint3 indices, in StructuredBuffer<float3> VertexBuffer,
//                         in StructuredBuffer<float3> NormalBuffer)
// {
//     Triangle tri = {
//         VertexBuffer[indices.x], VertexBuffer[indices.y], VertexBuffer[indices.z], NormalBuffer[indices.x],
//         NormalBuffer[indices.y], NormalBuffer[indices.z]
//     };
//
//     return tri;
// }
