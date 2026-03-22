using System;
using UnityEditor.VersionControl;
using UnityEngine;
using UnityEngine.EventSystems;

public class ReloadUserData : SingletonBehaviour<ReloadUserData>
{

    private EventTrigger _eventTrigger;

    private void Awake()
    {
        _eventTrigger = GetComponent<EventTrigger>();
    }

    public async void Reload()
    {
        try
        {
            Debug.Log("Reload UserData");
            _eventTrigger.enabled = false;
            AssetsDatabase.instance.LoadingPanel.SetActive(true);
            Debug.Log("Loading ON");
        
            await UserDataManager.instance.FetchUserDataByUIDForReload();
            Debug.Log("end await");
            GameObject.FindObjectOfType<CharacterPageManager>().ChangePage();
            Debug.Log("find cpm");

            AssetsDatabase.instance.LoadingPanel.SetActive(false);
            _eventTrigger.enabled = true;
        }
        catch (Exception e)
        {
            throw; // TODO 例外の処理
        }
    }

}
