using UnityEngine;


// TODO: merge w/ LockController?
public class ButtonController : MonoBehaviour, IInteractable
{
	public IUnlockable Parent { get; set; }


	private bool m_locked = true;


	public bool CanInteract(KinematicCharacter interactor) => m_locked;

	public void Interact(KinematicCharacter interactor)
	{
		m_locked = false;
		Parent.Unlock(gameObject);

		// update color
		// NOTE that LockController.DeactivateKey() also turns off our light
		GetComponent<SpriteRenderer>().color = Color.gray; // TODO: expose?

		// TODO: SFX in case lock is far away
	}

	public void Detach(bool noAutoReplace)
	{
		UnityEngine.Assertions.Assert.IsTrue(false); // this should never be called
	}
}
