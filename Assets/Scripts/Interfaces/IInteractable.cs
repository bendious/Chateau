using UnityEngine;


public interface IInteractable
{
	public Behaviour Component => this as Behaviour; // NOTE that returning as a Behaviour rather than Component allows access to the component enabled flag


	public bool CanInteract(KinematicCharacter interactor) => Component.enabled; // TODO: combine w/ Priority()?
	public bool CanInteractReverse(KinematicCharacter interactor) => false;

	public float Priority(KinematicCharacter interactor) => 1.0f;

	public void Interact(KinematicCharacter interactor, bool reverse);
}
