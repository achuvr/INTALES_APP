using TMPro;
using UnityEngine;

/// <summary>
/// お知らせの入力欄のカーソル（2026-11-10。定型文のため。アプリの送信画面 SpaceNoticeComposer で使う）。
///  ・押して入力をはじめたときは、全体を選ばずに文の最後から続けて打てるようにする
///    （TMP の既定の「全体を選ぶ」だと、定型文を入れたあとに1文字打っただけで全部消えてしまうため）
///  ・定型文を足したときは FocusAt で決めた位置（「」の中）にカーソルを置いて入力をはじめる。
///    TMP は入力をはじめるとき、入力欄のカーソル位置をスマホのキーボードにも渡す
/// </summary>
public class NoticeInputCaret : MonoBehaviour
{
    private TMP_InputField _input;
    private int _placeAt = -1;

    public static NoticeInputCaret Attach(TMP_InputField input)
    {
        input.onFocusSelectAll = false;
        var c = input.gameObject.AddComponent<NoticeInputCaret>();
        c._input = input;
        input.onSelect.AddListener(_ => c.OnSelected());
        return c;
    }

    /// <summary>index の位置にカーソルを置いて入力をはじめる（スマホならキーボードが出る）</summary>
    public void FocusAt(int index)
    {
        if (_input == null) return;
        _placeAt = Mathf.Clamp(index, 0, (_input.text ?? "").Length);
        _input.stringPosition = _placeAt; // もう選ばれていて OnSelected が呼ばれない場合のため
        _input.ActivateInputField();
    }

    /// <summary>入力欄が選ばれたとき（入力をはじめる直前）</summary>
    private void OnSelected()
    {
        int len = (_input.text ?? "").Length;
        int at = _placeAt >= 0 ? Mathf.Min(_placeAt, len) : len;
        _placeAt = -1;
        _input.stringPosition = at;
    }
}
