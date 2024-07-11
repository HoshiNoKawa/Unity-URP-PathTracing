﻿#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Sample.hlsl"

// Intersection
float SphereIntersection(Sphere sphere, Ray ray)
{
    float3 co = ray.origin - sphere.center;

    float b = dot(co, ray.direction);
    float c = dot(co, co) - sphere.radius * sphere.radius;

    float discriminant = b * b - c;

    if (discriminant > 0.0)
    {
        float t1 = -sqrt(discriminant) - b;
        if (t1 > 0.0)
            return t1;

        float t2 = sqrt(discriminant) - b;
        if (t2 > 0.0)
            return t2;
    }
    return FLT_MAX;
}

void PlaneIntersection(Plane plane, Ray ray, inout HitPayload payload)
{
    float t = (plane.height - ray.origin.y) / ray.direction.y;
    if (t > 0.0 && t < payload.closestT)
    {
        payload.closestT = t;
        payload.normal = normalize(float3(0.0, 1.0, 0.0));
        payload.mat = plane.mat;
    }
}

void TriangleIntersection(Triangle tri, Ray ray, inout HitPayload payload, Material mat, float4x4 M, float4x4 nM)
{
    float3 edge01 = tri.vert1 - tri.vert0;
    float3 edge02 = tri.vert2 - tri.vert0;

    float3 normalVector = cross(edge01, edge02);
    float3 ao = ray.origin - tri.vert0;
    float3 dao = cross(ao, ray.direction);

    float determinant = -dot(ray.direction, normalVector);
    if (determinant < 1e-6)
        return;

    float invDet = 1 / determinant;
    float u = dot(edge02, dao) * invDet;
    float v = -dot(edge01, dao) * invDet;
    float w = 1 - u - v;
    if (u < 0 || v < 0 || w < 0)
        return;

    float t = dot(ao, normalVector) * invDet;

    if (t > 0.0 && t < payload.closestT)
    {
        payload.closestT = t;
        payload.normal = normalize(tri.normal0 * w + tri.normal1 * u + tri.normal2 * v);
        payload.position = ray.origin + t * ray.direction;
        payload.mat = mat;
    }
}

bool AABBIntersection(float3 AABBMin, float3 AABBMax, Ray ray)
{
    float3 f = (AABBMax - ray.origin) / ray.direction;
    float3 n = (AABBMin - ray.origin) / ray.direction;

    float3 tmax = max(f, n);
    float3 tmin = min(f, n);

    float t1 = min(tmax.x, min(tmax.y, tmax.z));
    float t0 = max(tmin.x, max(tmin.y, tmin.z));

    return t1 >= t0 ? true : false;
}

// BRDF
float3 LambertianBRDF(float3 specColor, float3 diffColor, float nl, float nv)
{
    return 1.05 / PI * (1.0 - specColor) * diffColor * (1 - pow(1 - nl, 5)) * (1 - pow(1 - nv, 5));
}

float3 HammonRDF(float3 specColor, float3 diffColor, float nl, float nv, float nh, float lv, float roughness)
{
    float3 fsmooth = 1.05 * (1.0 - specColor) * (1.0 - pow(1.0 - nl, 5.0)) * (1.0 - pow(1.0 - nv, 5.0));
    float kfacing = 0.5 + 0.5 * lv;
    float frough = kfacing * (0.9 - 0.4 * kfacing) * ((0.5 + nh) / nh);
    float fmulti = 0.3641 * roughness;
    return diffColor / PI * ((1.0 - roughness) * fsmooth + roughness * frough + diffColor * fmulti);
}

float3 GGXBRDF(float3 specColor, float nl, float nv, float hl, float nh, float roughness)
{
    float alphag = max(roughness, 1e-3);
    float alpha2 = alphag * alphag;

    float3 F = specColor + (1.0 - specColor) * pow(1.0 - hl, 5);
    float D = alpha2 / pow(pow(nh, 2.0) * (alpha2 - 1.0) + 1.0, 2.0) / PI;
    float G = 0.5 / (nv * sqrt(alpha2 + pow(nl, 2) * (1 - alpha2)) + nl * sqrt(
        alpha2 + pow(nv, 2) * (1 - alpha2)));

    return F * D * G;
}

float3 DisneyDiffuse(float3 l, float3 h, float nl, float nv, Material mat)
{
    float FSS90 = mat.roughness * pow(dot(h, l), 2.0);
    float FD90 = 0.5 + 2.0 * FSS90;
    float fd = (1.0 + (FD90 - 1.0) * pow(1.0 - abs(nl), 5.0)) * (1.0 + (FD90 - 1.0) * pow(1.0 - abs(nv), 5.0));
    float FSS = (1.0 + (FSS90 - 1.0) * pow(1.0 - abs(nl), 5.0)) * (1.0 + (FSS90 - 1.0) * pow(1.0 - abs(nv), 5.0));
    float fss = 1.25 * ((1.0 / (abs(nl) + abs(nv)) - 0.5) * FSS + 0.5);
    return mat.baseColor / PI * lerp(fd, fss, mat.subsurface) * abs(nl);
}

float3 DisneyMetal(float3 n, float3 l, float3 v, float3 h, float nv, Material mat, float3 Ctint, float R0)
{
    float alphag = max(mat.roughness * mat.roughness, 1e-4);
    float alpha2 = alphag * alphag;

    float3 Ks = lerp(1.0, Ctint, mat.specularTint);
    float3 C0 = lerp(mat.specular * R0 * Ks, mat.baseColor, mat.metallic);

    float3 Fm = SchlickFresnel(C0, dot(h, l));
    float Dm = GGXNDF(dot(n, h), alpha2);
    float Gm = SmithGGXMasking(n, v, alpha2) * SmithGGXMasking(n, l, alpha2);
    return Fm * Dm * Gm / (4.0 * abs(nv));

    // float3 Fm = lerp(pow(1.0 - hl, 5.0), 1.0, C0);
    // float G1 = 2.0 / (1.0 + sqrt(1.0 + alpha2 * (1.0 / (nl * nl) - 1.0)));
    // return Fm * G1;
}

float3 DisneyClearCoat(float3 n, float3 l, float3 v, float3 h, float nv, Material mat, float R0)
{
    float alphagc = lerp(0.1, 0.001, mat.clearcoatGloss);
    float alpha2c = alphagc * alphagc;
    float3 Fc = SchlickFresnel(R0, dot(h, l));
    float nh = dot(n, h);
    float Dc = (alpha2c - 1.0) / (PI * log(alpha2c) * (nh * nh * (alpha2c - 1.0) + 1.0));
    float Gc = SmithGGXMasking(n, v, 0.0625) * SmithGGXMasking(n, l, 0.0625);
    return Fc * Dc * Gc / (4.0 * abs(nv));
}

float3 DisneySheen(float3 l, float3 h, float nl, Material mat, float3 Ctint)
{
    float3 Csheen = lerp(1.0, Ctint, mat.sheenTint);
    return Csheen * pow(1.0 - dot(h, l), 5.0) * abs(nl);
}

float3 DisneyGlass(float3 n, float3 l, float3 v, float3 h, float nl, float nv, Material mat)
{
    float alphag = max(mat.roughness * mat.roughness, 1e-4);
    float alpha2 = alphag * alphag;
    float eta = nv > 0 ? mat.IOR : 1.0 / mat.IOR;

    float nh = dot(n, h);
    float hv = dot(h, v);
    float hl = dot(h, l);

    // float Rs = (hv - eta * hl) / (hv + eta * hl);
    // float Rp = (eta * hv - hl) / (eta * hv + hl);
    // float Fg = (Rs * Rs + Rp * Rp) / 2.0;
    float Fg = DielectricFresnel(hv, eta);
    float Dg = GGXNDF(nh, alpha2);
    float Gg = SmithGGXMasking(n, v, alpha2) * SmithGGXMasking(n, l, alpha2);

    return nv * nl > 0
               ? mat.baseColor * Fg * Dg * Gg / (4.0 * abs(nv))
               : sqrt(mat.baseColor) * (1.0 - Fg) * Dg * Gg * abs(hv * hl) / (abs(nv) * pow(
                   hv + eta * hl, 2.0));

    // float Rs = (hv - eta * hl) / (hv + eta * hl);
    // float Rp = (eta * hv - hl) / (eta * hv + hl);
    // float Fg = (Rs * Rs + Rp * Rp) / 2.0;
    // float Dg = GGXNDF(nh, alpha2);
    // // float Gg = SmithGGXMasking(n, v, alpha2) * SmithGGXMasking(n, l, alpha2);
    // float G = SmithGGXMasking(n, l, alpha2);
    //
    // return nv * nl > 0
    //            ? mat.baseColor * G
    //            : sqrt(mat.baseColor) * G / (eta * eta);
}

float3 DisneyBSDF(float3 n, float3 v, float3 l, Material mat)
{
    float nv = dot(n, v);
    float nl = dot(n, l);

    float eta = nv > 0 ? mat.IOR : 1.0 / mat.IOR;
    float3 h = nv * nl > 0 ? normalize(l + v) : normalize(v + eta * l);
    if (dot(n, h) < 0)
        h = -h;
    // float nh = dot(n, h);
    // float hl = dot(h, l);

    // float alphag = max(mat.roughness * mat.roughness, 1e-4);
    // float alpha2 = alphag * alphag;

    float3 f_diffuse = 0;
    float3 f_metal = 0;
    float3 f_clearCoat = 0;
    float3 f_sheen = 0;

    if (nv > 0)
    {
        float luminance = Luminance(mat.baseColor);
        float3 Ctint = luminance > 0 ? mat.baseColor / luminance : 1;
        float R0 = pow((eta - 1.0) / (eta + 1.0), 2.0);

        f_diffuse = DisneyDiffuse(l, h, nl, nv, mat);
        f_metal = DisneyMetal(n, l, v, h, nv, mat, Ctint, R0);
        f_clearCoat = DisneyClearCoat(n, l, v, h, nv, mat, R0);
        f_sheen = DisneySheen(l, h, nl, mat, Ctint);
    }

    float3 f_glass = DisneyGlass(n, l, v, h, nl, nv, mat);

    float3 disney = (1.0 - mat.specularTransmission) * (1.0 - mat.metallic) * f_diffuse + (1.0 - mat.metallic) * mat.
        sheen * f_sheen + (1.0 - mat.specularTransmission * (1.0 - mat.metallic)) * f_metal + 0.25 * mat.clearcoat *
        f_clearCoat + (1.0 - mat.metallic) * mat.specularTransmission * f_glass;

    // disney *= abs(nl);
    disney = max(0, disney);

    return disney;
}
