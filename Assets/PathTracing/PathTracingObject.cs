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
    [Space] [Range(0f, 1f)] public float specularTransmission = 0.5f;
    [Range(1f, 2f)] public float indexOfRefraction = 1.5f;
    [Space] [Range(0f, 1f)] public float subsurface = 0.5f;
    [Space] [Range(0f, 1f)] public float specular = 0.5f;
    [Range(0f, 1f)] public float specularTint = 0f;
    [Space] [Range(0f, 1f)] public float anisotropic = 0f;
    [Space] [Range(0f, 1f)] public float sheen = 0.5f;
    [Range(0f, 1f)] public float sheenTint = 0f;
    [Space] [Range(0f, 1f)] public float clearcoat = 0.5f;
    [Range(0f, 1f)] public float clearcoatGloss = 0.5f;

    private PathTracingManager _pathTracingManager;
    private MeshRenderer _objectRenderer;
    private MeshFilter _objectMesh;

    private Vector3[] vertices;

    // [HideInInspector] public List<int> indices;
    [HideInInspector] public List<int> subRootNode;
    [HideInInspector] public List<int> subTriOffset;
    [HideInInspector] public List<BVHNode> nodeList;
    private List<MeshTriangle> subTriangleList;
    [HideInInspector] public List<MeshTriangle> triangleList;
    private List<BVHTriangleInfo> subTriInfoList;
    // [HideInInspector] public List<MeshTriangle> sortedTriangleList;

    private const int MaxDepth = 32;

    private void Start()
    {
        _pathTracingManager = transform.parent.GetComponent<PathTracingManager>();
        _objectRenderer = GetComponent<MeshRenderer>();
        _objectMesh = GetComponent<MeshFilter>();
        // BuildBVH();
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
        nodeList = new List<BVHNode>();
        subRootNode = new List<int>();
        subTriOffset = new List<int>();

        Mesh mesh = GetObjectMesh().sharedMesh;
        vertices = mesh.vertices;
        // normals = mesh.normals;

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
                    UVC = mesh.uv[indexC]
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

    void Split(int parentIndex, int triStart, int triCount, int depth = 0)
    {
        BVHNode parent = nodeList[parentIndex];
        Vector3 size = parent.AABBMax - parent.AABBMin;
        float parentCost = NodeCost(size, triCount);

        (int splitAxis, float splitPos, float cost) = ChooseSplit(parent, triStart, triCount);

        if (cost < parentCost && depth < MaxDepth)
        {
            Bounds boundsLeft = new Bounds();
            bool leftCreated = false;
            Bounds boundsRight = new Bounds();
            bool rightCreated = false;
            int numOnLeft = 0;

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

                    (subTriInfoList[triStart + numOnLeft], subTriInfoList[i]) =
                        (subTriInfoList[i], subTriInfoList[triStart + numOnLeft]);

                    (subTriangleList[triStart + numOnLeft], subTriangleList[i]) = (subTriangleList[i],
                        subTriangleList[triStart + numOnLeft]);

                    numOnLeft++;
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

            parent.LeftChild = nodeList.Count;
            nodeList.Add(leftChildNode);
            nodeList.Add(rightChildNode);

            nodeList[parentIndex] = parent;

            Split(parent.LeftChild, triStart, numOnLeft, depth + 1);
            Split(parent.LeftChild + 1, triStart + numOnLeft, triCount - numOnLeft, depth + 1);
        }
        else
        {
            parent.TriangleStart = triStart;
            parent.LeftChild = -triCount;
            nodeList[parentIndex] = parent;
        }
    }

    (int axis, float pos, float cost) ChooseSplit(BVHNode node, int triStart, int triCount)
    {
        if (triCount <= 1)
            return (0, 0, float.PositiveInfinity);

        float bestSplitPos = 0;
        int bestSplitAxis = 0;
        const int numSplitTests = 5;

        float bestCost = float.MaxValue;

        // Estimate best split pos
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
        int numOnLeft = 0;
        int numOnRight = 0;

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

                numOnLeft++;
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

                numOnRight++;
            }
        }

        float costA = NodeCost(boundsLeft.size, numOnLeft);
        float costB = NodeCost(boundsRight.size, numOnRight);
        return costA + costB;
    }

    private float NodeCost(Vector3 size, int numTriangles)
    {
        float halfArea = size.x * size.y + size.y * size.z + size.z * size.x;
        if (halfArea == 0)
        {
            return float.PositiveInfinity;
        }

        return halfArea * numTriangles;
    }
}