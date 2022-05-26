using System.Linq;
using TMPro;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractToggle : MonoBehaviour, IInteractable, IKey
{
	public string TextCurrent => m_text.text;

	public IUnlockable Lock { get; set; }
	public bool IsInPlace { get => TextCurrent == m_textCorrect; set => IsInPlace = IsInPlace/*TODO?*/; }

	private TMP_Text m_text;
	private string m_toggleText;
	private int m_charIdx = -1;

	private string m_textCorrect;


	private void Awake()
	{
		m_text = GetComponentInChildren<TMP_Text>();
	}


	public bool CanInteract(KinematicCharacter interactor) => m_toggleText != null && m_toggleText.Length > 1;
	public bool CanInteractReverse(KinematicCharacter interactor) => CanInteract(interactor);

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		m_charIdx = (m_charIdx + (reverse ? -1 : 1)).Modulo(m_toggleText.Length);
		m_text.text = m_toggleText[m_charIdx].ToString();

		(Lock as LockController).CheckInput();
	}

	public void Use()
	{
		Debug.Assert(false);
	}

	public void SetToggleText(string text, string textCorrect)
	{
		m_toggleText = text;
		m_textCorrect = textCorrect;
		if (m_toggleText != null)
		{
			m_charIdx = 0;
			m_text.text = m_toggleText.First().ToString();
		}
	}

	public void Deactivate()
	{
		SetToggleText(null, null);
		GetComponent<UnityEngine.Rendering.Universal.Light2D>().enabled = false;
	}
}
