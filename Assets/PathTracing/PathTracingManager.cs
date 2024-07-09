using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

public class PathTracingManager : MonoBehaviour
{
    struct PathTracingMaterial
    {
        public Vector3 Albedo;
        public float Metallic;
        public float Roughness;
        public int EnableEmission;
        public Vector3 EmissionColor;
        public float SpecularTransmission;
        public float Subsurface;
        public float Specular;
        public float SpecularTint;
        public float Anisotropic;
        public float Sheen;
        public float SheenTint;
        public float Clearcoat;
        public float ClearcoatGloss;
        public float IOR;
    };

    struct Sphere
    {
        public Vector3 Center;
        public float Radius;
        public PathTracingMaterial Mat;
    };

    // struct Triangle
    // {
    //     public Vector3 vert0, vert1, vert2;
    //     public Vector3 normal0, normal1, normal2;
    // };

    struct MeshInfo
    {
        public int TriangleIndexBegin;
        public int TriangleIndexEnd;
        public int PreVertexCount;
        public int WithoutBV;
        public Vector3 AABBMin;
        public Vector3 AABBMax;
        public Matrix4x4 WorldMatrix;
        public Matrix4x4 NormalWorldMatrix;
        public PathTracingMaterial Mat;
    };

    public ComputeShader rayTracingShader;

    public bool useSkybox = true;
    public Color skyColor = new Color(0.6f, 0.7f, 0.9f);
    public bool useRussianRoulette = false;
    [UnityEngine.Range(0f, 0.95f)] public float russianRouletteProbability = 0.9f;
    [UnityEngine.Range(1, 20)] public int bouncesCount = 5;

    private ComputeBuffer _sphereBuffer;
    private ComputeBuffer _vertexBuffer;
    private ComputeBuffer _normalBuffer;
    private ComputeBuffer _indexBuffer;
    private ComputeBuffer _meshBuffer;

    private ComputeBuffer _sampleCountBuffer;

    private List<Sphere> _sphereList;
    private List<Vector3> _vertexList;
    private List<Vector3> _normalList;
    private List<int> _indexList;
    private List<MeshInfo> _meshList;

    private int _preVertexCount;

    private int _frameCount;


    // Start is called before the first frame update
    void Start()
    {
        _sphereList = new List<Sphere>();
        _vertexList = new List<Vector3>();
        _normalList = new List<Vector3>();
        _indexList = new List<int>();
        _meshList = new List<MeshInfo>();

        _frameCount = 0;

        rayTracingShader.SetTexture(0, "_SkyBoxTexture", RenderSettings.skybox.GetTexture("_Tex"));

        ResetShaderProperties();
        ResetScene();
    }

    // Update is called once per frame
    void Update()
    {
        rayTracingShader.SetInt("_frameCount", Time.frameCount - _frameCount);
        ResetScene();
    }

    private void OnValidate()
    {
        // ResetShaderProperties();
        // ResetAccumulation();
    }

    private void OnDestroy()
    {
        _sphereBuffer?.Release();
        _vertexBuffer?.Release();
        _normalBuffer?.Release();
        _indexBuffer?.Release();
        _meshBuffer?.Release();
        _sampleCountBuffer?.Release();
    }

    public void ResetAccumulation()
    {
        _frameCount = Time.frameCount;
    }

    private Vector3 ColorToVector(Color color)
    {
        return new Vector3(color.r, color.g, color.b);
    }

    public void ResetShaderProperties()
    {
        rayTracingShader.SetKeyword(new LocalKeyword(rayTracingShader, "USE_SKYBOX"), useSkybox);
        rayTracingShader.SetVector("_skyColor", ColorToVector(skyColor));
        rayTracingShader.SetKeyword(new LocalKeyword(rayTracingShader, "USE_RR"), useRussianRoulette);
        rayTracingShader.SetFloat("_RR", russianRouletteProbability);
        rayTracingShader.SetInt("_bouncesCount", bouncesCount);
        
        if (_sampleCountBuffer == null || !_sampleCountBuffer.IsValid() || _sampleCountBuffer.count != 2 || _sampleCountBuffer.stride != sizeof(int))
        {
            if (_sampleCountBuffer != null)
                _sampleCountBuffer.Release();

            _sampleCountBuffer = new ComputeBuffer(2, sizeof(int));
            _sampleCountBuffer.SetData(new int[2] { 0, 0 });
        }
        //
        rayTracingShader.SetBuffer(0, "SampleCountBuffer", _sampleCountBuffer);
    }

    public void ResetScene()
    {
        _preVertexCount = 0;

        foreach (Transform child in transform)
        {
            PathTracingObject properties = child.GetComponent<PathTracingObject>();

            switch (properties.objectType)
            {
                case PathTracingObject.ObjectType.Sphere:
                {
                    Material material = child.GetComponent<MeshRenderer>().sharedMaterial;

                    PathTracingMaterial sphereMat = new PathTracingMaterial()
                    {
                        Albedo = ColorToVector(material.color),
                        Metallic = material.GetFloat("_Metallic"),
                        Roughness = 1f - material.GetFloat("_Smoothness"),
                        EnableEmission = material.IsKeywordEnabled("_EMISSION") ? 1 : 0,
                        EmissionColor = ColorToVector(material.GetColor("_EmissionColor")),
                        SpecularTransmission = properties.specularTransmission,
                        Subsurface = properties.subsurface,
                        Specular = properties.specular,
                        SpecularTint = properties.specularTint,
                        Anisotropic = properties.anisotropic,
                        Sheen = properties.sheen,
                        SheenTint = properties.sheenTint,
                        Clearcoat = properties.clearcoat,
                        ClearcoatGloss = properties.clearcoatGloss,
                        IOR = properties.indexOfRefraction
                    };
                    Sphere sphere = new Sphere()
                    {
                        Center = child.position,
                        Radius = child.localScale.x * 0.5f,
                        Mat = sphereMat
                    };

                    _sphereList.Add(sphere);

                    break;
                }

                case PathTracingObject.ObjectType.Mesh:
                {
                    Mesh mesh = child.GetComponent<MeshFilter>().sharedMesh;
                    Material[] subMaterials = child.GetComponent<MeshRenderer>().sharedMaterials;
                    Matrix4x4 worldMatrix = child.localToWorldMatrix;
                    Matrix4x4 normalWorldMatrix = child.localToWorldMatrix.inverse.transpose;

                    int indicesCount = _indexList.Count;

                    for (int i = 0; i < mesh.subMeshCount; i++)
                    {
                        SubMeshDescriptor subMesh = mesh.GetSubMesh(i);
                        Material subMaterial = subMaterials[i];

                        PathTracingMaterial meshMat = new PathTracingMaterial()
                        {
                            Albedo = ColorToVector(subMaterial.color),
                            Metallic = subMaterial.GetFloat("_Metallic"),
                            Roughness = 1f - subMaterial.GetFloat("_Smoothness"),
                            EnableEmission = subMaterial.IsKeywordEnabled("_EMISSION") ? 1 : 0,
                            EmissionColor = ColorToVector(subMaterial.GetColor("_EmissionColor")),
                            SpecularTransmission = properties.specularTransmission,
                            Subsurface = properties.subsurface,
                            Specular = properties.specular,
                            SpecularTint = properties.specularTint,
                            Anisotropic = properties.anisotropic,
                            Sheen = properties.sheen,
                            SheenTint = properties.sheenTint,
                            Clearcoat = properties.clearcoat,
                            ClearcoatGloss = properties.clearcoatGloss,
                            IOR = properties.indexOfRefraction
                        };

                        var subMeshBounds = subMesh.bounds;

                        MeshInfo meshInfo = new MeshInfo()
                        {
                            TriangleIndexBegin = indicesCount + subMesh.indexStart,
                            TriangleIndexEnd = indicesCount + subMesh.indexStart + subMesh.indexCount,
                            PreVertexCount = _preVertexCount,
                            WithoutBV = (subMeshBounds.size.x == 0f || subMeshBounds.size.y == 0f ||
                                         subMeshBounds.size.z == 0f)
                                ? 1
                                : 0,
                            AABBMin = subMeshBounds.min,
                            AABBMax = subMeshBounds.max,
                            WorldMatrix = worldMatrix,
                            NormalWorldMatrix = normalWorldMatrix,
                            Mat = meshMat
                        };

                        _meshList.Add(meshInfo);
                    }

                    _vertexList.AddRange(mesh.vertices);
                    _normalList.AddRange(mesh.normals);
                    _indexList.AddRange(mesh.triangles);

                    _preVertexCount += mesh.vertexCount;

                    break;
                }

                default:
                    break;
            }
        }

        if (_sphereList.Any())
        {
            CreateStructuredBuffer(ref _sphereBuffer, _sphereList);
            rayTracingShader.SetBuffer(0, "SphereBuffer", _sphereBuffer);

            _sphereList.Clear();
        }

        if (_meshList.Any())
        {
            CreateStructuredBuffer(ref _vertexBuffer, _vertexList);
            CreateStructuredBuffer(ref _normalBuffer, _normalList);
            CreateStructuredBuffer(ref _indexBuffer, _indexList);
            CreateStructuredBuffer(ref _meshBuffer, _meshList);

            rayTracingShader.SetBuffer(0, "VertexBuffer", _vertexBuffer);
            rayTracingShader.SetBuffer(0, "NormalBuffer", _normalBuffer);
            rayTracingShader.SetBuffer(0, "IndexBuffer", _indexBuffer);
            rayTracingShader.SetBuffer(0, "MeshBuffer", _meshBuffer);

            _vertexList.Clear();
            _normalList.Clear();
            _indexList.Clear();
            _meshList.Clear();
        }
    }

    private void CreateStructuredBuffer<T>(ref ComputeBuffer buffer, List<T> data) where T : struct
    {
        if (data.Count == 0)
            return;

        int length = data.Count;

        int stride = Marshal.SizeOf(typeof(T));

        if (buffer == null || !buffer.IsValid() || buffer.count != length || buffer.stride != stride)
        {
            if (buffer != null)
                buffer.Release();

            buffer = new ComputeBuffer(length, stride, ComputeBufferType.Structured);
        }

        buffer.SetData(data);
    }
}

#region oldCode

/*
{
    PathTracingMaterial meshMat = new PathTracingMaterial()
    {
        Albedo = ColorToVector(objectAttributes.albedo),
        Metallic = objectAttributes.metallic,
        Roughness = objectAttributes.roughness,
        Emission = objectAttributes.emission
    };

    MeshInfo meshInfo = new MeshInfo()
    {
        firstTriangleIndex = _triangleList.Count,
        mat = meshMat,
        worldMatrix = child.localToWorldMatrix,
        normalWorldMatrix = child.localToWorldMatrix.inverse.transpose
    };

    MeshFilter meshFilter = child.GetComponent<MeshFilter>();
    Mesh mesh = meshFilter.sharedMesh;

    Vector3[] verts = mesh.vertices;
    Vector3[] normals = mesh.normals;
    int[] indices = mesh.triangles;

    Vector3 AABBMin = Vector3.positiveInfinity;
    Vector3 AABBMax = Vector3.negativeInfinity;

    // Matrix4x4 worldMatrix = child.localToWorldMatrix;
    // Matrix4x4 normalWorldMatrix = worldMatrix.inverse.transpose;

    for (int i = 0; i < indices.Length; i += 3)
    {
        int a = indices[i];
        int b = indices[i + 1];
        int c = indices[i + 2];

        Vector3 vert0 = verts[a];
        Vector3 vert1 = verts[b];
        Vector3 vert2 = verts[c];

        // Vector3 vert0 = worldMatrix * new Vector4(verts[a].x, verts[a].y, verts[a].z, 1f);
        // Vector3 vert1 = worldMatrix * new Vector4(verts[b].x, verts[b].y, verts[b].z, 1f);
        // Vector3 vert2 = worldMatrix * new Vector4(verts[c].x, verts[c].y, verts[c].z, 1f);

        AABBMin.x = Mathf.Min(AABBMin.x, vert0.x, vert1.x, vert2.x);
        AABBMin.y = Mathf.Min(AABBMin.y, vert0.y, vert1.y, vert2.y);
        AABBMin.z = Mathf.Min(AABBMin.z, vert0.z, vert1.z, vert2.z);

        AABBMax.x = Mathf.Max(AABBMax.x, vert0.x, vert1.x, vert2.x);
        AABBMax.y = Mathf.Max(AABBMax.y, vert0.y, vert1.y, vert2.y);
        AABBMax.z = Mathf.Max(AABBMax.z, vert0.z, vert1.z, vert2.z);

        Vector3 normal0 = normals[a];
        Vector3 normal1 = normals[b];
        Vector3 normal2 = normals[c];

        Triangle triangle = new Triangle()
        {
            vert0 = vert0, vert1 = vert1, vert2 = vert2, normal0 = normal0, normal1 = normal1,
            normal2 = normal2
        };

        _triangleList.Add(triangle);
    }

    meshInfo.numTriangles = _triangleList.Count - meshInfo.firstTriangleIndex;
    meshInfo.AABBMin = mesh.bounds.min;
    meshInfo.AABBMax = mesh.bounds.max;

    _meshList.Add(meshInfo);

    break;
}
*/

#endregion