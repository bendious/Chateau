using TMPro;
using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D), typeof(SpriteRenderer), typeof(AudioSource))]
public class InteractToggle : MonoBehaviour, IInteractable, IKey
{
	[SerializeField] private WeightedObject<AudioClip>[] m_sfx;


	public IUnlockable Lock { get; set; }
	public bool IsInPlace { get => m_idxCurrent == m_idxCorrect; set { } }

	private TMP_Text m_text;
	private SpriteRenderer m_renderer;
	private AudioSource m_audioSource;

	private LockController.CombinationSet m_toggleSet;
	private int m_optionIndex;
	private int m_idxCurrent = -1;

	private int m_idxCorrect;


	private void Awake()
	{
		m_text = GetComponentInChildren<TMP_Text>();
		m_renderer = GetComponent<SpriteRenderer>();
		m_audioSource = GetComponent<AudioSource>();
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "UNT0001:Empty Unity message", Justification = "Required to ensure enable/disable checkbox appears in the Inspector")]
	private void Start()
	{
	}


	public bool CanInteract(KinematicCharacter interactor) => enabled && m_toggleSet != null && m_toggleSet.m_options.Length > 1;
	public bool CanInteractReverse(KinematicCharacter interactor) => CanInteract(interactor);

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		m_idxCurrent = (m_idxCurrent + (reverse ? -1 : 1)).Modulo(m_toggleSet.m_options.Length);
		UpdateVisuals();

		(Lock as LockController).CheckInput();

		m_audioSource.clip = m_sfx.RandomWeighted();
		m_audioSource.Play();
	}


	public void SetCombination(LockController.CombinationSet set, int[] combination, int optionIndex, int indexCorrect, int startIndex, int endIndex, bool useSprites)
	{
		m_toggleSet = set;
		m_optionIndex = useSprites ? -1 : optionIndex;
		m_idxCorrect = indexCorrect;
		m_idxCurrent = isActiveAndEnabled ? Random.Range(0, m_toggleSet.m_options.Length) : m_idxCorrect;

		UpdateVisuals();
	}

	public void Use() => Debug.LogError("Trying to Use() an InteractToggle.");

	public void Deactivate()
	{
		// force correct state in case we were unlocked via console command / etc.
		m_idxCurrent = m_idxCorrect;
		UpdateVisuals();

		enabled = false;
		GetComponentInChildren<UnityEngine.Rendering.Universal.Light2D>().enabled = false;
	}


	private void UpdateVisuals()
	{
		if (m_optionIndex < 0)
		{
			Sprite sprite = m_toggleSet.m_options[m_idxCurrent].m_sprite;
			m_renderer.sprite = sprite;
			m_renderer.drawMode = sprite.border == Vector4.zero ? SpriteDrawMode.Simple : SpriteDrawMode.Sliced;
		}
		else
		{
			m_text.text = m_toggleSet.m_options[m_idxCurrent].m_strings[m_optionIndex];
		}
	}
}
