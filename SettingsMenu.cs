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
	[Export] private SpinBox numRays;
	[Export] private SpinBox maxBounces;
	[Export] private Slider recentFrameBias;
	[Export] private CheckButton temporalAccumulationCheck;

    public override void _Ready()
    {
        if (instance != null && instance != this)
        {
            // Destroy this duplicate instance
            QueueFree();
            return;
        }
        instance = this;

		if (shaderMaterial == null)
        {
            GD.PrintErr("SettingsMenu: ShaderMaterial not assigned in the editor!");
            return;
        }

        if (slider == null)
        {
            GD.PrintErr("SettingsMenu: Slider not assigned in the editor!");
            return;
        }

		slider.ValueChanged += (value) => {
			OnSliderValueChanged(value);
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

		recentFrameBias.ValueChanged += (value) => {
			computeTest.settings.recentFrameBias = (float)value;
			if(computeTest.settings.temporalAccumulation) {
				computeTest.UpdateSettings();
			}
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

	public void OnSliderValueChanged(double value)
    {
        // Update the T parameter of the shader based on the Slider value
        if (shaderMaterial != null)
        {
            // Map the slider's value to the range 0.0 to 1.0 (adjust as needed)
            shaderMaterial.SetShaderParameter("t", value);
		}
    }
}
