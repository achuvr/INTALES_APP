using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Firebase.Firestore;
using Firebase.Extensions;
using UnityEngine.SceneManagement;

public class CallMethodFromQR : MonoBehaviour
{
    public async UniTask LevelUp(int upLevel)
    {
        var currentSelectCharacterNumber = UserDataManager.instance.CurrentSelectCharacterNumber;
        var db = FirebaseFirestore.DefaultInstance;
        var uid = UserDataManager.instance.UID;
        CollectionReference charactersRef = db.Collection("users").Document(uid).Collection("characters");

        charactersRef.Limit(1).GetSnapshotAsync().ContinueWithOnMainThread(async task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError("エラーが発生しました: " + task.Exception);
                return;
            }

            QuerySnapshot snapshot = task.Result;
            if (snapshot.Count > 0)
            {
                Dictionary<string, object> characterData = new Dictionary<string, object>
                {
                    {
                        "lv", UserDataManager.instance.UserData.Characters[currentSelectCharacterNumber].Level + upLevel
                    },
                };
                DocumentReference docRef = db
                    .Collection("users")
                    .Document(uid)
                    .Collection("characters")
                    .Document(currentSelectCharacterNumber.ToString());

                // データの書き込み (SetAsyncを使用)
                await docRef.SetAsync(characterData, SetOptions.MergeAll).ContinueWithOnMainThread(task =>
                {
                    if (task.IsFaulted)
                    {
                        Debug.LogError("書き込みエラー: " + task.Exception);
                    }
                    else
                    {
                        Debug.Log("サブコレクションにデータを追加しました！");
                    }
                });
                
                // データの更新
                docRef = db
                    .Collection("users")
                    .Document(uid)
                    .Collection("characters")
                    .Document(currentSelectCharacterNumber.ToString());
                var snapshotChara = await docRef.GetSnapshotAsync();
                Character character = snapshotChara.ConvertTo<Character>();
                
                Debug.Log($"[character] {uid} => " + $"{character.Name}, {character.Job}, {character.Element}, {character.Level}");
                UserDataManager.instance.UserData.Characters[currentSelectCharacterNumber].Level = character.Level;
                AssetsDatabase.instance.PlayLevelUpSE(); // レベルアップSEを再生
                End();

            }
        });
    }
    
    public async UniTask Atk()
    {
        var currentSelectCharacterNumber = UserDataManager.instance.CurrentSelectCharacterNumber;
        var db = FirebaseFirestore.DefaultInstance;
        var uid = UserDataManager.instance.UID;
        CollectionReference charactersRef = db.Collection("users").Document(uid).Collection("characters");

        charactersRef.Limit(1).GetSnapshotAsync().ContinueWithOnMainThread(async task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError("エラーが発生しました: " + task.Exception);
                return;
            }

            QuerySnapshot snapshot = task.Result;
            if (snapshot.Count > 0)
            {
                Dictionary<string, object> data = new Dictionary<string, object>
                {
                    {
                        "atk_coupon", UserDataManager.instance.UserData.ATKCoupon + 1
                    },
                };
                
                var db = FirebaseFirestore.DefaultInstance;
                var uid = UserDataManager.instance.UID;
                var docRef = db.Collection("users").Document(uid);
                await docRef.SetAsync(data, SetOptions.MergeAll);
                
                // データの更新
                DocumentSnapshot ds = await docRef.GetSnapshotAsync();
                var userData = ds.ConvertTo<UserData>();
                UserDataManager.instance.SetUserData(userData);
                Debug.Log("ATKクーポンを入手");
                End();
            }
        });
    }
    
    public async UniTask Drink()
    {
        var currentSelectCharacterNumber = UserDataManager.instance.CurrentSelectCharacterNumber;
        var db = FirebaseFirestore.DefaultInstance;
        var uid = UserDataManager.instance.UID;
        CollectionReference charactersRef = db.Collection("users").Document(uid).Collection("characters");

        charactersRef.Limit(1).GetSnapshotAsync().ContinueWithOnMainThread(async task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError("エラーが発生しました: " + task.Exception);
                return;
            }

            QuerySnapshot snapshot = task.Result;
            if (snapshot.Count > 0)
            {
                Dictionary<string, object> data = new Dictionary<string, object>
                {
                    {
                        "drink_coupon", UserDataManager.instance.UserData.DrinkCoupon + 1
                    },
                };
                
                var db = FirebaseFirestore.DefaultInstance;
                var uid = UserDataManager.instance.UID;
                var docRef = db.Collection("users").Document(uid);
                await docRef.SetAsync(data, SetOptions.MergeAll);
                
                // データの更新
                DocumentSnapshot ds = await docRef.GetSnapshotAsync();
                var userData = ds.ConvertTo<UserData>();
                UserDataManager.instance.SetUserData(userData);
                Debug.Log("Drinkクーポンを入手");
                End();
            }
        });
    }
    
    public async UniTask Coffee()
    {
        var currentSelectCharacterNumber = UserDataManager.instance.CurrentSelectCharacterNumber;
        var db = FirebaseFirestore.DefaultInstance;
        var uid = UserDataManager.instance.UID;
        CollectionReference charactersRef = db.Collection("users").Document(uid).Collection("characters");

        charactersRef.Limit(1).GetSnapshotAsync().ContinueWithOnMainThread(async task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError("エラーが発生しました: " + task.Exception);
                return;
            }

            QuerySnapshot snapshot = task.Result;
            if (snapshot.Count > 0)
            {
                Dictionary<string, object> data = new Dictionary<string, object>
                {
                    {
                        "coffee_coupon", UserDataManager.instance.UserData.CoffeeCoupon + 1
                    },
                };
                
                var db = FirebaseFirestore.DefaultInstance;
                var uid = UserDataManager.instance.UID;
                var docRef = db.Collection("users").Document(uid);
                await docRef.SetAsync(data, SetOptions.MergeAll);
                
                // データの更新
                DocumentSnapshot ds = await docRef.GetSnapshotAsync();
                var userData = ds.ConvertTo<UserData>();
                UserDataManager.instance.SetUserData(userData);
                Debug.Log("Coffeeクーポンを入手");
                End();
            }
        });
    }
    
    public async UniTask Five()
    {
        var currentSelectCharacterNumber = UserDataManager.instance.CurrentSelectCharacterNumber;
        var db = FirebaseFirestore.DefaultInstance;
        var uid = UserDataManager.instance.UID;
        CollectionReference charactersRef = db.Collection("users").Document(uid).Collection("characters");

        charactersRef.Limit(1).GetSnapshotAsync().ContinueWithOnMainThread(async task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError("エラーが発生しました: " + task.Exception);
                return;
            }

            QuerySnapshot snapshot = task.Result;
            if (snapshot.Count > 0)
            {
                Dictionary<string, object> data = new Dictionary<string, object>
                {
                    {
                        "five_coupon", UserDataManager.instance.UserData.FiveCoupon + 1
                    },
                };
                
                var db = FirebaseFirestore.DefaultInstance;
                var uid = UserDataManager.instance.UID;
                var docRef = db.Collection("users").Document(uid);
                await docRef.SetAsync(data, SetOptions.MergeAll);
                
                // データの更新
                DocumentSnapshot ds = await docRef.GetSnapshotAsync();
                var userData = ds.ConvertTo<UserData>();
                UserDataManager.instance.SetUserData(userData);
                Debug.Log("5クーポンを入手");
                End();
            }
        });
    }
    
    public async UniTask Seven()
    {
        var currentSelectCharacterNumber = UserDataManager.instance.CurrentSelectCharacterNumber;
        var db = FirebaseFirestore.DefaultInstance;
        var uid = UserDataManager.instance.UID;
        CollectionReference charactersRef = db.Collection("users").Document(uid).Collection("characters");

        charactersRef.Limit(1).GetSnapshotAsync().ContinueWithOnMainThread(async task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError("エラーが発生しました: " + task.Exception);
                return;
            }

            QuerySnapshot snapshot = task.Result;
            if (snapshot.Count > 0)
            {
                Dictionary<string, object> data = new Dictionary<string, object>
                {
                    {
                        "seven_coupon", UserDataManager.instance.UserData.SevenCoupon + 1
                    },
                };
                
                var db = FirebaseFirestore.DefaultInstance;
                var uid = UserDataManager.instance.UID;
                var docRef = db.Collection("users").Document(uid);
                await docRef.SetAsync(data, SetOptions.MergeAll);
                
                // データの更新
                DocumentSnapshot ds = await docRef.GetSnapshotAsync();
                var userData = ds.ConvertTo<UserData>();
                UserDataManager.instance.SetUserData(userData);
                Debug.Log("7クーポンを入手");
                End();
            }
        });
    }
    
    public async UniTask NewCharacter()
    {
        SceneLoader.instance.MergeScene("New");
    }

    // ReSharper disable Unity.PerformanceAnalysis
    public void End()
    {
        var qr = GameObject.FindWithTag("QR").gameObject;
        Destroy(qr.gameObject);
        ReloadUserData.instance.Reload();
    }

    public void EndFromButton()
    {
        var qr = GameObject.FindWithTag("QR").gameObject;
        Destroy(qr.gameObject); 
    }
}
