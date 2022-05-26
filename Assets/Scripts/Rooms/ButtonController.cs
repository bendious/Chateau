using UnityEngine;


[DisallowMultipleComponent, RequireComponent(typeof(Collider2D))]
public class ButtonController : MonoBehaviour, IInteractable, IKey
{
	[SerializeField]
	private bool m_reusable = false;


	public IUnlockable Lock { get; set; }

	public bool IsInPlace { get; set; }


	public bool CanInteract(KinematicCharacter interactor) => m_reusable || !IsInPlace;

	public void Interact(KinematicCharacter interactor, bool reverse)
	{
		Use();
		if (!m_reusable)
		{
			Deactivate();
		}
	}


	public void Use()
	{
		Lock.Unlock(this);
	}

	public void Deactivate()
	{
		m_reusable = false;

		// leave non-item keys in place, just turning off their light/text
		GetComponent<UnityEngine.Rendering.Universal.Light2D>().enabled = false;

		// update color
		GetComponent<SpriteRenderer>().color = Color.black; // TODO: expose?
	}
}
