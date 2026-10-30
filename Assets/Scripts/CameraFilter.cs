using UnityEngine;


//Code from 'Unity - How to add a Material to a Camera' by Quickz
// https://github.com/QuickzYT/camera-material-tutorial-source
// https://youtu.be/HW8UePVtU5M

[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
public class CameraFilter : MonoBehaviour
{
    public Material material;

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (material == null)
        {
            Graphics.Blit(source, destination);
            return;
        }

        Graphics.Blit(source, destination, material);
    }
}
