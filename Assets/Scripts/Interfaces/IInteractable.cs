using UnityEngine;


public interface IInteractable
{
	public Component Component => this as Component;


	public bool CanInteract(KinematicCharacter interactor) => true;
	public bool CanInteractReverse(KinematicCharacter interactor) => false;

	public float Priority(KinematicCharacter interactor) => 1.0f;

	public void Interact(KinematicCharacter interactor, bool reverse);
}
