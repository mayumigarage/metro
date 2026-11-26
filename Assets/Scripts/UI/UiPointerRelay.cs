using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Forwards mouse / touch events on a UI graphic to plain callbacks.</summary>
public sealed class UiPointerRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public Action Entered;
    public Action Exited;
    public Action<PointerEventData> Clicked;

    public void OnPointerEnter(PointerEventData eventData)
    {
        Entered?.Invoke();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Exited?.Invoke();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Clicked?.Invoke(eventData);
    }
}
