using UnityEngine;


// TODO: merge w/ LockController?
[RequireComponent(typeof(Collider2D))]
public class ButtonController : MonoBehaviour, IInteractable, IUnlockable
{
	public bool m_reusable = false;

	public GameObject Parent { get; set; }


	private bool m_locked = true;


	public bool CanInteract(KinematicCharacter interactor) => m_locked;

	public void Interact(KinematicCharacter interactor)
	{
		Unlock(gameObject);
	}

	public void SpawnKeys(RoomController lockRoom, RoomController[] keyRooms)
	{
		// no-op; we are our own key
	}

	public bool IsKey(GameObject obj)
	{
		return obj == gameObject;
	}

	public bool Unlock(GameObject key)
	{
		bool fullyUnlocked = Parent.GetComponent<IUnlockable>().Unlock(key);
		m_locked = m_reusable && !fullyUnlocked;
		if (m_locked)
		{
			return false;
		}

		// update color
		// NOTE that LockController.DeactivateKey() also turns off our light
		GetComponent<SpriteRenderer>().color = Color.black; // TODO: expose?

		return true;
	}
}
