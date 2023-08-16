using Godot;
using System;

public partial class SettingsMenu : Control
{
	private static Control instance;
    private bool isMenuVisible = false;
	[Export] private ShaderMaterial shaderMaterial;
	[Export] private ShaderMaterial projectionShaderMaterial;
	[Export] private CanvasItem canvasItem;
	[Export] private Sprite3D projectedView;
	[Export] private Camera3D camera;
	[Export(PropertyHint.Layers3DRender)] private uint defaultCameraMask;
	[Export(PropertyHint.Layers3DRender)] private uint projectedViewCameraMask;
    [Export] private Slider slider;

	[Export] private ComputeTest computeTest;
	[Export] private Panel settingsPanel;
	[Export] private OptionButton renderResolutionOptions;
	[Export] private OptionButton windowResolutionOptions;
	[Export] private CheckButton fullScreenCheck;
	[Export] private SpinBox numRays;
	[Export] private SpinBox maxBounces;
	[Export] private SpinBox targetFPS;
	[Export] private Label currentFPS;
	[Export] private Slider recentFrameBias;
	[Export] private CheckButton temporalAccumulationCheck;
	[Export] private CheckButton checkerboardCheck;
	[Export] private CheckButton viewReprojectionCheck;
	[Export] private Slider parallaxSlider;
	[Export] private Slider offsetSlider;
	[Export] private CheckButton showDepthCheck;

	public Vector2I[] resolutions = {new Vector2I(1920, 1080), new Vector2I(1600, 900), new Vector2I(1280, 720), new Vector2I(640, 360)};
	public float[] scales = {2, 1.5f, 1, 0.5f};

    public override void _Ready()
    {
        isMenuVisible = Visible;

		temporalAccumulationCheck.ButtonPressed = computeTest.settings.temporalAccumulation;
		checkerboardCheck.ButtonPressed = computeTest.settings.checkerboard;

		numRays.Value = computeTest.settings.numRays;
		maxBounces.Value = computeTest.settings.maxBounces;

		slider.ValueChanged += (value) => {
			shaderMaterial.SetShaderParameter("t", value);
		};

		renderResolutionOptions.ItemSelected += (index) => {
			if(index >= resolutions.Length || index < 0) return;

			computeTest.UpdateRenderResolution(resolutions[index]);

			if(viewReprojectionCheck.ButtonPressed) {
				camera.CullMask = projectedViewCameraMask;
			} else {
				camera.CullMask = defaultCameraMask;
			}
			computeTest.ProjectedViewEnabled = viewReprojectionCheck.ButtonPressed;
		};

		windowResolutionOptions.ItemSelected += (index) => {
			if(index >= resolutions.Length || index < 0) return;

			int scaleIndex = (int)Mathf.Clamp(index, 2, 4);
			settingsPanel.Scale = new Vector2(scales[scaleIndex], scales[scaleIndex]);
			computeTest.UpdateWindowResolution(resolutions[index]);
		};

		fullScreenCheck.Toggled += (value) => {
			windowResolutionOptions.Disabled = value;

			if(value) {
				computeTest.UpdateWindowResolution(DisplayServer.ScreenGetSize());
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
			} else {
				GD.Print(resolutions[windowResolutionOptions.Selected]);
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
				computeTest.UpdateWindowResolution(resolutions[windowResolutionOptions.Selected]);
			}
		};

		numRays.ValueChanged += (value) => {
			computeTest.settings.numRays = (uint)value;
			computeTest.UpdateSettings();
		};

		maxBounces.ValueChanged += (value) => {
			computeTest.settings.maxBounces = (uint)value;
			computeTest.UpdateSettings();
		};

		targetFPS.ValueChanged += (value) => {
			computeTest.interval = 1.0 / value;
		};

		temporalAccumulationCheck.Toggled += (value) => {
			computeTest.settings.temporalAccumulation = value;
			computeTest.UpdateSettings();
		};

		recentFrameBias.ValueChanged += (value) => {
			computeTest.settings.recentFrameBias = (float)value;
			if(computeTest.settings.temporalAccumulation) {
				computeTest.UpdateSettings();
			}
		};

		checkerboardCheck.Toggled += (value) => {
			computeTest.settings.checkerboard = value;
			computeTest.UpdateSettings();
		};

		viewReprojectionCheck.Toggled += (value) => {
			slider.Value = 1.0f;

			if(value) {
				camera.CullMask = projectedViewCameraMask;
			} else {
				camera.CullMask = defaultCameraMask;
			}
			computeTest.ProjectedViewEnabled = value;
		};

		parallaxSlider.ValueChanged += (value) => {
			projectionShaderMaterial.SetShaderParameter("depthMultiplier", value);
		};

		offsetSlider.ValueChanged += (value) => {
			projectionShaderMaterial.SetShaderParameter("offsetMultiplier", value);
		};

		showDepthCheck.Toggled += (value) => {
			shaderMaterial.SetShaderParameter("depth", value);
			projectionShaderMaterial.SetShaderParameter("depth", value);
		};
    }

	public override void _Process(double delta) {
		currentFPS.Text = Engine.GetFramesPerSecond().ToString();
	}

    public override void _Input(InputEvent @event)
    {
        // Toggle menu visibility with the Tab key
        if (@event.IsActionPressed("toggle_menu"))
        {
            isMenuVisible = !isMenuVisible;
            Visible = isMenuVisible;
        }
    }
}
