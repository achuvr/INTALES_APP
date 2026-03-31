using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// プレビュー画像のドラッグ移動ハンドラ
/// </summary>
public class EventImageDragHandler : MonoBehaviour,
    IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    private EventImagePreview _preview;
    private RectTransform _imgRT;
    private Vector2 _startPointer;
    private Vector2 _startAnchor;

    public void Init(EventImagePreview preview, RectTransform imgRT)
    {
        _preview = preview;
        _imgRT = imgRT;
        // raycastTargetを有効にして操作を受け取る
        var img = GetComponent<UnityEngine.UI.Image>();
        if (img != null) img.raycastTarget = true;
    }

    public void OnPointerDown(PointerEventData e)
    {
        _startPointer = e.position;
        _startAnchor = _imgRT.anchoredPosition;
    }

    public void OnDrag(PointerEventData e)
    {
        Vector2 delta = e.position - _startPointer;
        _imgRT.anchoredPosition = _startAnchor + delta;
    }

    public void OnPointerUp(PointerEventData e)
    {
        // ほぼ動いていない場合はタップ→閉じる処理は暗幕側に委ねる
    }
}