using System.Linq;
using TMPro;
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


	public void SetCombination(LockController.CombinationSet set, int[] combination, int optionIndex, int indexCorrect, int startIndex, int endIndex, bool useSprites) => GetComponentInChildren<TMP_Text>().text = IKey.CombinationToText(set, combination, optionIndex, startIndex, endIndex).Aggregate((str, strNew) => str + strNew); // TODO: embed w/i (short) flavor text?

	public void Use()
	{
		Debug.Assert(false, "Trying to Use() a KeyStatic.");
	}

	public void Deactivate()
	{
		GetComponent<UnityEngine.Rendering.Universal.Light2D>().enabled = false;
		TMP_Text text = GetComponentInChildren<TMP_Text>();
		if (text != null)
		{
			text.enabled = false;
		}
	}
}
