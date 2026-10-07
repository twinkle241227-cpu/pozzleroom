using UnityEngine;

/// <summary>
/// Inspector-facing settings for the fixed, screen-space grain pass.
/// Attach this to the gameplay camera; the renderer feature reads these values
/// every frame, so changes are visible immediately in the Game view.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class StaticScreenGrainController : MonoBehaviour
{
    public static StaticScreenGrainController Active { get; private set; }

    [Header("Static Screen Grain")]
    [SerializeField] private bool effectEnabled = true;
    [Tooltip("Overall strength of the fixed grain.")]
    [SerializeField, Range(0f, 0.35f)] private float intensity = 0.075f;
    [Tooltip("Grain cell size in screen pixels. Larger values make chunkier particles.")]
    [SerializeField, Range(0.5f, 6f)] private float grainSize = 1.25f;
    [Tooltip("How many grain cells are visible across the image.")]
    [SerializeField, Range(0f, 1f)] private float density = 0.78f;
    [Header("Lighting Response")]
    [Tooltip("Extra grain applied to the dark parts of the image.")]
    [SerializeField, Range(0f, 1f)] private float shadowWeight = 0.45f;
    [SerializeField, Range(0f, 1f)] private float shadowThreshold = 0.5f;
    [SerializeField, Range(0.01f, 1f)] private float shadowSoftness = 0.25f;
    [Header("Colour")]
    [SerializeField] private Color grainTint = Color.white;

    private static readonly int GrainIntensity = Shader.PropertyToID("_StaticGrainIntensity");
    private static readonly int GrainSize = Shader.PropertyToID("_StaticGrainSize");
    private static readonly int GrainDensity = Shader.PropertyToID("_StaticGrainDensity");
    private static readonly int GrainShadowWeight = Shader.PropertyToID("_StaticGrainShadowWeight");
    private static readonly int GrainShadowThreshold = Shader.PropertyToID("_StaticGrainShadowThreshold");
    private static readonly int GrainShadowSoftness = Shader.PropertyToID("_StaticGrainShadowSoftness");
    private static readonly int GrainTint = Shader.PropertyToID("_StaticGrainTint");

    public bool IsEnabledFor(Camera camera)
    {
        return effectEnabled && camera != null && camera == GetComponent<Camera>();
    }

    public void ApplyTo(Material material)
    {
        if (material == null)
        {
            return;
        }

        material.SetFloat(GrainIntensity, intensity);
        material.SetFloat(GrainSize, grainSize);
        material.SetFloat(GrainDensity, density);
        material.SetFloat(GrainShadowWeight, shadowWeight);
        material.SetFloat(GrainShadowThreshold, shadowThreshold);
        material.SetFloat(GrainShadowSoftness, shadowSoftness);
        material.SetColor(GrainTint, grainTint);
    }

    private void OnEnable()
    {
        Active = this;
    }

    private void OnDisable()
    {
        if (Active == this)
        {
            Active = null;
        }
    }

    private void OnValidate()
    {
        grainSize = Mathf.Max(0.5f, grainSize);
        shadowSoftness = Mathf.Max(0.01f, shadowSoftness);
        Active = this;
    }
}
