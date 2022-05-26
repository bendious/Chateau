using System;
using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;


[DisallowMultipleComponent]
public class ButtonPrompt : MonoBehaviour
{
	public PlayerInput m_playerInput;

	[Serializable]
	public struct PromptInfo
	{
		public string[] m_controlSchemeNames; // TODO: figure out how to get dropdown menu like in PlayerInput?
		public UnityEngine.Object[] m_sprites;
	}
	public PromptInfo[] m_infos;


	private int m_index = 0;


	public IEnumerator Start() // TODO: listen for control scheme changes if that is ever allowed
	{
		if (m_playerInput == null)
		{
			yield return new WaitUntil(() => GameController.Instance.m_avatars.Count > 0);
			m_playerInput = GameController.Instance.m_avatars.First().GetComponent<PlayerInput>();
		}
		SetSpriteInternal();
	}

	public void SetSprite(int index)
	{
		if (index == m_index)
		{
			return;
		}
		m_index = index;
		SetSpriteInternal();
	}


	private void SetSpriteInternal()
	{
		string currentSchemeName = m_playerInput.currentControlScheme;
		UnityEngine.Object currentSprite = m_infos.First(info => Array.Exists(info.m_controlSchemeNames, schemeName => schemeName == currentSchemeName)).m_sprites[m_index];

		SpriteRenderer renderer = GetComponent<SpriteRenderer>();
		if (renderer != null)
		{
			renderer.sprite = (Sprite)currentSprite;
		}

		TMP_Text text = GetComponent<TMP_Text>();
		if (text != null)
		{
			text.spriteAsset = (TMP_SpriteAsset)currentSprite;
		}
	}
}
