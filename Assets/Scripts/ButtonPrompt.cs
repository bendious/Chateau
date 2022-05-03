using System;
using System.Linq;
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
		public Sprite m_sprite;
	}
	public PromptInfo[] m_infos;


	public void SetSprite()
	{
		string currentSchemeName = m_playerInput.currentControlScheme;
		Sprite currentSprite = m_infos.First(info => Array.Exists(info.m_controlSchemeNames, schemeName => schemeName == currentSchemeName)).m_sprite;
		GetComponent<SpriteRenderer>().sprite = currentSprite;
	}
}
