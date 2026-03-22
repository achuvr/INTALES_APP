using UnityEngine;

public class SingletonBehaviour<T> : MonoBehaviour where T : SingletonBehaviour<T>
{

	public static T instance { get; private set; }

	virtual protected void Awake()
	{ 
		if (instance == null) instance = (T)this;
		else Destroy(this);
	}

}
