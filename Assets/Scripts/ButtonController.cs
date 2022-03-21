using UnityEngine;


// TODO: merge w/ LockController?
[RequireComponent(typeof(Collider2D))]
public class ButtonController : MonoBehaviour, IInteractable, IUnlockable
{
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

	public void Unlock(GameObject key)
	{
		m_locked = false;
		Parent.GetComponent<IUnlockable>().Unlock(key);

		// update color
		// NOTE that LockController.DeactivateKey() also turns off our light
		GetComponent<SpriteRenderer>().color = Color.gray; // TODO: expose?

		// TODO: SFX in case lock is far away
	}
}
