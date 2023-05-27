using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractRotate : MonoBehaviour, IInteractable, IKey
{
	[SerializeField] private GameObject m_rotator;
	[SerializeField] private float m_degreesIncrement = 30.0f;

	[SerializeField] private float m_autoInteractSec = -1.0f;
	[SerializeField] private InteractRotate m_autoInteractChild;
	[SerializeField] private float m_avatarInteractTimeout = 12.0f; // TODO: derive from m_degreesIncrement? player-facing messaging?

	[SerializeField] private WeightedObject<AudioClip>[] m_sfx;
	[SerializeField] private WeightedObject<AudioCollection>[] m_sfxGroups;


	public IUnlockable Lock { get; set; }

	public bool IsInPlace
	{
		get => m_rotator.transform.rotation.eulerAngles.z.FloatEqualDegrees(RotationCorrectDegrees, m_degreesIncrement * 0.5f);
		set { }
	}


	public float RotationCorrectDegrees { private get; set; }


	private AudioSource m_audio;

	private AudioCollection m_sfxGroup;

	private float m_lastAvatarInteractTime = float.MinValue;


	private void Start()
	{
		m_audio = GetComponentInParent<AudioSource>();
		if (m_sfxGroups.Length > 0)
		{
			m_sfxGroup = m_sfxGroups.RandomWeighted(); // TODO: match between different InteractRotates w/ the same root object
		}

		if (m_rotator == null)
		{
			m_rotator = gameObject;
		}
		Debug.Assert(360.0f.Modulo(m_degreesIncrement).FloatEqual(0.0f), "Uneven rotation increment?");
		m_rotator.transform.rotation = Quaternion.Euler(0.0f, 0.0f, m_degreesIncrement * Random.Range(0, Mathf.RoundToInt(360 / m_degreesIncrement)));

		if (m_autoInteractSec > 0.0f)
		{
			StartCoroutine(AutoInteract(m_autoInteractSec));
			if (m_autoInteractChild != null)
			{
				// if our auto-ticking is enabled, our children's should be disabled since we control them directly
				m_autoInteractChild.m_autoInteractSec = float.MinValue;
			}
		}
	}


	public bool CanInteract(KinematicCharacter interactor) => enabled;

	public bool CanInteractReverse(KinematicCharacter interactor) => CanInteract(interactor);

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		if (reverse && m_autoInteractChild != null && m_rotator.transform.rotation.eulerAngles.z.FloatEqual(0.0f))
		{
			m_autoInteractChild.Interact(interactor, reverse);
		}
		m_rotator.transform.rotation *= Quaternion.Euler(0.0f, 0.0f, reverse ? m_degreesIncrement : -m_degreesIncrement); // NOTE the reversal of the expected "reverse" semantics due to clockwise clock rotation // TODO: parameterize?
		if (!reverse && m_autoInteractChild != null && m_rotator.transform.rotation.eulerAngles.z.FloatEqual(0.0f))
		{
			m_autoInteractChild.Interact(interactor, reverse);
		}

		if (interactor is AvatarController)
		{
			m_lastAvatarInteractTime = Time.time;
		}
		if (m_lastAvatarInteractTime + m_avatarInteractTimeout >= Time.time) // TODO: also check interact times on parent interactor(s)?
		{
			(Lock as LockController).CheckInput();
		}

		if (m_sfxGroup != null)
		{
			m_audio.PlayOneShot(m_sfxGroup.m_collection.RandomWeighted());
		}
		else if (m_sfx.Length > 0)
		{
			m_audio.PlayOneShot(m_sfx.RandomWeighted());
		}
	}


	public void SetCombination(LockController.CombinationSet set, int[] combination, int optionIndex, int indexCorrect, int startIndex, int endIndex, bool useSprites)
	{
		RotationCorrectDegrees = -360.0f * indexCorrect / set.m_options.Length; // NOTE the negative due to clockwise clock rotation // TODO: parameterize?
		if (!isActiveAndEnabled)
		{
			m_rotator.transform.rotation = Quaternion.Euler(0.0f, 0.0f, RotationCorrectDegrees);
		}
	}

	public void Use()
	{
		Debug.Assert(false, "Trying to Use() an InteractRotate.");
	}

	public void Deactivate()
	{
		GetComponent<UnityEngine.Rendering.Universal.Light2D>().enabled = false;
		enabled = false;
	}


	private System.Collections.IEnumerator AutoInteract(float seconds)
	{
		yield return new WaitForSeconds(Random.Range(0.0f, seconds));

		WaitForSeconds wait = new(seconds);
		while (enabled && m_autoInteractSec > 0.0f)
		{
			Interact(null, false);
			yield return wait;
		}
	}
}
