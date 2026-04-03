using Firebase.Firestore;

/// <summary>
/// キャラクターのインベントリに保存する軽量参照。
/// Firestore の users/{uid}/characters/{charId}/inventory 配列に格納される。
/// 実際のアイテムデータは item/{Job}/items/{ItemId} から取得する。
/// </summary>
[FirestoreData, System.Serializable]
public class InventoryRef
{
    public InventoryRef() {}

    [FirestoreProperty("job")]
    public string Job { get; set; }

    [FirestoreProperty("item_id")]
    public string ItemId { get; set; }

    /// <summary>Firestore ドキュメントパスを構築</summary>
    public string FirestorePath => $"item/{Job}/items/{ItemId}";
}
