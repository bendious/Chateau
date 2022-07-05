using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class InteractRotate : MonoBehaviour, IInteractable, IKey
{
	[SerializeField] private GameObject m_rotator;
	[SerializeField] private float m_degreesIncrement = 30.0f;


	public IUnlockable Lock { get; set; }

	public bool IsInPlace
	{
		get => m_rotator.transform.rotation.eulerAngles.z.FloatEqualDegrees(RotationCorrectDegrees, m_degreesIncrement * 0.5f);
		set => IsInPlace = IsInPlace; // TODO?
	}


	public float RotationCorrectDegrees { private get; set; }


	private void Start()
	{
		if (m_rotator == null)
		{
			m_rotator = gameObject;
		}
		Debug.Assert(360.0f.Modulo(m_degreesIncrement).FloatEqual(0.0f), "Uneven rotation increment?");
		m_rotator.transform.rotation = Quaternion.Euler(0.0f, 0.0f, m_degreesIncrement * Random.Range(0, Mathf.RoundToInt(360 / m_degreesIncrement)));
	}


	public bool CanInteract(KinematicCharacter interactor) => enabled;

	public bool CanInteractReverse(KinematicCharacter interactor) => CanInteract(interactor);

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		m_rotator.transform.rotation *= Quaternion.Euler(0.0f, 0.0f, reverse ? m_degreesIncrement : -m_degreesIncrement); // NOTE the reversal of the expected "reverse" semantics due to clockwise clock rotation // TODO: parameterize?
		(Lock as LockController).CheckInput();
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
}