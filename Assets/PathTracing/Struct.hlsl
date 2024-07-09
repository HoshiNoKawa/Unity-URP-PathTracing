#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Macros.hlsl"

// Struct
struct Ray
{
    float3 origin;
    float3 direction;
};

struct Material
{
    float3 baseColor;
    float metallic;
    float roughness;
    int enableEmission;
    float3 emissionColor;
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
};

struct HitPayload
{
    float3 position;
    float3 normal;
    float closestT;
    Material mat;
};

struct Sphere
{
    float3 center;
    float radius;
    Material mat;
};

struct Plane
{
    float height;
    Material mat;
};

struct Triangle
{
    float3 vert0, vert1, vert2;
    float3 normal0, normal1, normal2;
};

struct Mesh
{
    uint triangleIndexBegin;
    uint triangleIndexEnd;
    uint preVertexCount;
    int withoutBV;
    float3 AABBMin;
    float3 AABBMax;
    float4x4 M;
    float4x4 nM;
    Material mat;
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

HitPayload CreateHitPayload()
{
    HitPayload payload;
    payload.closestT = FLT_MAX;
    payload.position = 0;
    payload.normal = 0;

    return payload;
}

Triangle CreateTriangle(uint3 indices, in StructuredBuffer<float3> VertexBuffer,
                        in StructuredBuffer<float3> NormalBuffer)
{
    Triangle tri = {
        VertexBuffer[indices.x], VertexBuffer[indices.y], VertexBuffer[indices.z], NormalBuffer[indices.x],
        NormalBuffer[indices.y], NormalBuffer[indices.z]
    };

    return tri;
}