using UnityEngine;

public class LaunchActivate : MonoBehaviour
{
    public void Awake()
    {
        transform.GetChild(0).gameObject.SetActive(true);
    }
}
