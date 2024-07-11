#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"
#include "Struct.hlsl"

// Random
uint PCG_Hash(uint seed)
{
    uint state = seed * 747796405u * 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

float RandomFloat(inout uint seed)
{
    seed = PCG_Hash(seed);
    return pow(float(seed) / float(UINT_MAX), 1.0);
}

// Transform
void TriObjectToWorld(inout Triangle tri, float4x4 M, float4x4 nM)
{
    tri.vert0 = mul(M, float4(tri.vert0, 1)).xyz;
    tri.vert1 = mul(M, float4(tri.vert1, 1)).xyz;
    tri.vert2 = mul(M, float4(tri.vert2, 1)).xyz;

    tri.normal0 = normalize(mul(nM, float4(tri.normal0, 0)).xyz);
    tri.normal1 = normalize(mul(nM, float4(tri.normal1, 0)).xyz);
    tri.normal2 = normalize(mul(nM, float4(tri.normal2, 0)).xyz);
}

float3 AABBObjectToWorld(float3 aabb, float4x4 M)
{
    return mul(M, float4(aabb, 1)).xyz;
}

Ray RayWorldToObject(Ray ray, float4x4 nMt)
{
    Ray transRay;
    float4x4 nM = transpose(nMt);
    transRay.origin = mul(nM, float4(ray.origin, 1)).xyz;
    transRay.direction = normalize(mul(nM, float4(ray.direction, 0)).xyz);
    return transRay;
}

float3 ToWorld(float3 target, float3 normal)
{
    float3 helper = float3(1, 0, 0);
    if (abs(normal.x) > 0.99f)
        helper = float3(0, 0, 1);

    float3 tangent = normalize(cross(normal, helper));
    float3 binormal = normalize(cross(normal, tangent));
    return mul(target, float3x3(tangent, binormal, normal));
}

float3 ToLocal(float3 target, float3 normal)
{
    float3 helper = float3(1, 0, 0);
    if (abs(normal.x) > 0.99f)
        helper = float3(0, 0, 1);

    float3 tangent = normalize(cross(normal, helper));
    float3 binormal = normalize(cross(normal, tangent));
    return mul(target, transpose(float3x3(tangent, binormal, normal)));
}

float3 SchlickFresnel(float3 F0, float cos_theta)
{
    return lerp(pow(max(1 - cos_theta, 0), 5.0), 1.0, F0);
}

float DielectricFresnel(float hv, float eta)
{
    float hl_sq = 1 - (1 - hv * hv) / (eta * eta);
    if (hl_sq < 0)
    {
        // total internal reflection
        return 1;
    }
    float hl = sqrt(hl_sq);

    hv = abs(hv);

    float Rs = (hv - eta * hl) / (hv + eta * hl);
    float Rp = (eta * hv - hl) / (eta * hv + hl);
    float F = (Rs * Rs + Rp * Rp) / 2;
    return F;
}

float GGXNDF(float nh, float alpha2)
{
    float t = 1 + (alpha2 - 1) * nh * nh;
    return alpha2 / (PI * t * t);
}

float SmithGGXMasking(float3 n, float3 v, float alpha2)
{
    float3 local_v = ToLocal(v, n);
    float3 v2 = local_v * local_v;
    return 2 / (1 + sqrt(1 + (v2.x * alpha2 + v2.y * alpha2) / v2.z));
}

// PDF
float DiffusePdf(float3 l, float3 n)
{
    return dot(n, l) / PI;
}

float MetalPdf(float3 h, float3 n, float3 v, float alphag)
{
    float alpha2 = alphag * alphag;

    float D = GGXNDF(dot(n, h), alpha2);
    float G = SmithGGXMasking(n, v, alpha2);

    return G * D / (4 * abs(dot(n, v)));
}

float ClearcoatPdf(float3 h, float3 n, float3 v, float alphagc)
{
    float nh = dot(n, h);
    float alpha2c = alphagc * alphagc;

    return (alpha2c - 1.0) * nh / (FOUR_PI * dot(h, v) * log(alpha2c) * (1.0 + (alpha2c - 1.0) * nh * nh));
}

float GlassPDF(float3 l, float3 n, float3 v, Material mat)
{
    float nv = dot(n, v);
    float nl = dot(n, l);
    float eta = nv > 0 ? mat.IOR : 1.0 / mat.IOR;
    float3 h = dot(n, v) * dot(n, l) > 0 ? normalize(l + v) : normalize(v + eta * l);
    if (dot(n, h) < 0)
        h = -h;

    float alpha = max(mat.roughness * mat.roughness, 1e-4);
    float alpha2 = alpha * alpha;

    float hv = dot(h, v);
    float F = DielectricFresnel(hv, eta);
    float D = GGXNDF(dot(n, h), alpha2);
    float G_in = SmithGGXMasking(n, v, alpha2);

    if (nv * nl > 0)
    {
        return F * D * G_in / (4 * abs(nv));
    }
    else
    {
        float hl = dot(h, l);
        float sqrt_denom = hv + eta * hl;
        float dh_dout = eta * eta * hl / (sqrt_denom * sqrt_denom);
        return (1 - F) * D * G_in * abs(dh_dout * hv / nv);
    }
}

// Sample
float3 InUnitSphere(inout uint seed)
{
    return normalize(
        float3(RandomFloat(seed) * 2.0 - 1.0, RandomFloat(seed) * 2.0 - 1.0, RandomFloat(seed) * 2.0 - 1.0));
}

float3 SphereSample(inout uint seed, float3 normal)
{
    float cosTheta = 2.0 * RandomFloat(seed) - 1.0;
    float sinTheta = sqrt(1.0f - cosTheta * cosTheta);
    float phi = TWO_PI * RandomFloat(seed);
    float3 tangentSpaceDir = float3(cos(phi) * sinTheta, sin(phi) * sinTheta, cosTheta);

    return ToWorld(tangentSpaceDir, normal);
}

float3 HemisphereSample(inout uint seed, float3 normal)
{
    float cosTheta = RandomFloat(seed);
    float sinTheta = sqrt(1.0f - cosTheta * cosTheta);
    float phi = TWO_PI * RandomFloat(seed);
    float3 tangentSpaceDir = float3(cos(phi) * sinTheta, sin(phi) * sinTheta, cosTheta);

    return ToWorld(tangentSpaceDir, normal);
}

float3 CosineWeightedHemisphereSample(inout uint seed, float3 normal)
{
    float cosTheta = sqrt(RandomFloat(seed));
    float sinTheta = sqrt(1.0f - cosTheta * cosTheta);
    float phi = TWO_PI * RandomFloat(seed);
    float3 tangentSpaceDir = float3(cos(phi) * sinTheta, sin(phi) * sinTheta, cosTheta);

    return ToWorld(tangentSpaceDir, normal);
}

float3 SampleVisibleNormals(inout uint seed, float3 local_v, float alpha)
{
    float2 rnd_param = float2(RandomFloat(seed), RandomFloat(seed));
    if (local_v.z < 0)
    {
        local_v *= -1;
    }

    float3 hemi_dir_in = normalize(
        float3(alpha * local_v.x, alpha * local_v.y, local_v.z));

    float r = sqrt(rnd_param.x);
    float phi = TWO_PI * rnd_param.y;
    float t1 = r * cos(phi);
    float t2 = r * sin(phi);
    float s = (1 + hemi_dir_in.z) / 2;
    t2 = (1 - s) * sqrt(1 - t1 * t1) + s * t2;
    float3 disk_N = float3(t1, t2, sqrt(max(0, 1 - t1 * t1 - t2 * t2)));

    float3 hemi_N = ToWorld(disk_N, hemi_dir_in);

    return normalize(float3(alpha * hemi_N.x, alpha * hemi_N.y, max(0, hemi_N.z)));
}

float3 ClearcoatSample(inout uint seed, float3 n, float3 v, Material mat, out float3 h)
{
    float alphagc = lerp(0.1, 0.001, mat.clearcoatGloss);
    float alpha2c = alphagc * alphagc;

    float cosTheta = sqrt((1.0 - pow(alpha2c, RandomFloat(seed))) / (1.0 - alpha2c));
    float sinTheta = sqrt(1.0f - cosTheta * cosTheta);
    float phi = TWO_PI * RandomFloat(seed);
    float3 tangentSpaceDir = float3(cos(phi) * sinTheta, sin(phi) * sinTheta, cosTheta);
    h = ToWorld(tangentSpaceDir, n);

    return reflect(-v, h);
}

float3 MetalSample(inout uint seed, float3 n, float3 v, float alphag, out float3 h)
{
    float3 local_v = ToLocal(v, n);
    h = SampleVisibleNormals(seed, local_v, alphag);
    h = ToWorld(h, n);
    return reflect(-v, h);
}

float3 GlassSample(inout uint seed, float3 n, float3 v, Material mat, out float3 h)
{
    float nv = dot(n, v);
    float eta = nv > 0 ? mat.IOR : 1.0 / mat.IOR;

    float alpha = mat.roughness * mat.roughness;
    float3 local_v = ToLocal(v, n);
    float3 local_h =
        SampleVisibleNormals(seed, local_v, alpha);

    h = ToWorld(local_h, n);

    if (dot(h, n) < 0)
    {
        h = -h;
    }

    float hv = dot(h, v);
    float F = DielectricFresnel(hv, eta);

    float u = RandomFloat(seed);
    if (u < F)
    {
        float3 reflected = reflect(-v, h);
        return reflected;
    }
    else
    {
        if (hv < 0)
        {
            h = -h;
        }
        float3 refracted = normalize(refract(-v, h, 1 / eta));
        return refracted;
    }
}

float MultipleImportanceSample(inout uint seed, float3 v, float3 n, Material mat, out float3 l)
{
    float3 h;

    float alphag = max(mat.roughness * mat.roughness, 1e-4);
    float alphagc = lerp(0.1, 0.001, mat.clearcoatGloss);

    if (dot(n, v) < 0)
    {
        l = GlassSample(seed, n, v, mat, h);
        return 1 / GlassPDF(l, n, v, mat);
    }
    else
    {
        float u = RandomFloat(seed);
    
        float sumWeights = 1.0 + (1.0 - mat.metallic) * (1.0 - mat.specularTransmission) + 0.25 * mat.clearcoat;
        float p0 = (1.0 - mat.metallic) * mat.specularTransmission / sumWeights;
        float p1 = 1.0 - mat.metallic / sumWeights;
        float p2 = 1.0 - 0.25 * mat.clearcoat / sumWeights;
    
        if (u < p0)
        {
            l = GlassSample(seed, n, v, mat, h);
        }
        else if (u < p1)
        {
            l = CosineWeightedHemisphereSample(seed, n);
            h = normalize(l + v);
        }
        else if (u < p2)
        {
            l = MetalSample(seed, n, v, alphag, h);
        }
        else
        {
            l = ClearcoatSample(seed, n, v, mat, h);
        }
        return 1 / (p0 * GlassPDF(l, n, v, mat) + (p1 - p0) * DiffusePdf(l, n) + (p2 - p1) *
            MetalPdf(h, n, v, alphag) + (1 - p2) * ClearcoatPdf(h, n, v, alphagc));
    }

    // float u = RandomFloat(seed);
    // float p1 = 1.0 - mat.metallic;
    // if (u < p1)
    // {
    //     l = CosineWeightedHemisphereSample(seed, n);
    //     h = normalize(l + v);
    // }
    // else
    // {
    //     l = MetalSample(seed, n, v, alphag, h);
    // }
    // return 1 / (p1 * DiffusePdf(l, n) + (1 - p1) * MetalPdf(h, n, v, alphag));

    // float u = RandomFloat(seed);
    // float p1 = 1.0 - mat.specularTransmission;
    // if (u < p1)
    // {
    //     l = CosineWeightedHemisphereSample(seed, n);
    //     h = normalize(l + v);
    // }
    // else
    // {
    //     l = GlassSample(seed, n, v, mat, h);
    // }
    // return 1 / (p1 * DiffusePdf(l, n) + (1 - p1) * GlassPDF(l, n, v, mat));

    // l = CosineWeightedHemisphereSample(seed, n);
    // return abs(1 / DiffusePdf(l, n));

    // l = MetalSample(seed, n, v, alphag, h);
    // h = SampleVndf_GGX(seed, v, alphag, n);
    // l = reflect(-v, h);
    // return 1 / MetalPdf(h, n, v, alphag);
    // return 1 / VNDFPdf(h, n, v, alphag);
    // return 1;

    // l = ClearcoatSample(seed, n, v, mat, h);
    // return 1 / ClearcoatPdf(h, n, v, alphagc);

    // l = GlassSample(seed, n, v, mat, h);
    // return 1 / GlassPDF(l, n, v, mat);

    // l = GlassSample(seed, n, v, mat, h);
    // return 1 / GlassPDF(l, n, v, mat);
}
