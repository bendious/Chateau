using UnityEngine;


public class KeyStatic : MonoBehaviour, IKey
{
	public IUnlockable Lock { get; set; }

	public bool IsInPlace { get; set; }


	public void Use()
	{
		Debug.Assert(false);
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
