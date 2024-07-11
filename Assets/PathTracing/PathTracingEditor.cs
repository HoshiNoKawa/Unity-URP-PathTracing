using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PathTracingManager))]
public class PathTracingManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        PathTracingManager pathTracingManager = (PathTracingManager)target;

        DrawDefaultInspector();
        
        EditorGUILayout.Space();

        if (GUILayout.Button("Reset Accumulation"))
        {
            pathTracingManager.ResetAccumulation();
        }
    }
}