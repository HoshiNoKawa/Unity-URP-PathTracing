using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class CombineMesh : Editor
{
    [MenuItem("Tools/Combine Meshes with Submeshes")]
    static void CombineSelectedMeshesWithSubmeshes()
    {
        // 获取选中的游戏对象
        GameObject selectedObject = Selection.activeGameObject;
        if (selectedObject == null)
        {
            Debug.LogError("Please select a GameObject.");
            return;
        }

        // 获取所有子对象的 MeshFilter 组件
        MeshFilter[] meshFilters = selectedObject.GetComponentsInChildren<MeshFilter>();
        if (meshFilters.Length == 0)
        {
            Debug.LogError("No MeshFilter found in the selected GameObject or its children.");
            return;
        }

        // 创建 CombineInstance 数组并填充
        CombineInstance[] combine = new CombineInstance[meshFilters.Length];
        Material[] materials = new Material[meshFilters.Length];
        for (int i = 0; i < meshFilters.Length; i++)
        {
            combine[i].mesh = meshFilters[i].sharedMesh;
            combine[i].transform = meshFilters[i].transform.localToWorldMatrix;
            materials[i] = meshFilters[i].GetComponent<Renderer>().sharedMaterial;
        }

        // 创建一个新的网格并合并子网格
        Mesh combinedMesh = new Mesh();
        combinedMesh.CombineMeshes(combine, false); // 第二个参数 false 表示保留子网格

        // 保存合并后的网格为资产
        string path = "Assets/CombinedMeshWithSubmeshes.asset";
        AssetDatabase.CreateAsset(combinedMesh, path);
        AssetDatabase.SaveAssets();

        // 创建一个新的 GameObject 来包含合并后的网格
        GameObject combinedObject = new GameObject("Combined Mesh with Submeshes");
        MeshFilter meshFilter = combinedObject.AddComponent<MeshFilter>();
        meshFilter.mesh = combinedMesh;

        // 添加 MeshRenderer 并设置材质
        MeshRenderer meshRenderer = combinedObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterials = materials;

        // 将合并后的对象放在场景中选定对象的位置
        combinedObject.transform.position = selectedObject.transform.position;

        Debug.Log("Meshes combined with submeshes and saved as " + path);
    }
}