using UnityEngine;


[DisallowMultipleComponent]
public class KeyStatic : MonoBehaviour, IKey
{
	public IUnlockable Lock { get; set; }

	public bool IsInPlace { get; set; }


	private void Awake()
	{
		IsInPlace = true;
	}


	public void Use()
	{
		Debug.Assert(false, "Trying to Use() a KeyStatic.");
	}

	public void Deactivate()
	{
		GetComponent<UnityEngine.Rendering.Universal.Light2D>().enabled = false;
		Canvas canvas = GetComponentInChildren<Canvas>();
		if (canvas != null)
		{
			canvas.enabled = false;
		}
	}
}
