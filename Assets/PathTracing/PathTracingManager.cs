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
    private ComputeBuffer _triangleBuffer;
    private ComputeBuffer _meshBuffer;
    private ComputeBuffer _nodeBuffer;

    private List<Sphere> _sphereList;
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
        ResetObjectCount();
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
    
    public void ResetObjectCount()
    {
        int sphereCount = 0, meshCount = 0;
        foreach (Transform child in transform)
        {
            PathTracingObject properties = child.GetComponent<PathTracingObject>();

            switch (properties.objectType)
            {
                case PathTracingObject.ObjectType.Sphere:
                {
                    sphereCount++;
                    break;
                }

                case PathTracingObject.ObjectType.Mesh:
                {
                    meshCount++;
                    break;
                }

                default:
                    break;
            }
        }

        rayTracingShader.SetKeyword(new LocalKeyword(rayTracingShader, "HAS_SPHERE"), sphereCount > 0);
        rayTracingShader.SetKeyword(new LocalKeyword(rayTracingShader, "HAS_MESH"), meshCount > 0);
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

                    if (properties.useTextureMapping)
                    {
                        rayTracingShader.SetTexture(0, "BaseMap", subMaterials[0].GetTexture("_BaseMap"));
                        rayTracingShader.SetTexture(0, "NormalMap", subMaterials[0].GetTexture("_BumpMap"));
                        rayTracingShader.SetTexture(0, "ARMMap", subMaterials[0].GetTexture("_MetallicGlossMap"));
                    }
                    // else
                    // {
                    //     rayTracingShader.SetTexture(0, "Tex0", CreateDefaultTexture(subMaterial.color));
                    // }

                    for (int i = 0; i < properties.subRootNode.Count; i++)
                    {
                        Material subMaterial = subMaterials[i];

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
                            TextureIndex = properties.useTextureMapping ? 1 : 0
                        };

                        MeshInfo meshInfo = new MeshInfo
                        {
                            RootNode = properties.subRootNode[i],
                            NodeOffset = _nodeList.Count,
                            TriangleOffset = _triangleList.Count + properties.subTriOffset[i],
                            WorldMatrix = worldMatrix,
                            NormalWorldMatrix = normalWorldMatrix,
                            Mat = meshMat
                        };

                        _meshList.Add(meshInfo);
                    }

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
            CreateStructuredBuffer(ref _triangleBuffer, _triangleList);
            CreateStructuredBuffer(ref _meshBuffer, _meshList);
            CreateStructuredBuffer(ref _nodeBuffer, _nodeList);

            rayTracingShader.SetBuffer(0, "TriangleBuffer", _triangleBuffer);
            rayTracingShader.SetBuffer(0, "MeshBuffer", _meshBuffer);
            rayTracingShader.SetBuffer(0, "BVHNodeBuffer", _nodeBuffer);

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