using Godot;
using System;


[Tool]
public partial class Sphere : MeshInstance3D
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		if (Engine.IsEditorHint())
		{
			if(Mesh is null) {
				Mesh = ResourceLoader.Load<Mesh>("res://Models/sphere_mesh.tres");
			}
		}
	}
}
