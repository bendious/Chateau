using UnityEngine;


public interface IInteractable
{
	public GameObject Object { get; }


	public bool CanInteract(KinematicCharacter interactor) => true;

	public void Interact(KinematicCharacter interactor) => interactor.AttachItem(this);

	public void Detach(bool noAutoReplace);
}
