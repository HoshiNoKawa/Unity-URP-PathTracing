using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PathTracingObject))]
public class PathTracingSphereEditor : Editor
{
    public override void OnInspectorGUI()
    {
        PathTracingObject pathTracingObject = (PathTracingObject)target;

        DrawDefaultInspector();

        if (GUILayout.Button("Sync with Material"))
        {
            // pathTracingObject.SyncWithMaterial();
            pathTracingObject.OnMaterialChanged();
        }
    }
}