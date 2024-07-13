using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

public class PathTracingManager : MonoBehaviour
{
    struct PathTracingMaterial
    {
        public Vector3 Albedo;
        public Vector3 EmissionColor;
        public float Metallic;
        public float Roughness;
        public int EnableEmission;
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
        public int TextureIndex;
    };

    struct Sphere
    {
        public PathTracingMaterial Mat;
        public Vector3 Center;
        public float Radius;
    };

    struct MeshInfo
    {
        public PathTracingMaterial Mat;
        public Matrix4x4 WorldMatrix;
        public Matrix4x4 NormalWorldMatrix;
        public int RootNode;
        public int NodeOffset;
        public int TriangleOffset;
    };

    public ComputeShader rayTracingShader;

    public static bool EnablePathTracing = true;
    public bool enablePathTracing = true;

    [Header("Sky Settings")] public bool useSkybox = true;
    public Color skyColor = new Color(0.6f, 0.7f, 0.9f);

    [Header("Stop Condition")] [UnityEngine.Range(1, 20)]
    public int maxBouncesCount = 5;

    public bool useRussianRoulette = false;
    [UnityEngine.Range(0f, 0.95f)] public float russianRouletteProbability = 0.8f;

    [Header("Camera Control")] public bool useCameraController = true;
    public float moveSpeed = 5.0f;
    public float rotationSpeed = 3.0f;

    private ComputeBuffer _sphereBuffer;

    // private ComputeBuffer _vertexBuffer;
    // private ComputeBuffer _normalBuffer;
    // private ComputeBuffer _indexBuffer;
    private ComputeBuffer _triangleBuffer;
    private ComputeBuffer _meshBuffer;
    private ComputeBuffer _nodeBuffer;

    private List<Sphere> _sphereList;

    // private List<Vector3> _vertexList;
    // private List<Vector3> _normalList;
    // private List<int> _indexList;
    private List<PathTracingObject.MeshTriangle> _triangleList;
    private List<MeshInfo> _meshList;
    private List<PathTracingObject.BVHNode> _nodeList;

    private int _frameCount;

    private Transform _mainCamera;

    private float _rotationX = 0.0f;
    private float _rotationY = 0.0f;

    void Start()
    {
        _sphereList = new List<Sphere>();
        // _vertexList = new List<Vector3>();
        // _normalList = new List<Vector3>();
        // _indexList = new List<int>();
        _triangleList = new List<PathTracingObject.MeshTriangle>();
        _meshList = new List<MeshInfo>();
        _nodeList = new List<PathTracingObject.BVHNode>();

        _frameCount = 0;

        if (Camera.main != null)
        {
            _mainCamera = Camera.main.transform;
            Vector3 angles = transform.eulerAngles;
            _rotationX = angles.y;
            _rotationY = angles.x;
        }

        rayTracingShader.SetTexture(0, "_SkyBoxTexture", RenderSettings.skybox.GetTexture("_Tex"));

        ResetShaderProperties();
        ResetScene();
    }

    void Update()
    {
        rayTracingShader.SetInt("_frameCount", Time.frameCount - _frameCount);
        // ResetScene();
        if (useCameraController)
            ControlCamera();
    }

    private void OnValidate()
    {
        EnablePathTracing = enablePathTracing;
        ResetShaderProperties();
        ResetAccumulation();
    }

    private void OnDestroy()
    {
        _sphereBuffer?.Release();
        // _vertexBuffer?.Release();
        // _normalBuffer?.Release();
        // _indexBuffer?.Release();
        _triangleBuffer?.Release();
        _meshBuffer?.Release();
        _nodeBuffer?.Release();
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
        rayTracingShader.SetInt("_bouncesCount", maxBouncesCount);
    }

    public void ResetScene()
    {
        foreach (Transform child in transform)
        {
            PathTracingObject properties = child.GetComponent<PathTracingObject>();

            switch (properties.objectType)
            {
                case PathTracingObject.ObjectType.Sphere:
                {
                    Material material = properties.GetObjectRenderer().sharedMaterial;

                    PathTracingMaterial sphereMat = new PathTracingMaterial
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

                    Sphere sphere = new Sphere
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
                    properties.BuildBVH();
                    Material[] subMaterials = properties.GetObjectRenderer().sharedMaterials;


                    Matrix4x4 worldMatrix = child.localToWorldMatrix;
                    Matrix4x4 normalWorldMatrix = child.localToWorldMatrix.inverse.transpose;

                    for (int i = 0; i < properties.subRootNode.Count; i++)
                    {
                        Material subMaterial = subMaterials[i];

                        Texture baseMap = subMaterial.GetTexture("_BaseMap");
                        if (baseMap)
                        {
                            rayTracingShader.SetTexture(0, "Tex0", baseMap);
                        }
                        // else
                        // {
                        //     rayTracingShader.SetTexture(0, "Tex0", CreateDefaultTexture(subMaterial.color));
                        // }


                        PathTracingMaterial meshMat = new PathTracingMaterial
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
                            IOR = properties.indexOfRefraction,
                            TextureIndex = baseMap == null ? 0 : 1
                        };

                        MeshInfo meshInfo = new MeshInfo
                        {
                            RootNode = properties.subRootNode[i],
                            NodeOffset = _nodeList.Count,
                            TriangleOffset = _triangleList.Count + properties.subTriOffset[i],
                            // IndexOffset = _indexList.Count,
                            // VertexOffset = _vertexList.Count,
                            WorldMatrix = worldMatrix,
                            NormalWorldMatrix = normalWorldMatrix,
                            Mat = meshMat
                        };

                        _meshList.Add(meshInfo);
                    }

                    // _vertexList.AddRange(properties.vertices);
                    // _normalList.AddRange(properties.normals);
                    // _indexList.AddRange(properties.indices);
                    _triangleList.AddRange(properties.triangleList);
                    _nodeList.AddRange(properties.nodeList);

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
            // CreateStructuredBuffer(ref _vertexBuffer, _vertexList);
            // CreateStructuredBuffer(ref _normalBuffer, _normalList);
            // CreateStructuredBuffer(ref _indexBuffer, _indexList);
            CreateStructuredBuffer(ref _triangleBuffer, _triangleList);
            CreateStructuredBuffer(ref _meshBuffer, _meshList);
            CreateStructuredBuffer(ref _nodeBuffer, _nodeList);

            // rayTracingShader.SetBuffer(0, "VertexBuffer", _vertexBuffer);
            // rayTracingShader.SetBuffer(0, "NormalBuffer", _normalBuffer);
            // rayTracingShader.SetBuffer(0, "IndexBuffer", _indexBuffer);
            rayTracingShader.SetBuffer(0, "TriangleBuffer", _triangleBuffer);
            rayTracingShader.SetBuffer(0, "MeshBuffer", _meshBuffer);
            rayTracingShader.SetBuffer(0, "BVHNodeBuffer", _nodeBuffer);

            // _vertexList.Clear();
            // _normalList.Clear();
            // _indexList.Clear();
            _triangleList.Clear();
            _meshList.Clear();
            _nodeList.Clear();
        }
    }

    private void ControlCamera()
    {
        if (Input.GetMouseButton(1))
        {
            _rotationX += Input.GetAxis("Mouse X") * rotationSpeed;
            _rotationY -= Input.GetAxis("Mouse Y") * rotationSpeed;
            _rotationY = Mathf.Clamp(_rotationY, -89.9f, 89.9f);

            Quaternion rotation = Quaternion.Euler(_rotationY, _rotationX, 0);
            if (_mainCamera.rotation != rotation)
                ResetAccumulation();
            _mainCamera.rotation = rotation;
        }

        float moveForward = Input.GetAxis("Vertical") * moveSpeed * Time.deltaTime;
        float moveRight = Input.GetAxis("Horizontal") * moveSpeed * Time.deltaTime;
        float moveUp = 0;

        if (Input.GetKey(KeyCode.E))
        {
            moveUp = moveSpeed * 0.8f * Time.deltaTime;
        }

        if (Input.GetKey(KeyCode.Q))
        {
            moveUp = -moveSpeed * 0.8f * Time.deltaTime;
        }

        Vector3 moveDirection =
            (_mainCamera.forward * moveForward) + (_mainCamera.right * moveRight) + (_mainCamera.up * moveUp);
        _mainCamera.position += moveDirection;

        if (moveDirection.magnitude > 0)
            ResetAccumulation();
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

    private Texture CreateDefaultTexture(Color color)
    {
        Texture2D defaultTexture = new Texture2D(1, 1);
        defaultTexture.SetPixel(0, 0, color);
        defaultTexture.Apply();
        return defaultTexture;
    }
}

#region deprecatedCode

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