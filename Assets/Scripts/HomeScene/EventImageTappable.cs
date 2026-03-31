using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class EventImageTappable : MonoBehaviour, IPointerClickHandler
{
    private Sprite _sprite;

    public void Setup(Sprite sprite)
    {
        _sprite = sprite;
        var img = GetComponent<Image>();
        if (img != null) img.raycastTarget = true;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.dragging) return;
        if (_sprite != null && EventImagePreview.instance != null)
            EventImagePreview.instance.Show(_sprite);
    }
}