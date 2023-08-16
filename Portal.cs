using Godot;
using System;

[Tool]
public partial class Portal : MeshInstance3D
{
	[Export] public float Width {
		get {
			return _width;
		} 
		set {
			_width = value;
			UpdateWidthHeight();
		}
	} 
	
	[Export] public float Height {
		get {
			return _height;
		} 
		set {
			_height = value;
			UpdateWidthHeight();
		}
	}

	private void UpdateWidthHeight() {
		Scale = new Vector3(_width, _height,1f);
	}

	private float _width = 1f, _height = 1f;
	[Export] public Portal Other;

	public override void _Ready()
	{
		if (Engine.IsEditorHint())
		{
			if(Mesh is null) {
				Mesh = ResourceLoader.Load<Mesh>("res://Models/portal_mesh.tres");
			}
		}
	}
}
