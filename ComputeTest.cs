using Godot;
using Godot.Collections;
using System;
using System.Runtime.InteropServices;

[Tool]
public partial class ComputeTest : Sprite2D
{
    [Export] Vector2I Resolution = new Vector2I(1280, 720);
    [Export] Camera3D Camera;
    [Export] DirectionalLight3D DirectionalLight;
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

    [Export]
    public Transform3D Matrix
    {
        get => Camera.GlobalTransform;
        set
        {
        }
    }

    private RenderingDevice rd;
    private Rid shader;

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    struct DirectionalLightData
    {
        [FieldOffset(0)] public Vector3 position;
        [FieldOffset(12)] public float intensity;
    }

    /*struct Matrix4x4
    {
        public float xx = 0, xy = 0, xz = 0, xw = 0,
                    yx = 0, yy = 0, yz = 0, yw = 0,
                    zx = 0, zy = 0, zz = 0, zw = 0,
                    wx = 0, wy = 0, wz = 0, ww = 0;

        public float this[int x, int y]
        {
            get
            {
                switch (x, y)
                {
                    case (0, 0):
                        return xx;
                    case (0, 1):
                        return xy;
                    case (0, 2):
                        return xz;
                    case (0, 3):
                        return xw;
                    case (1, 0):
                        return yx;
                    case (1, 1):
                        return yy;
                    case (1, 2):
                        return yz;
                    case (1, 3):
                        return yw;
                    case (2, 0):
                        return zx;
                    case (2, 1):
                        return zy;
                    case (2, 2):
                        return zz;
                    case (2, 3):
                        return zw;
                    case (3, 0):
                        return wx;
                    case (3, 1):
                        return wy;
                    case (3, 2):
                        return wz;
                    case (3, 3):
                        return ww;
                    default:
                        throw new IndexOutOfRangeException();
                }
            }
            set
            {
                switch (x, y)
                {
                    case (0, 0):
                        xx = value;
                        return;
                    case (0, 1):
                        xy = value;
                        return;
                    case (0, 2):
                        xz = value;
                        return;
                    case (0, 3):
                        xw = value;
                        return;
                    case (1, 0):
                        yx = value;
                        return;
                    case (1, 1):
                        yy = value;
                        return;
                    case (1, 2):
                        yz = value;
                        return;
                    case (1, 3):
                        yw = value;
                        return;
                    case (2, 0):
                        zx = value;
                        return;
                    case (2, 1):
                        zy = value;
                        return;
                    case (2, 2):
                        zz = value;
                        return;
                    case (2, 3):
                        zw = value;
                        return;
                    case (3, 0):
                        wx = value;
                        return;
                    case (3, 1):
                        wy = value;
                        return;
                    case (3, 2):
                        wz = value;
                        return;
                    case (3, 3):
                        ww = value;
                        return;
                    default:
                        throw new IndexOutOfRangeException();
                }
            }
        }

        public Matrix4x4()
        {
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 88)]
    struct CameraData
    {
        [FieldOffset(0)] public Vector3 worldSpaceCameraPosition;
        [FieldOffset(12)] public float width;
        [FieldOffset(16)] public float height;
        [FieldOffset(20)] public float near;
        [FieldOffset(24)] public Matrix4x4 cameraToWorld;
    }*/

    [StructLayout(LayoutKind.Explicit, Size = 8)]
    struct SizeData
    {
        [FieldOffset(0)] public int width;
        [FieldOffset(4)] public int height;
    }

    // Buffers to update
    private byte[] cameraDataBytes;
    private float[] /*CameraData*/ cameraData;
    private Rid cameraDataBuffer;

    private byte[] directionalLightDataBytes;
    private DirectionalLightData directionalLightData;

    private Rid directionalLightDataBuffer;

    private Rid outputTexture;
    private Image img;

    private ImageTexture imageTexture;

    private bool _initialized = false;

    private uint currentFrame = 0;

    void Initialize()
    {
        if (Camera is null || DirectionalLight is null)
        {
            var root = GetParent().GetParent();

            Camera = root.GetNode("Camera3D") as Camera3D;
            DirectionalLight = root.GetNode("DirectionalLight3D") as DirectionalLight3D;
        }

        rd = RenderingServer.CreateLocalRenderingDevice();

        var shaderFile = GD.Load<RDShaderFile>("res://Shader/rtx_compute.glsl");
        var shaderBytecode = shaderFile.GetSpirV();
        shader = rd.ShaderCreateFromSpirV(shaderBytecode);

        TextureFilter = TextureFilterEnum.Nearest;

        // Create buffers

        // Size
        var size = new SizeData();
        size.width = Resolution.X;
        size.height = Resolution.Y;
        var sizeBytes = GetBytes(size);

        var sizeBuffer = rd.StorageBufferCreate((uint)sizeBytes.Length, sizeBytes);

        // Camera
        cameraData = new float[16 + 3 + 3 + 1]; // mat4 vec3 vec3 uint
        cameraDataBytes = new byte[cameraData.Length * 4];
        UpdateCameraData();
        //cameraData = new CameraData();

        /*GD.Print("In: ");
        PrintArray(GetBytes(cameraData.viewParams), 0, 12, 8);*/

        //cameraDataBytes = GetBytes(cameraData);
        Buffer.BlockCopy(cameraData, 0, cameraDataBytes, 0, cameraData.Length * sizeof(float));
        cameraDataBuffer = rd.StorageBufferCreate((uint)cameraDataBytes.Length, cameraDataBytes);
        /*GD.Print("Out: ");
        PrintArray(rd.BufferGetData(cameraDataBuffer, 12, 12), 0, 12, 8);

        GD.Print("Test: ");
        PrintArray(GetBytes(new Vector3(0, 0, 1)), 0, 12, 8);*/

        // Light
        directionalLightData = new DirectionalLightData(); // Position = 3, Strength = 1 
        directionalLightData.position = DirectionalLight.Position;
        directionalLightData.intensity = DirectionalLight.LightEnergy;

        directionalLightDataBytes = GetBytes(directionalLightData);
        directionalLightDataBuffer = rd.StorageBufferCreate((uint)directionalLightDataBytes.Length, directionalLightDataBytes);

        // Objects (TODO)

        // ...

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

        var uniformSize = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding = 1
        };
        var uniformCamera = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding = 2
        };
        var uniformDirectionalLight = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding = 3
        };

        outputTextureUniform.AddId(outputTexture);
        uniformSize.AddId(sizeBuffer);
        uniformCamera.AddId(cameraDataBuffer);
        uniformDirectionalLight.AddId(directionalLightDataBuffer);

        uniformSet = rd.UniformSetCreate(new Array<RDUniform> { outputTextureUniform, uniformSize, uniformCamera, uniformDirectionalLight }, shader, 0);

        // Create a compute pipeline
        pipeline = rd.ComputePipelineCreate(shader);

        currentFrame = 0;
    }

    void Compute()
    {
        // UPDATE BUFFERS

        // Camera
        UpdateCameraData();

        // PrintBuffer(cameraDataBytes);

        //cameraDataBytes = GetBytes(cameraData);
        Buffer.BlockCopy(cameraData, 0, cameraDataBytes, 0, cameraData.Length * sizeof(float));
        rd.BufferUpdate(cameraDataBuffer, 0, (uint)cameraDataBytes.Length, cameraDataBytes);

        // DirectionalLight
        directionalLightData.position = DirectionalLight.Position;
        directionalLightData.intensity = DirectionalLight.LightEnergy;

        directionalLightDataBytes = GetBytes(directionalLightData);
        rd.BufferUpdate(directionalLightDataBuffer, 0, (uint)directionalLightDataBytes.Length, directionalLightDataBytes);

        // Submit to GPU and wait for sync
        var computeList = rd.ComputeListBegin();
        rd.ComputeListBindComputePipeline(computeList, pipeline);
        rd.ComputeListBindUniformSet(computeList, uniformSet, 0);
        rd.ComputeListDispatch(computeList, xGroups: (uint)Resolution.X / 8, yGroups: (uint)Resolution.Y / 8, zGroups: 1);
        rd.ComputeListEnd();

        rd.Submit();
        rd.Sync();

        // Read new data from the output image buffer

        var byteData = rd.TextureGetData(outputTexture, 0);

        if (imageTexture is null)
        {
            img = Image.CreateFromData(Resolution.X, Resolution.Y, false, Image.Format.Rgbaf, byteData);
            img.ResourceName = "RTX Generated Image";

            imageTexture = ImageTexture.CreateFromImage(img);
            Texture = imageTexture;
        }
        else
        {
            img.SetData(Resolution.X, Resolution.Y, false, Image.Format.Rgbaf, byteData);
            imageTexture.Update(img);
        }

        currentFrame++;
    }

    void UpdateCameraData()
    {
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

        float planeHeight = Camera.Near * Mathf.Tan(Mathf.DegToRad(Camera.Fov * 0.5f)) * 2;
        float planeWidth = planeHeight * aspectRatio;

        cameraData[19] = planeWidth;
        cameraData[20] = planeHeight;
        cameraData[21] = Camera.Near;

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
            if (i == 16 || i == 19 || (i < 16 && i % 4 == 0))
            {
                lines += "\n";
            }

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
        // GD.Print("------------------------------------");
        // PrintArray(arr, 0, arr.Length, 8);
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
    private Rid uniformSet;
    private Rid pipeline;

    public override void _Process(double delta)
    {
        if (!Engine.IsEditorHint())
        {
            if (t >= interval)
            {
                // GD.Print("Rendered");
                Compute();
                t = 0;
            }

            t += delta;
        }
    }
}
