using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PathTracing : ScriptableRendererFeature
{
    class PathTracingPass : ScriptableRenderPass
    {
        private ComputeShader _rayTracingShader;
        private RTHandle _renderTarget;

        private RenderTextureDescriptor _renderTargetDescriptor;

        // private Transform _pathTracingObjectNode;
        // private List<Sphere> _sphereList;
        // private ComputeBuffer _sphereBuffer;

        public PathTracingPass(ComputeShader rayTracingShader)
        {
            _rayTracingShader = rayTracingShader;
            // _pathTracingObjectNode = GameObject.Find("Path Tracing Object").transform;

            // _sphereBuffer = new ComputeBuffer(_pathTracingObjectNode.childCount, Marshal.SizeOf(typeof(Sphere)));

            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

            _renderTargetDescriptor = new RenderTextureDescriptor(Screen.width,
                Screen.height, RenderTextureFormat.Default, 0);

            // _sphereList = new List<Sphere>();
        }


        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            CameraData cameraData = renderingData.cameraData;
            cmd.SetComputeIntParam(_rayTracingShader, "_cameraType", (int)cameraData.cameraType);
            cmd.SetComputeMatrixParam(_rayTracingShader, "_CameraToWorld", cameraData.camera.cameraToWorldMatrix);
            cmd.SetComputeMatrixParam(_rayTracingShader, "_CameraInverseProjection",
                cameraData.camera.projectionMatrix.inverse);
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            _renderTargetDescriptor.width = cameraTextureDescriptor.width;
            _renderTargetDescriptor.height = cameraTextureDescriptor.height;
            _renderTargetDescriptor.graphicsFormat = GraphicsFormat.R32G32B32A32_SFloat;
            _renderTargetDescriptor.enableRandomWrite = true;
            RenderingUtils.ReAllocateIfNeeded(ref _renderTarget, _renderTargetDescriptor);

            // cmd.SetComputeBufferParam(_rayTracingShader, 0, "SphereBuffer", _sphereBuffer);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get("Path Tracing Pass");

            RTHandle cameraColorHandle = renderingData.cameraData.renderer.cameraColorTargetHandle;

            //填充数据
            cmd.SetComputeTextureParam(_rayTracingShader, 0, "Result", _renderTarget);

            //设置渲染线程
            int threadGroupsX = Mathf.CeilToInt(_renderTarget.rt.width / 8.0f);
            int threadGroupsY = Mathf.CeilToInt(_renderTarget.rt.height / 8.0f);
            cmd.DispatchCompute(_rayTracingShader, 0, threadGroupsX, threadGroupsY, 1);

            //绑定到屏幕输出
            Blit(cmd, _renderTarget, cameraColorHandle);

            // cmd.SetRenderTarget(_renderTarget);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            // _sphereBuffer.Release();
        }

        public void Dispose()
        {
            _renderTarget?.Release();
            // _sphereBuffer?.Release();
        }
    }

    public ComputeShader rayTracingShader;

    private PathTracingPass _pathTracingPass;

    public override void Create()
    {
        _pathTracingPass = new PathTracingPass(rayTracingShader);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!Application.isPlaying)
            return;
        renderer.EnqueuePass(_pathTracingPass);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _pathTracingPass.Dispose();
        base.Dispose(disposing);
    }
}