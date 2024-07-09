using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class PathTracingObject : MonoBehaviour
{
    public enum ObjectType
    {
        Sphere,
        Mesh
    }

    public ObjectType objectType = ObjectType.Mesh;
    [Range(0f, 1f)] public float specularTransmission = 0.5f;
    [Range(0f, 1f)] public float subsurface = 0.5f;
    [Range(0f, 1f)] public float specular = 0.5f;
    [Range(0f, 1f)] public float specularTint = 0f;
    [Range(0f, 1f)] public float anisotropic = 0.5f;
    [Range(0f, 1f)] public float sheen = 0.5f;
    [Range(0f, 1f)] public float sheenTint = 0f;
    [Range(0f, 1f)] public float clearcoat = 0.5f;
    [Range(0f, 1f)] public float clearcoatGloss = 0.5f;
    [Range(1f, 2f)] public float indexOfRefraction = 1.5f;
    
    [SerializeField, HideInInspector] int materialObjectID;

    private PathTracingManager _pathTracingManager;
    private MeshRenderer _objectRenderer;

    private void Start()
    {
        _pathTracingManager = transform.parent.GetComponent<PathTracingManager>();
        _objectRenderer = GetComponent<MeshRenderer>();
    }

    private void OnValidate()
    {
        // SyncToMaterial();
        // OnMaterialChanged();
    }

    public void OnMaterialChanged()
    {
        if (_pathTracingManager)
        {
            _pathTracingManager.ResetScene();
            _pathTracingManager.ResetAccumulation();
        }
    }
}