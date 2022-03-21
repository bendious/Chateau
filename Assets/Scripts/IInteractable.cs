using UnityEngine;


public interface IInteractable
{
	public Component Component => this as Component;


	public bool CanInteract(KinematicCharacter interactor) => true;

	public void Interact(KinematicCharacter interactor);
}
