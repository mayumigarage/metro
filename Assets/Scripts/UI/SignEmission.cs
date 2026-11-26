using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Makes one UI layer emissive: multiplies an HDR colour into the graphic's material
/// (the <c>_Color</c> tint of UI/Default), so the layer can be brighter than white and
/// the stage's bloom picks it up. The graphic's own colour still shapes it (alpha,
/// gradients, the song's line colour); this sets how strongly it glows.
///
/// Used on every lit layer of the HUD sign prefab (MetroSignHud), so each one's
/// brightness is tuned on its own in the Inspector: the HDR picker's Intensity is the
/// glow in exposure stops. Works in edit mode too (no bloom there, but the colours show).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Graphic))]
public sealed class SignEmission : MonoBehaviour, IMaterialModifier
{
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [Tooltip("HDR tint multiplied into the layer. Intensity above 0 makes it brighter than white, so it blooms.")]
    [ColorUsage(false, true)]
    [SerializeField] private Color emission = Color.white;

    private Material material;
    private Material source;
    private Graphic graphic;

    public Color Emission
    {
        get => emission;
        set
        {
            emission = value;
            SetDirty();
        }
    }

    public Material GetModifiedMaterial(Material baseMaterial)
    {
        if (!isActiveAndEnabled || baseMaterial == null || !baseMaterial.HasProperty(ColorId))
        {
            return baseMaterial;
        }

        if (material == null || source != baseMaterial)
        {
            Release();
            source = baseMaterial;
            material = new Material(baseMaterial)
            {
                name = baseMaterial.name + " (Emission)",
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        Color tint = baseMaterial.GetColor(ColorId);
        Color lit = tint * emission;
        lit.a = tint.a;
        material.SetColor(ColorId, lit);
        return material;
    }

    private void OnEnable()
    {
        SetDirty();
    }

    private void OnDisable()
    {
        SetDirty();
        Release();
    }

    private void OnDestroy()
    {
        Release();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        SetDirty();
    }
#endif

    private void SetDirty()
    {
        if (graphic == null)
        {
            graphic = GetComponent<Graphic>();
        }

        if (graphic != null)
        {
            graphic.SetMaterialDirty();
        }
    }

    private void Release()
    {
        if (material != null)
        {
            if (Application.isPlaying)
            {
                Destroy(material);
            }
            else
            {
                DestroyImmediate(material);
            }
        }

        material = null;
        source = null;
    }
}
