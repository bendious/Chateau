using UnityEngine;


[RequireComponent(typeof(Collider2D))]
public class DoorInteract : MonoBehaviour, IInteractable
{
	public string m_sceneName; // TODO: less error-prone type?


	public bool CanInteract(KinematicCharacter interactor) => !string.IsNullOrEmpty(m_sceneName);

	public void Interact(KinematicCharacter interactor)
	{
		GameController.Instance.LoadScene(m_sceneName);
	}

	public void Detach(bool noAutoReplace)
	{
		throw new System.NotImplementedException();
	}
}
