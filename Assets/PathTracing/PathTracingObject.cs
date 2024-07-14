using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

public class PathTracingObject : MonoBehaviour
{
    public enum ObjectType
    {
        Sphere,
        Mesh
    }

    public ObjectType objectType = ObjectType.Mesh;
    [Space] [Range(0f, 1f)] public float specularTransmission = 0f;
    [Range(1f, 2f)] public float indexOfRefraction = 1.5f;
    [Space] [Range(0f, 1f)] public float subsurface = 0f;
    [Space] [Range(0f, 1f)] public float specular = 0.5f;
    [Range(0f, 1f)] public float specularTint = 0f;
    [Space] [Range(0f, 1f)] public float anisotropic = 0f;
    [Space] [Range(0f, 1f)] public float sheen = 0f;
    [Range(0f, 1f)] public float sheenTint = 0f;
    [Space] [Range(0f, 1f)] public float clearcoat = 0f;
    [Range(0f, 1f)] public float clearcoatGloss = 0f;

    [Space] public bool useTextureMapping = false;

    private PathTracingManager _pathTracingManager;
    private MeshRenderer _objectRenderer;
    private MeshFilter _objectMesh;

    private Vector3[] vertices;

    [HideInInspector] public List<int> subRootNode;
    [HideInInspector] public List<int> subTriOffset;
    [HideInInspector] public List<BVHNode> nodeList;
    private List<MeshTriangle> subTriangleList;
    [HideInInspector] public List<MeshTriangle> triangleList;
    private List<BVHTriangleInfo> subTriInfoList;

    private bool _hasBuiltedBvh = false;

    private const int MaxDepth = 32;

    private void Start()
    {
        _pathTracingManager = transform.parent.GetComponent<PathTracingManager>();
        _objectRenderer = GetComponent<MeshRenderer>();
        _objectMesh = GetComponent<MeshFilter>();
    }

    private void OnValidate()
    {
        if (_pathTracingManager)
        {
            _pathTracingManager.ResetScene();
            _pathTracingManager.ResetAccumulation();
        }
    }

    public MeshRenderer GetObjectRenderer()
    {
        if (_objectRenderer)
            return _objectRenderer;
        _objectRenderer = GetComponent<MeshRenderer>();
        return _objectRenderer;
    }

    public MeshFilter GetObjectMesh()
    {
        if (_objectMesh)
            return _objectMesh;
        _objectMesh = GetComponent<MeshFilter>();
        return _objectMesh;
    }

    public struct BVHNode
    {
        public Vector3 AABBMin;
        public Vector3 AABBMax;
        public int LeftChild;
        public int TriangleStart;
    }

    public struct MeshTriangle
    {
        public Vector4 TangentA, TangentB, TangentC;
        public Vector3 VertexA, VertexB, VertexC;
        public Vector3 NormalA, NormalB, NormalC;
        public Vector2 UVA, UVB, UVC;
    };

    struct BVHTriangleInfo
    {
        public Bounds AABB;
        public Vector3 center;
    };

    public void BuildBVH()
    {
        if (!_hasBuiltedBvh)
        {
            InternalBuildBVH();
            _hasBuiltedBvh = true;
        }
    }

    private void InternalBuildBVH()
    {
        nodeList = new List<BVHNode>();
        subRootNode = new List<int>();
        subTriOffset = new List<int>();

        Mesh mesh = GetObjectMesh().sharedMesh;
        vertices = mesh.vertices;

        List<int> indexList = new List<int>();
        indexList.AddRange(mesh.triangles);

        subTriangleList = new List<MeshTriangle>();
        subTriInfoList = new List<BVHTriangleInfo>();

        triangleList = new List<MeshTriangle>();

        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            subTriangleList.Clear();
            subTriInfoList.Clear();

            subTriOffset.Add(triangleList.Count);

            SubMeshDescriptor subMesh = mesh.GetSubMesh(i);

            List<int> subIndices = indexList.GetRange(subMesh.indexStart, subMesh.indexCount);

            for (int j = 0; j < subIndices.Count; j += 3)
            {
                int indexA = subIndices[j];
                int indexB = subIndices[j + 1];
                int indexC = subIndices[j + 2];

                Vector3 vertexA = vertices[indexA];
                Vector3 vertexB = vertices[indexB];
                Vector3 vertexC = vertices[indexC];

                Bounds aabb = new Bounds();

                aabb.SetMinMax(vertexA, vertexA);
                aabb.Encapsulate(vertexB);
                aabb.Encapsulate(vertexC);

                BVHTriangleInfo triInfo = new BVHTriangleInfo
                {
                    center = (vertexA + vertexB + vertexC) / 3,
                    AABB = aabb
                };

                subTriInfoList.Add(triInfo);

                MeshTriangle tri = new MeshTriangle
                {
                    VertexA = vertexA,
                    VertexB = vertexB,
                    VertexC = vertexC,
                    NormalA = mesh.normals[indexA],
                    NormalB = mesh.normals[indexB],
                    NormalC = mesh.normals[indexC],
                    UVA = mesh.uv[indexA],
                    UVB = mesh.uv[indexB],
                    UVC = mesh.uv[indexC],
                    TangentA = mesh.tangents[indexA],
                    TangentB = mesh.tangents[indexB],
                    TangentC = mesh.tangents[indexC]
                };

                subTriangleList.Add(tri);
            }

            BVHNode node = new BVHNode
            {
                AABBMin = subMesh.bounds.min,
                AABBMax = subMesh.bounds.max
            };

            subRootNode.Add(nodeList.Count);
            nodeList.Add(node);
            Split(nodeList.Count - 1, 0, subTriangleList.Count);

            triangleList.AddRange(subTriangleList);
        }
    }

    private void Split(int nodeIndex, int triStart, int triCount, int depth = 0)
    {
        BVHNode node = nodeList[nodeIndex];
        Vector3 size = node.AABBMax - node.AABBMin;
        float parentCost = NodeCost(size, triCount);

        (int splitAxis, float splitPos, float cost) = ChooseSplit(node, triStart, triCount);

        if (cost < parentCost && depth < MaxDepth)
        {
            Bounds boundsLeft = new Bounds();
            bool leftCreated = false;
            Bounds boundsRight = new Bounds();
            bool rightCreated = false;
            int leftCount = 0;

            for (int i = triStart; i < triStart + triCount; i++)
            {
                BVHTriangleInfo triInfo = subTriInfoList[i];

                if (triInfo.center[splitAxis] < splitPos)
                {
                    if (leftCreated)
                        boundsLeft.Encapsulate(triInfo.AABB);
                    else
                    {
                        boundsLeft = triInfo.AABB;
                        leftCreated = true;
                    }

                    (subTriInfoList[triStart + leftCount], subTriInfoList[i]) =
                        (subTriInfoList[i], subTriInfoList[triStart + leftCount]);

                    (subTriangleList[triStart + leftCount], subTriangleList[i]) = (subTriangleList[i],
                        subTriangleList[triStart + leftCount]);

                    leftCount++;
                }
                else
                {
                    if (rightCreated)
                        boundsRight.Encapsulate(triInfo.AABB);
                    else
                    {
                        boundsRight = triInfo.AABB;
                        rightCreated = true;
                    }
                }
            }

            BVHNode leftChildNode = new BVHNode
            {
                AABBMin = boundsLeft.min,
                AABBMax = boundsLeft.max
            };
            BVHNode rightChildNode = new BVHNode
            {
                AABBMin = boundsRight.min,
                AABBMax = boundsRight.max
            };

            node.LeftChild = nodeList.Count;
            nodeList.Add(leftChildNode);
            nodeList.Add(rightChildNode);

            nodeList[nodeIndex] = node;

            Split(node.LeftChild, triStart, leftCount, depth + 1);
            Split(node.LeftChild + 1, triStart + leftCount, triCount - leftCount, depth + 1);
        }
        else
        {
            node.TriangleStart = triStart;
            node.LeftChild = -triCount;
            nodeList[nodeIndex] = node;
        }
    }

    private (int axis, float pos, float cost) ChooseSplit(BVHNode node, int triStart, int triCount)
    {
        if (triCount <= 1)
            return (0, 0, float.PositiveInfinity);

        float bestSplitPos = 0;
        int bestSplitAxis = 0;
        const int numSplitTests = 5;

        float bestCost = float.MaxValue;

        for (int axis = 0; axis < 3; axis++)
        {
            for (int i = 0; i < numSplitTests; i++)
            {
                float splitT = (i + 1) / (numSplitTests + 1f);
                float splitPos = Mathf.Lerp(node.AABBMin[axis], node.AABBMax[axis], splitT);
                float cost = EvaluateSplit(axis, splitPos, triStart, triCount);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestSplitPos = splitPos;
                    bestSplitAxis = axis;
                }
            }
        }

        return (bestSplitAxis, bestSplitPos, bestCost);
    }

    private float EvaluateSplit(int splitAxis, float splitPos, int triStart, int triCount)
    {
        Bounds boundsLeft = new Bounds();
        bool leftCreated = false;
        Bounds boundsRight = new Bounds();
        bool rightCreated = false;
        int leftCount = 0;
        int rightCount = 0;

        for (int i = triStart; i < triStart + triCount; i++)
        {
            BVHTriangleInfo triInfo = subTriInfoList[i];

            if (triInfo.center[splitAxis] < splitPos)
            {
                if (leftCreated)
                    boundsLeft.Encapsulate(triInfo.AABB);
                else
                {
                    boundsLeft = triInfo.AABB;
                    leftCreated = true;
                }

                leftCount++;
            }
            else
            {
                if (rightCreated)
                    boundsRight.Encapsulate(triInfo.AABB);
                else
                {
                    boundsRight = triInfo.AABB;
                    rightCreated = true;
                }

                rightCount++;
            }
        }

        float leftCost = NodeCost(boundsLeft.size, leftCount);
        float rightCost = NodeCost(boundsRight.size, rightCount);
        return leftCost + rightCost;
    }

    private float NodeCost(Vector3 boundSize, int triCount)
    {
        float halfSurfArea = boundSize.x * boundSize.y + boundSize.y * boundSize.z + boundSize.z * boundSize.x;
        
        if (halfSurfArea == 0)
            return float.PositiveInfinity;

        return halfSurfArea * triCount;
    }
}