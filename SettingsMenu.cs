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

	[Export] private CustomRenderer customRenderer;
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

		temporalAccumulationCheck.ButtonPressed = customRenderer.settings.temporalAccumulation;
		checkerboardCheck.ButtonPressed = customRenderer.settings.checkerboard;

		numRays.Value = customRenderer.settings.numRays;
		maxBounces.Value = customRenderer.settings.maxBounces;

		slider.ValueChanged += (value) => {
			shaderMaterial.SetShaderParameter("t", value);
		};

		renderResolutionOptions.ItemSelected += (index) => {
			if(index >= resolutions.Length || index < 0) return;

			customRenderer.UpdateRenderResolution(resolutions[index]);

			if(viewReprojectionCheck.ButtonPressed) {
				camera.CullMask = projectedViewCameraMask;
			} else {
				camera.CullMask = defaultCameraMask;
			}
			customRenderer.ProjectedViewEnabled = viewReprojectionCheck.ButtonPressed;
		};

		windowResolutionOptions.ItemSelected += (index) => {
			if(index >= resolutions.Length || index < 0) return;

			int scaleIndex = (int)Mathf.Clamp(index, 2, 4);
			settingsPanel.Scale = new Vector2(scales[scaleIndex], scales[scaleIndex]);
			customRenderer.UpdateWindowResolution(resolutions[index]);
		};

		fullScreenCheck.Toggled += (value) => {
			windowResolutionOptions.Disabled = value;

			if(value) {
				customRenderer.UpdateWindowResolution(DisplayServer.ScreenGetSize());
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
			} else {
				GD.Print(resolutions[windowResolutionOptions.Selected]);
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
				customRenderer.UpdateWindowResolution(resolutions[windowResolutionOptions.Selected]);
			}
		};

		numRays.ValueChanged += (value) => {
			customRenderer.settings.numRays = (uint)value;
			customRenderer.UpdateSettings();
		};

		maxBounces.ValueChanged += (value) => {
			customRenderer.settings.maxBounces = (uint)value;
			customRenderer.UpdateSettings();
		};

		targetFPS.ValueChanged += (value) => {
			customRenderer.interval = 1.0 / value;
		};

		temporalAccumulationCheck.Toggled += (value) => {
			customRenderer.settings.temporalAccumulation = value;
			customRenderer.UpdateSettings();
		};

		recentFrameBias.ValueChanged += (value) => {
			customRenderer.settings.recentFrameBias = (float)value;
			if(customRenderer.settings.temporalAccumulation) {
				customRenderer.UpdateSettings();
			}
		};

		checkerboardCheck.Toggled += (value) => {
			customRenderer.settings.checkerboard = value;
			customRenderer.UpdateSettings();
		};

		viewReprojectionCheck.Toggled += (value) => {
			slider.Value = 1.0f;

			if(value) {
				camera.CullMask = projectedViewCameraMask;
			} else {
				camera.CullMask = defaultCameraMask;
			}
			customRenderer.ProjectedViewEnabled = value;
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
