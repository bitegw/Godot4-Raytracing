using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

[Tool]
public partial class ComputeTest : Sprite2D
{
    [Export] public Vector2I Resolution = new Vector2I(1280, 720);
    [Export] Camera3D Camera;
    [Export] Node3D SceneRoot;
    [Export] float aspectRatio = 1280f / 720f;
    //[Export] float focusDistance = 1f;

    [Export]
    public bool Run
    {
        get => false;
        set
        {
            Compute();
        }
    }

    [Export]
    public bool InitializeShader
    {
        get => false;
        set
        {
            if (rd is not null)
            {
                rd.Dispose();
            }
            Initialize();
            Compute();
        }
    }

    // [Export]
    // public Transform3D Matrix
    // {
    //     get => Camera.GlobalTransform;
    //     set
    //     {
    //     }
    // }

    private RenderingDevice rd;
    private Rid shader;

    private const int SIZE_SETTINGS = 28;
    private const int SIZE_LIGHT = 44;
    private const int SIZE_MATERIAL = 48;
    private const int SIZE_SURFACE = 72;
    private const int SIZE_VEC3 = 12;
    private const int SIZE_VEC2 = 8;
    private const int SIZE_NUM = 4;

    [StructLayout(LayoutKind.Sequential, Size = SIZE_SETTINGS)]
    public struct SettingsData
    {
         public uint width;
         public uint height;
         public uint numRays;
         public uint maxBounces;
         public bool temporalAccumulation;
         public float recentFrameBias;
         public bool checkerboard;
    }

    [StructLayout(LayoutKind.Sequential, Size = SIZE_LIGHT)]
    public struct LightData {
         public float positionX;
         public float positionY;
         public float positionZ;
         public float colorR;
         public float colorG;
         public float colorB;
         public float intensity;
         public bool isDirectionalLight;
         public float directionX; // or range if not directional
         public float directionY;
         public float directionZ;
    }

    [StructLayout(LayoutKind.Sequential, Size = SIZE_MATERIAL)]
    public struct MaterialData {
        public float albedoR = 0.95f;
        public float albedoG = 0.95f;
        public float albedoB = 0.95f;
        public int textureID = -1;
        public float emissiveR = 0f;
        public float emissiveG = 0f;
        public float emissiveB = 0f;
        public int emissiveTextureID = -1;
        public float roughness = 1f;
        public int roughnessTextureID = -1;
        public float alpha = 1f;
        public int alphaTextureID = -1;

        public MaterialData() {}
    }

    class SurfaceData {
        public Vector3[] vertices;
        public Vector3[] normals;
        public Vector2[] uvs;
        public int[] indices;
    }

    class SurfaceInstance {
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale;
        public Vector3 boxMin;
        public Vector3 boxMax;
        // public int materialIndex;
        // public int surfaceIndex;
        public MaterialData material;
        public SurfaceData surfaceData;
    }

    [StructLayout(LayoutKind.Sequential, Size = SIZE_SURFACE)]
    class SurfaceDescriptor {
         public float positionX;
         public float positionY;
         public float positionZ;
         public float rotationX;
         public float rotationY;
         public float rotationZ;
         public float scaleX;
         public float scaleY;
         public float scaleZ;
         public float boxMinX;
         public float boxMinY;
         public float boxMinZ;
         public float boxMaxX;
         public float boxMaxY;
         public float boxMaxZ;
         public int materialID;
         public int indexStart;
         public int indexEnd;
    }

    // Buffers to update
    public SettingsData settings;
    private Rid settingsBuffer;

    private byte[] cameraDataBytes;
    private float[] cameraData;
    private Rid cameraDataBuffer;

    private List<LightData> lightData;

    private Rid lightDataBuffer;
    private Rid surfaceDataBuffer;
    private Rid vertexBuffer;
    private Rid normalBuffer;
    private Rid uvBuffer;
    private Rid indexBuffer;
    private Rid materialBuffer;

    // private Rid albedoTextureBuffer;
    // private Rid emissiveTextureBuffer;
    // private Rid roughnessTextureBuffer;
    // private Rid alphaTextureBuffer;

    private Rid outputTexture;
    private Image img;
    private ImageTexture imageTexture;
    private Rid uniformSet;
    private Rid pipeline;

    private bool _initialized = false;

    private uint currentFrame = 0;

    void Initialize()
    {
        if (Camera is null || SceneRoot is null)
        {
            var root = GetParent().GetParent();

            Camera = root.GetNode("Camera3D") as Camera3D;
            SceneRoot = root.GetNode("World") as Node3D;
        }

        rd = RenderingServer.CreateLocalRenderingDevice();

        var shaderFile = GD.Load<RDShaderFile>("res://Shader/rtx_compute.glsl");
        var shaderBytecode = shaderFile.GetSpirV();
        shader = rd.ShaderCreateFromSpirV(shaderBytecode);

        TextureFilter = TextureFilterEnum.Nearest;

        // Create buffers
        var settingsBytes = GetBytes(settings);
        settingsBuffer = rd.StorageBufferCreate((uint)settingsBytes.Length, settingsBytes);

        // Camera
        cameraData = new float[16 + 3 + 3 + 1]; // mat4 Vector3 Vector3 uint
        cameraDataBytes = new byte[cameraData.Length * 4];
        UpdateCameraData();
        //cameraData = new CameraData();

        //cameraDataBytes = GetBytes(cameraData);
        Buffer.BlockCopy(cameraData, 0, cameraDataBytes, 0, cameraData.Length * sizeof(float));
        cameraDataBuffer = rd.StorageBufferCreate((uint)cameraDataBytes.Length, cameraDataBytes);

        UpdateScene(SceneRoot);

        // Light
        lightDataBuffer = rd.StorageBufferCreate(1);

        // Mesh
        surfaceDataBuffer = rd.StorageBufferCreate((uint)surfaceDataBytes.Length, surfaceDataBytes);
        vertexBuffer = rd.StorageBufferCreate((uint)vertexDataBytes.Length, vertexDataBytes);
        normalBuffer = rd.StorageBufferCreate((uint)normalDataBytes.Length, normalDataBytes);
        uvBuffer = rd.StorageBufferCreate((uint)uvDataBytes.Length, uvDataBytes);
        indexBuffer = rd.StorageBufferCreate((uint)indexDataBytes.Length, indexDataBytes);
        materialBuffer = rd.StorageBufferCreate((uint)materialDataBytes.Length, materialDataBytes);

        // albedoTextureBuffer = rd.TextureBufferCreate(1, RenderingDevice.DataFormat.R32G32B32A32Sfloat);
        // roughnessTextureBuffer = rd.TextureBufferCreate(1, RenderingDevice.DataFormat.R32G32B32A32Sfloat);
        // emissiveTextureBuffer = rd.TextureBufferCreate(1, RenderingDevice.DataFormat.R32G32B32A32Sfloat);
        // alphaTextureBuffer = rd.TextureBufferCreate(1, RenderingDevice.DataFormat.R32G32B32A32Sfloat);

        // Create uniforms to assign the buffers to the rendering device

        var fmt = new RDTextureFormat();
        fmt.Width = (uint)Resolution.X;
        fmt.Height = (uint)Resolution.Y;
        fmt.Format = RenderingDevice.DataFormat.R32G32B32A32Sfloat;
        fmt.UsageBits = RenderingDevice.TextureUsageBits.CanUpdateBit | RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.CanCopyFromBit;

        var view = new RDTextureView();

        var outputImage = Image.Create(Resolution.X, Resolution.Y, false, Image.Format.Rgbaf);
        var outputData = new Godot.Collections.Array<byte[]> { outputImage.GetData() };
        outputTexture = rd.TextureCreate(fmt, view, outputData);
        var outputTextureUniform = new RDUniform()
        {
            UniformType = RenderingDevice.UniformType.Image,
            Binding = 0
        };
        
        var uniformSettings = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding = 1
        };
        var uniformCamera = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding = 2
        };
        var uniformLights = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding = 3
        };

        var uniformSurfaces = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding = 4
        };
        var uniformVertices = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding = 5
        };
        var uniformNormals = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding = 6
        };
        var uniformUVs = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding = 7
        };
        var uniformIndices = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding = 8
        };
        var uniformMaterials = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding = 9
        };

        // var uniformTexturesAlbedo = new RDUniform
        // {
        //     UniformType = RenderingDevice.UniformType.TextureBuffer,
        //     Binding = 
        // };
        // var uniformTexturesRoughness = new RDUniform
        // {
        //     UniformType = RenderingDevice.UniformType.TextureBuffer,
        //     Binding = 
        // };
        // var uniformTextures = new RDUniform
        // {
        //     UniformType = RenderingDevice.UniformType.TextureBuffer,
        //     Binding = 
        // };
        // var uniformTexturesAlpha = new RDUniform
        // {
        //     UniformType = RenderingDevice.UniformType.TextureBuffer,
        //     Binding = 
        // };

        outputTextureUniform.AddId(outputTexture);
        uniformSettings.AddId(settingsBuffer);
        uniformCamera.AddId(cameraDataBuffer);
        uniformLights.AddId(lightDataBuffer);
        uniformSurfaces.AddId(surfaceDataBuffer);
        uniformVertices.AddId(vertexBuffer);
        uniformNormals.AddId(normalBuffer);
        uniformUVs.AddId(uvBuffer);
        uniformIndices.AddId(indexBuffer);
        uniformMaterials.AddId(materialBuffer);

        // uniformTexturesAlbedo.AddId(albedoTextureBuffer);
        // uniformTexturesRoughness.AddId(roughnessTextureBuffer);
        // uniformTexturesEmissive.AddId(emissiveTextureBuffer);
        // uniformTexturesAlpha.AddId(alphaTextureBuffer);

        uniformSet = rd.UniformSetCreate(new Array<RDUniform> { 
            outputTextureUniform, 
            uniformSettings, 
            uniformCamera, 
            uniformLights, 
            uniformSurfaces, 
            uniformVertices, 
            uniformNormals, 
            uniformUVs,
            uniformIndices, 
            uniformMaterials, 
            // uniformTexturesAlbedo, 
            // uniformTexturesRoughness, 
            // uniformTexturesEmissive, 
            // uniformTexturesAlpha 
        }, shader, 0);

        // Create a compute pipeline
        pipeline = rd.ComputePipelineCreate(shader);

        currentFrame = 0;
    }

    void Compute()
    {
        // UPDATE BUFFERS

        // Camera
        if(cameraData is not null) {
            UpdateCameraData();

            // PrintBuffer(cameraDataBytes);
            Buffer.BlockCopy(cameraData, 0, cameraDataBytes, 0, cameraData.Length * sizeof(float));
            rd.BufferUpdate(cameraDataBuffer, 0, (uint)cameraDataBytes.Length, cameraDataBytes);
        }

        if(rd is null) {
            GD.Print("No render device!");
            return;
        }

        // Submit to GPU and wait for sync
        var computeList = rd.ComputeListBegin();
        rd.ComputeListBindComputePipeline(computeList, pipeline);
        rd.ComputeListBindUniformSet(computeList, uniformSet, 0);
        rd.ComputeListDispatch(computeList, xGroups: settings.width / 8, yGroups:settings.height / 8, zGroups: 1);
        rd.ComputeListEnd();

        rd.Submit();
        rd.Sync();

        // Read new data from the output image buffer
        if (imageTexture is null)
        {
            img = Image.Create(Resolution.X, Resolution.Y, false, Image.Format.Rgbaf);
            img.ResourceName = "Raytraced Image";

            imageTexture = ImageTexture.CreateFromImage(img);
            Texture = imageTexture;
        } else {
            var byteData = rd.TextureGetData(outputTexture, 0);
            img.SetData(Resolution.X, Resolution.Y, false, Image.Format.Rgbaf, byteData);
            imageTexture.Update(img);
        }
        
        currentFrame++;
    }

    public void UpdateResolution(Vector2I newSize) {
        imageTexture = null;
        Resolution = newSize;
        settings.width = (uint)newSize.X;
        settings.height = (uint)newSize.Y;
        Initialize();
    }

    public static List<Vector3> BytesToVector3List(byte[] byteArray) {
        if (byteArray.Length % 12 != 0) {
            throw new ArgumentException("Byte array length must be a multiple of 12 bytes.");
        }

        List<Vector3> vectorList = new List<Vector3>();

        for (int i = 0; i < byteArray.Length; i += 12) {
            byte[] subArray = new byte[12];
            System.Array.Copy(byteArray, i, subArray, 0, 12);

            Vector3 vector3;
            GCHandle handle = GCHandle.Alloc(subArray, GCHandleType.Pinned);
            vector3 = (Vector3)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(Vector3));
            handle.Free();

            vectorList.Add(vector3);
        }

        return vectorList;
    }

    public void ProcessNode(Node3D node) {
        if(!node.Visible)
            return;

        if(node is MeshInstance3D) {
            MeshInstance3D meshInstance = (MeshInstance3D)node;
            Mesh mesh = meshInstance.Mesh;
            if(mesh != null) {
                int numSurfaces = mesh.GetSurfaceCount();
                for(int i=0; i<numSurfaces; i++) {
                    var arrays = mesh.SurfaceGetArrays(i); // 0 - positions, 1 - normals, 2 - tangent, 3 - color, 4 - uv, 12 - index
                    SurfaceInstance newSurfaceInstance = new SurfaceInstance
                    {
                        position = meshInstance.GlobalPosition,
                        rotation = meshInstance.GlobalRotation,
                        scale = meshInstance.Scale,
                        boxMin = meshInstance.GetAabb().Position,
                        boxMax = meshInstance.GetAabb().End,
                        surfaceData = new SurfaceData()
                    };

                    newSurfaceInstance.surfaceData.vertices = (Vector3[])arrays[0];
                    newSurfaceInstance.surfaceData.normals = (Vector3[])arrays[1];
                    newSurfaceInstance.surfaceData.uvs = (Vector2[])arrays[4];
                    newSurfaceInstance.surfaceData.indices = (int[])arrays[12];

                    var materialData = new MaterialData();
                    Material material = mesh.SurfaceGetMaterial(i);
                    Material materialOverride = meshInstance.MaterialOverride;
                    Material materialOverlay = meshInstance.MaterialOverlay;
                    if(materialOverride is not null) {
                        material = materialOverride;
                    }
                    if(materialOverlay is not null) {
                        material = materialOverlay;
                    }
                    if(material is StandardMaterial3D standardMaterial) {
                        GD.Print("Standard material found.");
                        materialData.albedoR = standardMaterial.AlbedoColor.R;
                        materialData.albedoG =  standardMaterial.AlbedoColor.G;
                        materialData.albedoB =  standardMaterial.AlbedoColor.B;
                        materialData.emissiveR = standardMaterial.Emission.R;
                        materialData.emissiveG = standardMaterial.Emission.G;
                        materialData.emissiveB = standardMaterial.Emission.B;
                        materialData.roughness = standardMaterial.Roughness;
                        materialData.textureID = 0;
                        materialData.emissiveTextureID = 0;
                        materialData.alphaTextureID = 0;
                        materialData.textureID = 0;
                    }

                    newSurfaceInstance.material = materialData;

                    surfaceInstances.Add(newSurfaceInstance);
                }
            }
        } else if(node is DirectionalLight3D){
            // GD.Print("Light!");
        }

        foreach(Node childNode in node.GetChildren()) {
            if(childNode is Node3D)
            ProcessNode((Node3D)childNode);
        }
    }

    private void RegisterSurface(
        SurfaceInstance surfaceInstance,
        List<Vector3> bigVertexList,
        List<Vector3> bigNormalList,
        List<Vector2> bigUVList,
        List<int> bigIndexList,
        List<MaterialData> bigMaterialList,
        List<SurfaceDescriptor> bigSurfaceDescriptorList,
        System.Collections.Generic.Dictionary<MaterialData, int> materialCache,
        System.Collections.Generic.Dictionary<SurfaceData, int> surfaceDataCache,
        ref int indexOffset
        )
    {
        if (!materialCache.TryGetValue(surfaceInstance.material, out int materialIndex)) {
            materialIndex = bigMaterialList.Count;
            bigMaterialList.Add(surfaceInstance.material);
            materialCache[surfaceInstance.material] = materialIndex;
        }

        SurfaceDescriptor surfaceDescriptor = new SurfaceDescriptor {
            positionX = surfaceInstance.position.X,
            positionY = surfaceInstance.position.Y,
            positionZ = surfaceInstance.position.Z,
            rotationX = surfaceInstance.rotation.X,
            rotationY = surfaceInstance.rotation.Y,
            rotationZ = surfaceInstance.rotation.Z,
            scaleX = surfaceInstance.scale.X,
            scaleY = surfaceInstance.scale.Y,
            scaleZ = surfaceInstance.scale.Z,
            boxMinX = surfaceInstance.boxMin.X,
            boxMinY = surfaceInstance.boxMin.Y,
            boxMinZ = surfaceInstance.boxMin.Z,
            boxMaxX = surfaceInstance.boxMax.X,
            boxMaxY = surfaceInstance.boxMax.Y,
            boxMaxZ = surfaceInstance.boxMax.Z,
            materialID = materialIndex,
            indexStart = indexOffset,
            indexEnd = indexOffset + surfaceInstance.surfaceData.indices.Length
        };

        if (!surfaceDataCache.TryGetValue(surfaceInstance.surfaceData, out int surfaceDataIndex)) {
            surfaceDataIndex = surfaceDataCache.Count;
            surfaceDataCache[surfaceInstance.surfaceData] = surfaceDataIndex;

            int vertexOffset = bigVertexList.Count;

            // Add vertices, normals, uvs, and indices to big lists
            bigVertexList.AddRange(surfaceInstance.surfaceData.vertices);
            bigNormalList.AddRange(surfaceInstance.surfaceData.normals);
            bigUVList.AddRange(surfaceInstance.surfaceData.uvs);

            foreach (int index in surfaceInstance.surfaceData.indices) {
                bigIndexList.Add(index + vertexOffset);
            }

            indexOffset += surfaceInstance.surfaceData.indices.Length;
        }

        bigSurfaceDescriptorList.Add(surfaceDescriptor);
    }

    List<SurfaceInstance> surfaceInstances;

    List<Vector3> bigVertexList;
    List<Vector3> bigNormalList;
    List<Vector2> bigUVList;
    List<int> bigIndexList;
    List<MaterialData> bigMaterialList;
    List<SurfaceDescriptor> bigSurfaceDescriptorList;

    byte[] surfaceDataBytes, vertexDataBytes, normalDataBytes, uvDataBytes, indexDataBytes, materialDataBytes;

    public void UpdateScene(Node3D root) {
        // Process node will scan the nodes and populate the surfaceInstances and genericLights.
        if(surfaceInstances == null)
            surfaceInstances = new List<SurfaceInstance>();
        else
            surfaceInstances.Clear();
        
        ProcessNode(root);

        bigVertexList = new List<Vector3>();
        bigNormalList = new List<Vector3>();
        bigUVList = new List<Vector2>();
        bigIndexList = new List<int>();
        bigMaterialList = new List<MaterialData>();
        bigSurfaceDescriptorList = new List<SurfaceDescriptor>();

        System.Collections.Generic.Dictionary<MaterialData, int> materialCache = new System.Collections.Generic.Dictionary<MaterialData, int>();
        System.Collections.Generic.Dictionary<SurfaceData, int> surfaceDataCache = new System.Collections.Generic.Dictionary<SurfaceData, int>();

        int indexOffset = 0;

        foreach(SurfaceInstance surfaceInstance in surfaceInstances) {
            RegisterSurface(surfaceInstance, bigVertexList, bigNormalList, bigUVList, bigIndexList, bigMaterialList, bigSurfaceDescriptorList, materialCache, surfaceDataCache, ref indexOffset);
        }

        surfaceDataBytes = GetBytes(bigSurfaceDescriptorList, SIZE_SURFACE);
        vertexDataBytes = GetBytes(bigVertexList, SIZE_VEC3);
        normalDataBytes = GetBytes(bigNormalList, SIZE_VEC3);
        uvDataBytes = GetBytes(bigUVList, SIZE_VEC2);
        indexDataBytes = GetBytes(bigIndexList, SIZE_NUM);
        materialDataBytes = GetBytes(bigMaterialList, SIZE_MATERIAL);
    }

    public void UpdateSceneBuffers() {
        rd.BufferUpdate(surfaceDataBuffer, 0, (uint)surfaceDataBytes.Length, surfaceDataBytes);
        rd.BufferUpdate(vertexBuffer, 0, (uint)vertexDataBytes.Length, vertexDataBytes);
        rd.BufferUpdate(normalBuffer, 0, (uint)normalDataBytes.Length, normalDataBytes);
        rd.BufferUpdate(uvBuffer, 0, (uint)uvDataBytes.Length, uvDataBytes);
        rd.BufferUpdate(indexBuffer, 0, (uint)indexDataBytes.Length, indexDataBytes);
    }

    public void UpdateSettings() {
        var settingsBytes = GetBytes(settings);
        rd.BufferUpdate(settingsBuffer, 0, (uint)settingsBytes.Length, settingsBytes);
    }

    void UpdateCameraData()
    {
        float[] lastFrameCameraData = (float[])cameraData.Clone();

        var camT = Camera.GlobalTransform;

        cameraData[0] = camT.Basis.X.X;
        cameraData[1] = camT.Basis.Y.X;
        cameraData[2] = camT.Basis.Z.X;
        cameraData[3] = camT.Origin.X;

        cameraData[4] = camT.Basis.X.Y;
        cameraData[5] = camT.Basis.Y.Y;
        cameraData[6] = camT.Basis.Z.Y;
        cameraData[7] = camT.Origin.Y;

        cameraData[8] = camT.Basis.X.Z;
        cameraData[9] = camT.Basis.Y.Z;
        cameraData[10] = camT.Basis.Z.Z;
        cameraData[11] = camT.Origin.Z;

        cameraData[12] = 0;
        cameraData[13] = 0;
        cameraData[14] = 0;
        cameraData[15] = 1;

        cameraData[16] = Camera.GlobalPosition.X;
        cameraData[17] = Camera.GlobalPosition.Y;
        cameraData[18] = Camera.GlobalPosition.Z;

        float planeHeight = Camera.Near * Mathf.Tan(Mathf.DegToRad(Camera.Fov / 2));
        float planeWidth = planeHeight * aspectRatio;

        cameraData[19] = planeWidth;
        cameraData[20] = planeHeight;
        cameraData[21] = Camera.Near;

        for(int i=0; i<12; i++)
        if(cameraData[i] != lastFrameCameraData[i]) {
            if(settings.checkerboard) {
                currentFrame = currentFrame%2;
            }
            else {
                currentFrame = 0;
            }
        }

        cameraData[22] = currentFrame;
    }

    void PrintBuffer(byte[] data)
    {
        string lines = "";
        int i = 0;
        int offset = 0;
        while (offset < data.Length)
        {
            i++;
            float val = BitConverter.ToSingle(data, offset);

            lines += val.ToString("0.000") + " ";
            // if (i == 16 || i == 19 || (i < 16 && i % 4 == 0))
            // {
            //     lines += "\n";
            // }

            offset += 4;
        }

        GD.Print(lines + "\n");
    }

    byte[] GetBytes(object myStruct)
    {
        int size = Marshal.SizeOf(myStruct);
        byte[] arr = new byte[size];

        IntPtr ptr = IntPtr.Zero;
        try
        {
            ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(myStruct, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return arr;
    }

    byte[] GetBytes<T>(List<T> list, uint size)
    {
            byte[] arr = new byte[size * list.Count];

            for(int i=0; i<list.Count; i++) {
                byte[] subArr = GetBytes(list[i]);
                for(int j=0; j<size; j++) {
                    arr[i * size + j] = subArr[j]; 
                }
            }
            return arr;
    }

    private void PrintArray(float[] array, int start, int end, int chars)
    {
        string str = "";
        for (int i = start; i < end - 1; i++)
        {
            str += array[i].ToString().Substr(0, chars);
            str += " , ";
        }
        str += array[end - 1].ToString().Substr(0, chars);

        GD.Print(str);
    }

    public override void _Ready()
    {
        // Settings
        settings = new SettingsData();
        settings.width = (uint)Resolution.X;
        settings.height= (uint)Resolution.Y;
        settings.temporalAccumulation = true;
        settings.numRays = 5u;
        settings.maxBounces = 5u;
        settings.recentFrameBias = 0f;
        settings.checkerboard = false;

        if (!Engine.IsEditorHint())
        {
            if (!_initialized)
            {
                _initialized = true;
                Initialize();
            }

            Compute();
        }
    }

    double interval = 1f / 60f;
    double t = 0f;

    public override void _Process(double delta)
    {
        if (!Engine.IsEditorHint())
        {
            if (t >= interval)
            {
                Compute();
                t = 0;
            }

            t += delta;
        }
    }
}
