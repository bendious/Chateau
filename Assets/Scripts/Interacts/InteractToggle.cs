using System.Linq;
using TMPro;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractToggle : MonoBehaviour, IInteractable, IKey
{
	public IUnlockable Lock { get; set; }
	public bool IsInPlace { get => m_charIdx == m_idxCorrect; set => IsInPlace = IsInPlace/*TODO?*/; }

	private TMP_Text m_text;
	private SpriteRenderer m_renderer;

	private LockController.CombinationSet m_toggleSet;
	private bool m_useSprites;
	private int m_charIdx = -1;

	private int m_idxCorrect;


	private void Awake()
	{
		m_text = GetComponentInChildren<TMP_Text>();
		m_renderer = GetComponent<SpriteRenderer>();
	}


	public bool CanInteract(KinematicCharacter interactor) => m_toggleSet != null && m_toggleSet.m_string.Length > 1;
	public bool CanInteractReverse(KinematicCharacter interactor) => CanInteract(interactor);

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		m_charIdx = (m_charIdx + (reverse ? -1 : 1)).Modulo(m_toggleSet.m_string.Length);
		if (m_useSprites)
		{
			SetSpriteAndMode(m_toggleSet.m_sprites[m_charIdx]);
		}
		else
		{
			m_text.text = m_toggleSet.m_string[m_charIdx].ToString();
		}

		(Lock as LockController).CheckInput();
	}

	public void Use()
	{
		Debug.Assert(false);
	}

	public void SetToggleText(LockController.CombinationSet set, bool useSprites, int indexCorrect)
	{
		m_toggleSet = set;
		m_useSprites = useSprites;
		Debug.Assert(!useSprites || set.m_sprites.Length == set.m_string.Length);
		m_idxCorrect = indexCorrect;

		if (m_toggleSet != null)
		{
			m_charIdx = 0;
			if (m_useSprites)
			{
				SetSpriteAndMode(m_toggleSet.m_sprites.First());
			}
			else
			{
				m_text.text = m_toggleSet.m_string.First().ToString();
			}
		}
	}

	public void Deactivate()
	{
		SetToggleText(null, false, -1);
		GetComponent<UnityEngine.Rendering.Universal.Light2D>().enabled = false;
	}


	private void SetSpriteAndMode(Sprite sprite)
	{
		m_renderer.sprite = sprite;
		m_renderer.drawMode = sprite.border == Vector4.zero ? SpriteDrawMode.Simple : SpriteDrawMode.Sliced;
	}
}
