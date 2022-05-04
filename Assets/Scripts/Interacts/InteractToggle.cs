using System.Linq;
using TMPro;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractToggle : MonoBehaviour, IInteractable
{
	public string TextCurrent => m_text.text;


	private TMP_Text m_text;
	private string m_toggleText;
	private int m_charIdx = -1;

	private LockController m_lock;


	private void Awake()
	{
		m_text = GetComponentInChildren<TMP_Text>();
		m_lock = GetComponentInParent<LockController>();
	}


	public bool CanInteract(KinematicCharacter interactor) => m_toggleText != null && m_toggleText.Length > 1;

	public void Interact(KinematicCharacter interactor)
	{
		m_charIdx = (m_charIdx + 1) % m_toggleText.Length;
		m_text.text = m_toggleText[m_charIdx].ToString();

		m_lock.CheckInput();
	}

	public void SetToggleText(string text)
	{
		m_toggleText = text;
		if (m_toggleText != null)
		{
			m_charIdx = 0;
			m_text.text = m_toggleText.First().ToString();
		}
	}

	public void Deactivate()
	{
		SetToggleText(null);
		GetComponent<UnityEngine.Rendering.Universal.Light2D>().enabled = false;
	}
}
