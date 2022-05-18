using System;
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
		public UnityEngine.Object m_sprite;
	}
	public PromptInfo[] m_infos;


	public void Start() // TODO: listen for control scheme changes if that is ever allowed
	{
		string currentSchemeName = m_playerInput.currentControlScheme;
		UnityEngine.Object currentSprite = m_infos.First(info => Array.Exists(info.m_controlSchemeNames, schemeName => schemeName == currentSchemeName)).m_sprite;

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
