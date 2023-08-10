using Godot;
using System;

public partial class SettingsMenu : Control
{
	private static Control instance;
    private bool isMenuVisible = false;
	[Export] private ShaderMaterial shaderMaterial;
	[Export] private CanvasItem canvasItem;
    [Export] private Slider slider;

	[Export] private ComputeTest computeTest;
	[Export] private OptionButton resolutionOptions;
	[Export] private SpinBox numRays;
	[Export] private SpinBox maxBounces;
	[Export] private SpinBox targetFPS;
	[Export] private Slider recentFrameBias;
	[Export] private CheckButton temporalAccumulationCheck;
	[Export] private CheckButton checkerboardCheck;

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

		temporalAccumulationCheck.Toggled += (value) => {
			computeTest.settings.temporalAccumulation = value;
			computeTest.UpdateSettings();
		};

		numRays.ValueChanged += (value) => {
			computeTest.settings.numRays = (uint)value;
			computeTest.UpdateSettings();
		};

		maxBounces.ValueChanged += (value) => {
			computeTest.settings.numRays = (uint)value;
			computeTest.UpdateSettings();
		};

		targetFPS.ValueChanged += (value) => {
			computeTest.interval = 1.0 / value;
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

		resolutionOptions.ItemSelected += (index) => {
			if(index >= resolutions.Length || index < 0) return;

			GetWindow().Size = resolutions[index];
			this.Scale = new Vector2(scales[index], scales[index]);
			computeTest.UpdateResolution(resolutions[index]);
		};
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
